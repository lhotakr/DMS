using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DMS.Desktop.Help;

public static class DmsMarkdownFlowDocumentRenderer
{
    private static readonly Regex InlineRegex = new(
        @"(\[[^\]]+\]\(dms:[^)]+\)|\*\*[^*]+\*\*|`[^`]+`)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FlowDocument Render(string? markdown, Action<string>? executeTransaction = null)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            LineHeight = double.NaN,
            FontSize = 14
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "DmsForegroundBrush");

        var text = (markdown ?? string.Empty).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var codeLines = new List<string>();
        var inCode = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    AddCodeBlock(document, codeLines);
                    codeLines.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                codeLines.Add(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AddHeading(document, line[4..], 16, new Thickness(0, 12, 0, 4));
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddHeading(document, line[3..], 18, new Thickness(0, 16, 0, 6));
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AddHeading(document, line[2..], 24, new Thickness(0, 0, 0, 12));
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                var quote = new Paragraph
                {
                    Margin = new Thickness(12, 4, 0, 8),
                    FontStyle = FontStyles.Italic
                };
                quote.SetResourceReference(TextElement.ForegroundProperty, "DmsMutedForegroundBrush");
                AddInlineContent(quote, line[2..], executeTransaction);
                document.Blocks.Add(quote);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                var paragraph = new Paragraph { Margin = new Thickness(14, 2, 0, 2) };
                paragraph.Inlines.Add(new Run("•  "));
                AddInlineContent(paragraph, line[2..], executeTransaction);
                document.Blocks.Add(paragraph);
                continue;
            }

            var numbered = Regex.Match(line, @"^(\d+)\.\s+(.+)$");
            if (numbered.Success)
            {
                var paragraph = new Paragraph { Margin = new Thickness(14, 2, 0, 2) };
                paragraph.Inlines.Add(new Run(numbered.Groups[1].Value + ".  "));
                AddInlineContent(paragraph, numbered.Groups[2].Value, executeTransaction);
                document.Blocks.Add(paragraph);
                continue;
            }

            if (line == "---")
            {
                document.Blocks.Add(new Paragraph(new Run("────────────────────────"))
                {
                    Margin = new Thickness(0, 8, 0, 8)
                });
                continue;
            }

            var body = new Paragraph { Margin = new Thickness(0, 2, 0, 8) };
            AddInlineContent(body, line, executeTransaction);
            document.Blocks.Add(body);
        }

        if (codeLines.Count > 0)
        {
            AddCodeBlock(document, codeLines);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run("No documentation is available for this topic.")));
        }

        return document;
    }

    private static void AddHeading(FlowDocument document, string text, double fontSize, Thickness margin)
    {
        var paragraph = new Paragraph
        {
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Margin = margin
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "DmsAccentBrush");
        paragraph.Inlines.Add(new Run(text.Trim()));
        document.Blocks.Add(paragraph);
    }

    private static void AddCodeBlock(FlowDocument document, IEnumerable<string> lines)
    {
        var paragraph = new Paragraph
        {
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(12, 4, 0, 10)
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "DmsForegroundBrush");
        paragraph.Inlines.Add(new Run(string.Join(Environment.NewLine, lines)));
        document.Blocks.Add(paragraph);
    }

    private static void AddInlineContent(Paragraph paragraph, string text, Action<string>? executeTransaction)
    {
        var index = 0;
        foreach (Match match in InlineRegex.Matches(text))
        {
            if (match.Index > index)
            {
                paragraph.Inlines.Add(new Run(text[index..match.Index]));
            }

            var token = match.Value;

            if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith("`", StringComparison.Ordinal) && token.EndsWith("`", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold
                });
            }
            else
            {
                var linkMatch = Regex.Match(token, @"^\[([^\]]+)\]\(dms:([^)]+)\)$", RegexOptions.IgnoreCase);
                if (linkMatch.Success)
                {
                    var command = linkMatch.Groups[2].Value.Trim();
                    var hyperlink = new Hyperlink(new Run(linkMatch.Groups[1].Value))
                    {
                        ToolTip = command,
                        Tag = command
                    };
                    hyperlink.SetResourceReference(TextElement.ForegroundProperty, "DmsAccentBrush");
                    hyperlink.Click += (_, _) => executeTransaction?.Invoke(command);
                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(token));
                }
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[index..]));
        }
    }
}
