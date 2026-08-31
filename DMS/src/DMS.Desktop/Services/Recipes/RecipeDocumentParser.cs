using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DMS.Core.Recipes;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace DMS.Desktop.Services.Recipes;

public sealed partial class RecipeDocumentParser
{
    private readonly RecipeNormalizationService _normalizer = new();

    public RecipeImportResult ParseSpray(string filePath)
    {
        return System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => ParseSprayPdf(filePath),
            ".docx" => ParseSprayDocx(filePath),
            _ => throw new NotSupportedException("Spray recipe import supports PDF and DOCX files.")
        };
    }

    public RecipeImportResult ParseScreenPrint(string filePath)
    {
        return System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".docx" => ParseScreenPrintDocx(filePath),
            ".pdf" => ParseScreenPrintPdf(filePath),
            _ => throw new NotSupportedException("Screen-print recipe import supports DOCX and text-based PDF files.")
        };
    }

    private RecipeImportResult ParseSprayPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var page = document.GetPage(1);
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new PositionedWord(
                word.Text.Trim(),
                word.BoundingBox.Left,
                word.BoundingBox.Right,
                word.BoundingBox.Bottom,
                word.BoundingBox.Top))
            .ToList();

        var contentText = string.Join(" ", words
            .OrderByDescending(word => word.CenterY)
            .ThenBy(word => word.Left)
            .Select(word => word.Text));

        var result = new RecipeImportResult
        {
            Kind = RecipeImportKind.SprayCoating,
            SourceFile = filePath,
            ArticleNumber = FindArticleNumber(contentText, words),
            HdNumber = FindHdNumber(contentText, words),
            Color = FindRowValueAfterLabel(words, "Barva:") ?? string.Empty,
            Device = FindRowValueAfterLabel(words, "Zařízení:") ?? string.Empty,
            GeneralNote = ExtractGeneralNote(contentText)
        };

        var layerHeaders = FindLayerHeaders(words);

        if (layerHeaders.Count == 0)
        {
            throw new InvalidDataException("No layer headers were found in the spray recipe PDF.");
        }

        var hdDigits = string.Concat(result.HdNumber.Where(char.IsDigit));
        var tableHeaderY = layerHeaders.Max(header => header.Y);
        var componentBandBottom = tableHeaderY - 180d;
        var footerBandBottom = tableHeaderY - 235d;

        const double layerHeaderInset = 20d;

        for (var index = 0; index < layerHeaders.Count; index++)
        {
            var header = layerHeaders[index];

            // HD-FOR-013-040 places the "1.", "2.", ... marker inside the
            // narrow quantity column near the LEFT edge of each layer.
            //
            // The previous midpoint calculation treated that marker as if it
            // were the layer centre. That cut off the right side of the
            // description cell. Example:
            //
            //   25 000 g. | Klar AQUA 410-90106-
            //              8
            //
            // "410-90106-" ended outside Lay10 while the wrapped "8" was
            // still inside, producing "Klar AQUA 8".
            //
            // The layer boundary is approximately 20 PDF points left of the
            // next layer marker. Using the same boundary for both adjacent
            // layers avoids overlaps and preserves the complete description
            // cell even when the whole form shifts on the page.
            var left = Math.Max(
                0d,
                header.X - layerHeaderInset);

            var right = index == layerHeaders.Count - 1
                ? page.Width
                : Math.Max(
                    left + 1d,
                    layerHeaders[index + 1].X - layerHeaderInset);

            var layerWords = words
                .Where(word => word.CenterX >= left && word.CenterX < right)
                .Where(word => word.CenterY < tableHeaderY - 3d && word.CenterY > footerBandBottom)
                .ToList();

            var layer = new RecipeLayer
            {
                LayerNumber = header.Number,
                KText = hdDigits.Length == 0
                    ? $"Lay{header.Number * 10:00}"
                    : $"HD{hdDigits}_Lay{header.Number * 10:00}"
            };

            var lines = GroupLines(layerWords);

            RecipeComponent? previousComponent = null;
            double previousDescriptionLeft = 0d;
            double previousComponentLineY = 0d;

            foreach (var line in lines)
            {
                var y = line.Average(word => word.CenterY);
                var lineText = JoinWords(line);

                if (y > componentBandBottom)
                {
                    if (TryParseComponentLine(
                            line,
                            out var grams,
                            out var description,
                            out var descriptionLeft))
                    {
                        previousComponent = new RecipeComponent
                        {
                            SourceText = description,
                            SourceGrams = grams
                        };

                        layer.Components.Add(previousComponent);
                        previousDescriptionLeft = descriptionLeft;
                        previousComponentLineY = y;
                        continue;
                    }

                    // A component description can wrap inside the same PDF
                    // table cell, for example:
                    //
                    // 25 000 g. Klar AQUA 410-90106-
                    //          8
                    //
                    // PdfPig exposes the second visual row as a separate line.
                    // Join it back to the preceding component when it starts
                    // in the same description column and is vertically close.
                    if (previousComponent is not null &&
                        TryParseComponentContinuation(
                            line,
                            previousDescriptionLeft,
                            previousComponentLineY,
                            out var continuation))
                    {
                        previousComponent.SourceText =
                            JoinWrappedComponentText(
                                previousComponent.SourceText,
                                continuation);

                        previousComponentLineY = y;
                        continue;
                    }

                    previousComponent = null;

                    if (lineText.Contains("VISKOZ", StringComparison.OrdinalIgnoreCase))
                    {
                        layer.TextItems.Add(lineText);
                    }
                }
                else
                {
                    previousComponent = null;

                    if (!string.IsNullOrWhiteSpace(lineText))
                    {
                        layer.TextItems.Add(lineText);
                    }
                }
            }

            layer.ProcessText = string.Join(" | ", layer.TextItems
                .Where(text =>
                    text.Contains("metaliz", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("láhev", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("lahev", StringComparison.OrdinalIgnoreCase)));

            layer.ProcessOnly =
                layer.Components.Count == 0 &&
                layer.TextItems.Any(text => text.Contains("metaliz", StringComparison.OrdinalIgnoreCase));

            _normalizer.NormalizeLayer(layer);
            result.Layers.Add(layer);
        }

        result.Layers = result.Layers
            .Where(layer =>
                layer.Components.Count > 0 ||
                layer.ProcessOnly ||
                layer.TextItems.Count > 0)
            .OrderBy(layer => layer.LayerNumber)
            .ToList();

        return result;
    }

    private RecipeImportResult ParseSprayDocx(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body
                   ?? throw new InvalidDataException("DOCX body is missing.");

        var allText = body.InnerText;
        var result = new RecipeImportResult
        {
            Kind = RecipeImportKind.SprayCoating,
            SourceFile = filePath,
            ArticleNumber = ArticleRegex().Match(allText) is { Success: true } article
                ? article.Groups["article"].Value
                : string.Empty,
            HdNumber = HdRegex().Match(allText) is { Success: true } hd
                ? $"{hd.Groups["a"].Value} {hd.Groups["b"].Value} {hd.Groups["c"].Value}"
                : string.Empty,
            GeneralNote = ExtractGeneralNote(allText)
        };

        var tables = body.Elements<Table>().ToList();
        var layerTable = tables.FirstOrDefault(table =>
            GetExpandedRows(table).Any(row =>
                row.Any(cell => cell.Contains("1. Vrstva", StringComparison.OrdinalIgnoreCase) ||
                                cell.Contains("1.Vrstva", StringComparison.OrdinalIgnoreCase))));

        if (layerTable is null)
        {
            throw new InvalidDataException("Layer table was not found in the spray DOCX.");
        }

        var rows = GetExpandedRows(layerTable);
        var headerIndex = rows.FindIndex(row =>
            row.Any(cell => cell.Contains("1. Vrstva", StringComparison.OrdinalIgnoreCase) ||
                            cell.Contains("1.Vrstva", StringComparison.OrdinalIgnoreCase)));

        if (headerIndex < 0)
        {
            throw new InvalidDataException("Layer header row was not found in the spray DOCX.");
        }

        var header = rows[headerIndex];
        var layerStarts = new List<(int Layer, int Start)>();

        for (var column = 0; column < header.Count; column++)
        {
            var match = Regex.Match(header[column], @"(?<layer>[1-9])\.?\s*Vrstva", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                layerStarts.Add((int.Parse(match.Groups["layer"].Value, CultureInfo.InvariantCulture), column));
            }
        }

        if (layerStarts.Count == 0)
        {
            throw new InvalidDataException("No layer columns were found in the spray DOCX.");
        }

        var hdDigits = string.Concat(result.HdNumber.Where(char.IsDigit));

        for (var i = 0; i < layerStarts.Count; i++)
        {
            var current = layerStarts[i];
            var end = i + 1 < layerStarts.Count ? layerStarts[i + 1].Start : header.Count;
            var layer = new RecipeLayer
            {
                LayerNumber = current.Layer,
                KText = hdDigits.Length == 0
                    ? $"Lay{current.Layer * 10:00}"
                    : $"HD{hdDigits}_Lay{current.Layer * 10:00}"
            };

            foreach (var row in rows.Skip(headerIndex + 1))
            {
                if (current.Start >= row.Count) continue;

                var cells = row
                    .Skip(current.Start)
                    .Take(Math.Max(1, end - current.Start))
                    .Where(cell => !string.IsNullOrWhiteSpace(cell))
                    .ToList();

                if (cells.Count == 0) continue;

                var line = string.Join(" ", cells).Trim();
                var amountCell = cells[0];

                if (TryParseDecimalGrams(amountCell, out var grams))
                {
                    var description = string.Join(" ", cells.Skip(1)).Trim();
                    if (description.Length > 0)
                    {
                        layer.Components.Add(new RecipeComponent
                        {
                            SourceText = description,
                            SourceGrams = grams
                        });
                        continue;
                    }
                }

                if (line.Contains("Viskoz", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("láhev", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("lahev", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("metaliz", StringComparison.OrdinalIgnoreCase))
                {
                    layer.TextItems.Add(line);
                }
            }

            layer.ProcessOnly =
                layer.Components.Count == 0 &&
                layer.TextItems.Any(text => text.Contains("metaliz", StringComparison.OrdinalIgnoreCase));
            layer.ProcessText = string.Join(" | ", layer.TextItems);
            _normalizer.NormalizeLayer(layer);

            if (layer.Components.Count > 0 || layer.ProcessOnly || layer.TextItems.Count > 0)
            {
                result.Layers.Add(layer);
            }
        }

        return result;
    }

    private RecipeImportResult ParseScreenPrintDocx(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var tables = document.MainDocumentPart?.Document?.Body?.Elements<Table>().ToList()
                     ?? new List<Table>();

        var recipeTable = tables.FirstOrDefault(table =>
            GetRows(table).Any(row =>
                row.Any(cell => cell.Contains("Rezepturnummer", StringComparison.OrdinalIgnoreCase) ||
                                cell.Contains("Číslo receptury", StringComparison.OrdinalIgnoreCase))));

        if (recipeTable is null)
        {
            throw new InvalidDataException("Recipe table was not found in the DOCX.");
        }

        var rows = GetRows(recipeTable);
        var recipeNumber = rows
            .Where(row => row.Count == 1)
            .Select(row => row[0].Trim())
            .FirstOrDefault(value => RecipeNumberRegex().IsMatch(value))
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipeNumber))
        {
            throw new InvalidDataException("Recipe number was not found in the DOCX.");
        }

        var result = new RecipeImportResult
        {
            Kind = RecipeImportKind.ScreenPrinting,
            SourceFile = filePath,
            RecipeNumber = recipeNumber,
            KText = $"Rezept {recipeNumber}"
        };

        var layer = new RecipeLayer
        {
            LayerNumber = 1,
            KText = result.KText,
            BaseQuantityGrams = 1000m
        };

        var headerIndex = rows.FindIndex(row =>
            row.Count >= 2 &&
            row[0].Contains("Barva", StringComparison.OrdinalIgnoreCase) &&
            row[1].Contains("Gram", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            throw new InvalidDataException("Component/grams table header was not found in the DOCX.");
        }

        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            if (!TryParseDecimalGrams(row[1], out var grams))
            {
                continue;
            }

            layer.Components.Add(new RecipeComponent
            {
                SourceText = row[0].Trim(),
                SourceGrams = grams
            });
        }

        if (layer.Components.Count == 0)
        {
            throw new InvalidDataException("The screen-print recipe contains no component rows.");
        }

        _normalizer.NormalizeLayer(layer);
        result.Layers.Add(layer);
        return result;
    }

    private RecipeImportResult ParseScreenPrintPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var lines = new List<string>();

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new PositionedWord(
                    word.Text.Trim(),
                    word.BoundingBox.Left,
                    word.BoundingBox.Right,
                    word.BoundingBox.Bottom,
                    word.BoundingBox.Top));

            lines.AddRange(GroupLines(words).Select(JoinWords));
        }

        var text = string.Join(Environment.NewLine, lines);
        var recipeMatch = RecipeNumberInTextRegex().Match(text);

        if (!recipeMatch.Success)
        {
            throw new InvalidDataException("Recipe number was not found in the PDF.");
        }

        var result = new RecipeImportResult
        {
            Kind = RecipeImportKind.ScreenPrinting,
            SourceFile = filePath,
            RecipeNumber = recipeMatch.Groups["number"].Value,
            KText = $"Rezept {recipeMatch.Groups["number"].Value}"
        };

        var layer = new RecipeLayer { LayerNumber = 1, KText = result.KText };

        foreach (var line in lines)
        {
            var match = ScreenPrintComponentRegex().Match(line);
            if (!match.Success || !TryParseDecimalGrams(match.Groups["grams"].Value, out var grams))
            {
                continue;
            }

            layer.Components.Add(new RecipeComponent
            {
                SourceText = match.Groups["code"].Value,
                SourceGrams = grams
            });
        }

        if (layer.Components.Count == 0)
        {
            throw new InvalidDataException("No screen-print components were detected in the text-based PDF.");
        }

        _normalizer.NormalizeLayer(layer);
        result.Layers.Add(layer);
        return result;
    }

    private static List<List<string>> GetExpandedRows(Table table)
    {
        var rows = new List<List<string>>();

        foreach (var row in table.Elements<TableRow>())
        {
            var values = new List<string>();

            foreach (var cell in row.Elements<TableCell>())
            {
                var value = string.Join(" ", cell.Descendants<Text>().Select(text => text.Text)).Trim();
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                values.Add(value);

                for (var index = 1; index < span; index++)
                {
                    values.Add(string.Empty);
                }
            }

            rows.Add(values);
        }

        return rows;
    }

    private static List<List<string>> GetRows(Table table)
    {
        return table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>()
                .Select(cell => string.Join(" ", cell.Descendants<Text>().Select(text => text.Text)).Trim())
                .ToList())
            .ToList();
    }

    private static List<LayerHeader> FindLayerHeaders(IReadOnlyList<PositionedWord> words)
    {
        var result = new List<LayerHeader>();

        foreach (var word in words)
        {
            var trimmed = word.Text.Trim();

            if (!trimmed.EndsWith('.') ||
                !int.TryParse(trimmed.TrimEnd('.'), out var number) ||
                number < 1 || number > 9)
            {
                continue;
            }

            var headerTail = string.Concat(words
                .Where(other =>
                    Math.Abs(other.CenterY - word.CenterY) < 4d &&
                    other.Left > word.Right &&
                    other.Left - word.Right < 55d)
                .OrderBy(other => other.Left)
                .Take(3)
                .Select(other => other.Text));

            var hasVrstva = headerTail.Contains("Vrstva", StringComparison.OrdinalIgnoreCase);

            if (hasVrstva)
            {
                // Keep the actual marker position. ParseSprayPdf derives the
                // layer left/right boundaries from these marker positions.
                result.Add(new LayerHeader(number, word.Left, word.CenterY));
            }
        }

        return result
            .OrderBy(header => header.X)
            .ToList();
    }

    private static List<List<PositionedWord>> GroupLines(
        IEnumerable<PositionedWord> words)
    {
        var ordered = words
            .OrderByDescending(word => word.CenterY)
            .ThenBy(word => word.Left)
            .ToList();

        var lines = new List<List<PositionedWord>>();

        foreach (var word in ordered)
        {
            var line = lines.FirstOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.CenterY) - word.CenterY) <= 3.2d);

            if (line is null)
            {
                line = new List<PositionedWord>();
                lines.Add(line);
            }

            line.Add(word);
        }

        foreach (var line in lines)
        {
            line.Sort((left, right) => left.Left.CompareTo(right.Left));
        }

        return lines
            .OrderByDescending(line => line.Average(word => word.CenterY))
            .ToList();
    }

    private static bool TryParseComponentLine(
        IReadOnlyList<PositionedWord> line,
        out decimal grams,
        out string description,
        out double descriptionLeft)
    {
        grams = 0m;
        description = string.Empty;
        descriptionLeft = 0d;

        if (line.Count < 2)
        {
            return false;
        }

        var lineText = JoinWords(line);

        if (lineText.Contains(
                "Viskoz",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Controlled spray-recipe row format:
        //
        //   <quantity> g[.] <component text>
        //
        // Examples supported by the actual forms:
        //   22 000 g. Klarlack 4811-91
        //   46 g. Gelb P97131
        //   2,4 g. Schwarz P97940
        //   20,000 g Klar AQUA 410-90106-8
        //
        // Parsing the reconstructed row instead of individual PDF words
        // makes the importer independent of whether the PDF engine splits
        // "22 000" into two separate words and whether the unit is "g"
        // or "g.".
        var formatted = SprayComponentLineRegex().Match(lineText);

        if (formatted.Success &&
            TryParseDecimalGrams(
                formatted.Groups["grams"].Value,
                out grams))
        {
            description =
                formatted.Groups["description"]
                    .Value
                    .Trim();

            descriptionLeft =
                GetComponentDescriptionLeft(line);

            return description.Length > 0;
        }

        // Conservative fallback for older/generated PDFs where the unit is
        // separated unusually. It still recognizes both "g" and "g.".
        var descriptionStart = 1;

        if (line.Count >= 4 &&
            line[0].Text.All(char.IsDigit) &&
            Regex.IsMatch(line[1].Text, @"^\d{3}$") &&
            IsGramUnitToken(line[2].Text))
        {
            if (!decimal.TryParse(
                    line[0].Text + line[1].Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out grams))
            {
                return false;
            }

            descriptionStart = 3;
        }
        else
        {
            if (!TryParseDecimalGrams(
                    line[0].Text,
                    out grams))
            {
                return false;
            }

            if (line.Count > 1 &&
                IsGramUnitToken(line[1].Text))
            {
                descriptionStart = 2;
            }
        }

        description = string.Join(
                " ",
                line.Skip(descriptionStart)
                    .Select(word => word.Text))
            .Trim();

        descriptionLeft =
            descriptionStart < line.Count
                ? line[descriptionStart].Left
                : GetComponentDescriptionLeft(line);

        return description.Length > 0;
    }

    private static double GetComponentDescriptionLeft(
        IReadOnlyList<PositionedWord> line)
    {
        for (var index = 0; index < line.Count; index++)
        {
            if (!IsGramUnitToken(line[index].Text))
            {
                continue;
            }

            if (index + 1 < line.Count)
            {
                return line[index + 1].Left;
            }
        }

        return line.Count > 0
            ? line[0].Left
            : 0d;
    }

    private static bool TryParseComponentContinuation(
        IReadOnlyList<PositionedWord> line,
        double descriptionLeft,
        double previousLineY,
        out string continuation)
    {
        continuation = string.Empty;

        if (line.Count == 0)
        {
            return false;
        }

        var lineText =
            JoinWords(line);

        if (string.IsNullOrWhiteSpace(lineText) ||
            IsProcessOrFooterLine(lineText))
        {
            return false;
        }

        // A new recipe row is never a continuation.
        if (SprayComponentLineRegex().IsMatch(lineText) ||
            line.Any(word => IsGramUnitToken(word.Text)))
        {
            return false;
        }

        var currentY =
            line.Average(word => word.CenterY);

        // Wrapped rows in the controlled forms are typically about
        // 10-12 PDF points apart. 18 keeps a small layout tolerance while
        // preventing process/footer text from being attached to a component.
        if (Math.Abs(previousLineY - currentY) > 18d)
        {
            return false;
        }

        var firstLeft =
            line.Min(word => word.Left);

        // Continuation text must start in (or very close to) the description
        // column, not in the quantity column.
        if (firstLeft < descriptionLeft - 8d ||
            firstLeft > descriptionLeft + 35d)
        {
            return false;
        }

        continuation = lineText.Trim();
        return continuation.Length > 0;
    }

    private static bool IsProcessOrFooterLine(
        string lineText)
    {
        return lineText.Contains(
                   "Viskoz",
                   StringComparison.OrdinalIgnoreCase) ||
               lineText.Contains(
                   "metaliz",
                   StringComparison.OrdinalIgnoreCase) ||
               lineText.Contains(
                   "láhev",
                   StringComparison.OrdinalIgnoreCase) ||
               lineText.Contains(
                   "lahev",
                   StringComparison.OrdinalIgnoreCase) ||
               lineText.Contains(
                   "Poznám",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinWrappedComponentText(
        string current,
        string continuation)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return continuation.Trim();
        }

        if (string.IsNullOrWhiteSpace(continuation))
        {
            return current.Trim();
        }

        var left = current.TrimEnd();
        var right = continuation.TrimStart();

        // When a material code is visually wrapped after a hyphen, preserve
        // the code without inserting a space:
        //   410-90106- + 8 -> 410-90106-8
        if (left.EndsWith(
                "-",
                StringComparison.Ordinal))
        {
            return left + right;
        }

        return left + " " + right;
    }

    private static bool IsGramUnitToken(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();

        return string.Equals(
                   normalized,
                   "g",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "g.",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseDecimalGrams(string? value, out decimal grams)
    {
        grams = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Trim()
            .Replace("g", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty);

        // In the controlled spray form 20,000 / 24,000 means 20 000 / 24 000 g,
        // while values such as 26,6 use comma as the decimal separator.
        if (ThousandsCommaRegex().IsMatch(normalized))
        {
            normalized = normalized.Replace(",", string.Empty);
        }
        else
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out grams);
    }

    private static string FindArticleNumber(
        string text,
        IReadOnlyList<PositionedWord> words)
    {
        var match = ArticleRegex().Match(text);

        if (match.Success)
        {
            return match.Groups["article"].Value;
        }

        // Some newer/older spray forms do not contain a 10-digit SAP number
        // in the article field (for example "F11703 100.70027"). Preserve
        // the complete source article identification instead of leaving the
        // field empty. We deliberately do not invent a SAP number.
        var sourceArticle =
            FindRowValueAfterLabel(
                words,
                "Artikl:");

        if (!string.IsNullOrWhiteSpace(sourceArticle))
        {
            return sourceArticle;
        }

        return words
            .Select(word => word.Text)
            .FirstOrDefault(value =>
                value.Length == 10 &&
                value.All(char.IsDigit))
            ?? string.Empty;
    }

    private static string FindHdNumber(
        string text,
        IReadOnlyList<PositionedWord> words)
    {
        var match = HdRegex().Match(text);

        if (match.Success)
        {
            return $"{match.Groups["a"].Value} {match.Groups["b"].Value} {match.Groups["c"].Value}";
        }

        var rowValue =
            FindRowValueAfterLabel(
                words,
                "HD-Nummer:");

        if (string.IsNullOrWhiteSpace(rowValue))
        {
            return string.Empty;
        }

        var rowMatch =
            HdValueRegex().Match(rowValue);

        return rowMatch.Success
            ? $"{rowMatch.Groups["a"].Value} {rowMatch.Groups["b"].Value} {rowMatch.Groups["c"].Value}"
            : rowValue;
    }

    private static string? FindRowValueAfterLabel(
        IReadOnlyList<PositionedWord> words,
        string label)
    {
        var marker = words.FirstOrDefault(word =>
            string.Equals(
                word.Text,
                label,
                StringComparison.OrdinalIgnoreCase));

        if (marker is null)
        {
            return null;
        }

        var sameRow = words
            .Where(word =>
                Math.Abs(word.CenterY - marker.CenterY) < 5d &&
                word.Left > marker.Right)
            .OrderBy(word => word.Left)
            .ToList();

        if (sameRow.Count == 0)
        {
            return null;
        }

        // Stop at the next field label on the same row, e.g.
        // Artikl: ... HD-Nummer: ...
        // Barva:  ... Zařízení: ...
        var nextLabelLeft = sameRow
            .Where(word =>
                word.Text.EndsWith(
                    ":",
                    StringComparison.Ordinal))
            .Select(word => word.Left)
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();

        var valueWords = sameRow
            .Where(word => word.Left < nextLabelLeft)
            .Select(word => word.Text)
            .ToList();

        if (valueWords.Count == 0)
        {
            return null;
        }

        return string.Join(
                " ",
                valueWords)
            .Trim();
    }

    private static string? FindValueAfterLabel(
        IReadOnlyList<PositionedWord> words,
        string label)
    {
        var marker = words.FirstOrDefault(word =>
            string.Equals(word.Text, label, StringComparison.OrdinalIgnoreCase));

        if (marker is null) return null;

        return words
            .Where(word => Math.Abs(word.CenterY - marker.CenterY) < 5d && word.Left > marker.Right)
            .OrderBy(word => word.Left)
            .Select(word => word.Text)
            .FirstOrDefault();
    }

    private static string? FindMultiWordValueAfterLabel(
        IReadOnlyList<PositionedWord> words,
        string label,
        int take)
    {
        var marker = words.FirstOrDefault(word =>
            string.Equals(word.Text, label, StringComparison.OrdinalIgnoreCase));

        if (marker is null) return null;

        return string.Join(" ", words
            .Where(word => Math.Abs(word.CenterY - marker.CenterY) < 5d && word.Left > marker.Right)
            .OrderBy(word => word.Left)
            .Take(take)
            .Select(word => word.Text));
    }

    private static string ExtractGeneralNote(string text)
    {
        var marker = text.IndexOf("Poznámka", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return string.Empty;

        var tail = text.Substring(marker);
        var stop = tail.IndexOf("Datum vytvoření", StringComparison.OrdinalIgnoreCase);
        return (stop > 0 ? tail.Substring(0, stop) : tail).Trim();
    }

    private static string JoinWords(IEnumerable<PositionedWord> line) =>
        string.Join(" ", line.OrderBy(word => word.Left).Select(word => word.Text)).Trim();

    private sealed record PositionedWord(
        string Text,
        double Left,
        double Right,
        double Bottom,
        double Top)
    {
        public double CenterX => (Left + Right) / 2d;
        public double CenterY => (Bottom + Top) / 2d;
    }

    private sealed record LayerHeader(int Number, double X, double Y);

    [GeneratedRegex(@"SAP:\s*(?<article>\d{10})", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleRegex();

    [GeneratedRegex(
        @"HD-Nummer:\s*(?:HD\s*)?(?<a>\d{2})\s*(?<b>\d{5})\s*(?<c>\d{2})",
        RegexOptions.IgnoreCase)]
    private static partial Regex HdRegex();

    [GeneratedRegex(
        @"^(?:HD\s*)?(?<a>\d{2})\s*(?<b>\d{5})\s*(?<c>\d{2})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex HdValueRegex();

    [GeneratedRegex(
        @"^\s*(?<grams>\d{1,3}(?:[\s\u00A0]\d{3})+|\d+(?:[,.]\d+)?)\s*g\.?\s+(?<description>.+?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SprayComponentLineRegex();

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+$")]
    private static partial Regex ThousandsCommaRegex();

    [GeneratedRegex(@"^\d{1,6}/\d{1,3}$")]
    private static partial Regex RecipeNumberRegex();

    [GeneratedRegex(@"(?<number>\d{1,6}/\d{1,3})")]
    private static partial Regex RecipeNumberInTextRegex();

    [GeneratedRegex(@"(?m)^(?<code>[A-Za-z]+[0-9][A-Za-z0-9\-]*)\s+(?<grams>\d+(?:[,.]\d+)?)\s*$")]
    private static partial Regex ScreenPrintComponentRegex();
}
