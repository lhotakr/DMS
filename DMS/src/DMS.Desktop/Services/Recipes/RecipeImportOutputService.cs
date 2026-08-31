using ClosedXML.Excel;
using DMS.Core.Recipes;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Desktop.Services.Recipes;

public sealed class RecipeImportOutputService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string SaveJson(RecipeImportResult result, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var name = BuildSafeName(result);
        var path = System.IO.Path.Combine(
            outputDirectory,
            $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(result, JsonOptions),
            new UTF8Encoding(true));

        return path;
    }

    public void ExportExcel(RecipeImportResult result, string filePath)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell("A1").Value = "Source";
        summary.Cell("B1").Value = result.SourceFile;
        summary.Cell("A2").Value = "Kind";
        summary.Cell("B2").Value = result.Kind.ToString();
        summary.Cell("A3").Value = "Article";
        summary.Cell("B3").Value = result.ArticleNumber;
        summary.Cell("A4").Value = "HD number";
        summary.Cell("B4").Value = result.HdNumber;
        summary.Cell("A5").Value = "Recipe";
        summary.Cell("B5").Value = result.RecipeNumber;
        summary.Cell("A6").Value = "KText";
        summary.Cell("B6").Value = result.KText;
        summary.Cell("A7").Value = "Color";
        summary.Cell("B7").Value = result.Color;
        summary.Cell("A8").Value = "Device";
        summary.Cell("B8").Value = result.Device;
        summary.Cell("A10").Value = "Warnings";
        summary.Cell("B10").Value = string.Join(Environment.NewLine, result.Warnings);
        summary.Cell("A12").Value = "General note";
        summary.Cell("B12").Value = result.GeneralNote;
        summary.Columns().AdjustToContents();
        summary.Column(2).Width = Math.Min(summary.Column(2).Width, 80d);
        summary.Cell("B10").Style.Alignment.WrapText = true;
        summary.Cell("B12").Style.Alignment.WrapText = true;

        foreach (var layer in result.Layers)
        {
            var sheetName = result.Kind == RecipeImportKind.ScreenPrinting
                ? "Recipe"
                : layer.LayerCode;

            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Cell("A1").Value = "KText";
            sheet.Cell("B1").Value = layer.KText;
            sheet.Cell("A2").Value = "Base quantity [g]";
            sheet.Cell("B2").Value = layer.BaseQuantityGrams;
            sheet.Cell("A3").Value = "Source total [g]";
            sheet.Cell("B3").Value = layer.SourceTotalGrams;
            sheet.Cell("A4").Value = "Final BOM total [g]";
            sheet.Cell("B4").Value = layer.FinalTotalGrams;
            sheet.Cell("A5").Value = "Process only";
            sheet.Cell("B5").Value = layer.ProcessOnly;
            sheet.Cell("A6").Value = "Texts";
            sheet.Cell("B6").Value = string.Join(" | ", layer.TextItems);

            var headers = new[]
            {
                "SAP material", "SAP description", "Source text", "Source g",
                "BOM g / base 1 kg", "Match", "Method", "Hardener", "Rule"
            };

            for (var column = 0; column < headers.Length; column++)
            {
                sheet.Cell(8, column + 1).Value = headers[column];
                sheet.Cell(8, column + 1).Style.Font.Bold = true;
            }

            var row = 9;
            foreach (var component in layer.Components)
            {
                sheet.Cell(row, 1).Value = component.SapMaterialNumber;
                sheet.Cell(row, 2).Value = component.SapDescription;
                sheet.Cell(row, 3).Value = component.SourceText;
                sheet.Cell(row, 4).Value = component.SourceGrams;
                sheet.Cell(row, 5).Value = component.BomGrams;
                sheet.Cell(row, 6).Value = component.MatchScore;
                sheet.Cell(row, 7).Value = component.MatchMethod;
                sheet.Cell(row, 8).Value = component.IsHardener;
                sheet.Cell(row, 9).Value = component.GeneratedByRule;
                row++;
            }

            sheet.Column(5).Style.NumberFormat.Format = "0.000000";
            sheet.Column(6).Style.NumberFormat.Format = "0%";
            sheet.Columns().AdjustToContents();
        }

        workbook.SaveAs(filePath);
    }

    private static string BuildSafeName(RecipeImportResult result)
    {
        var value = result.Kind == RecipeImportKind.ScreenPrinting
            ? $"Rezept-{result.RecipeNumber}"
            : $"HD-{string.Concat(result.HdNumber.Where(char.IsDigit))}-{result.ArticleNumber}";

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace('/', '-');
    }
}
