using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapImportView : UserControl
{
    public SapImportView()
    {
        InitializeComponent();

        DgvColumnMapping.ItemsSource = GetMaterialColumnMappings();

        TxtResult.Text =
            "Připraveno k importu materiálů.\n\n" +
            "Vyber export MARA a MAKT ze SAPu.";
    }

    private void BtnSelectMara_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile("Vyber MARA export");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            TxtMaraFile.Text = filePath;
        }
    }

    private void BtnSelectMakt_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile("Vyber MAKT export");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            TxtMaktFile.Text = filePath;
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            storagePaths.EnsureDirectories();

            var rulesPath = Path.Combine(
                AppContext.BaseDirectory,
                "Config",
                "sap-material-rules.json");

            var outputPath = storagePaths.SapMaterialsFilePath;

            var rules = new SapMaterialRulesLoader().LoadFromJson(rulesPath);

            var numberRuleCount = rules.MaterialNumberRules?.Count ?? 0;
            var textRuleCount = rules.TextClassificationRules?.Count ?? 0;

            if (numberRuleCount == 0)
            {
                TxtResult.Text =
                    "Import zastaven.\n\n" +
                    "Nenačetla se žádná SAP materiálová pravidla:\n" +
                    rulesPath;

                return;
            }

            var classifier = new SapMaterialClassifier(rules);
            var repository = new JsonSapMaterialRepository(outputPath);

            var service = new SapMaterialExcelImportService(
                classifier,
                repository);

            var result = service.Import(
                TxtMaraFile.Text.Trim(),
                TxtMaktFile.Text.Trim());

            TxtResult.Text =
                result.ToDisplayText() +
                "\n\nPoužitá pravidla:\n" +
                rulesPath +
                "\n\nPočet číselných pravidel: " + numberRuleCount +
                "\nPočet textových pravidel: " + textRuleCount +
                "\n\nVýstupní soubor:\n" +
                outputPath;
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import selhal.\n\n" +
                ex.Message;
        }
    }

    private bool ValidateInputFiles()
    {
        if (!File.Exists(TxtMaraFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný MARA Excel.",
                "SAP00 - Import materiálů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!File.Exists(TxtMaktFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný MAKT Excel.",
                "SAP00 - Import materiálů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        return true;
    }

    private static string? SelectExcelFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Excel soubory (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Všechny soubory (*.*)|*.*"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private static IReadOnlyList<MaterialColumnMappingRow> GetMaterialColumnMappings()
    {
        return new List<MaterialColumnMappingRow>
        {
            new()
            {
                TableName = "MARA",
                ColumnName = "MATNR",
                IsRequired = true,
                SapMeaning = "Číslo materiálu",
                DmsMeaning = "Hlavní SAP číslo materiálu / artiklu. V DMS se ukládá jako text."
            },
            new()
            {
                TableName = "MARA",
                ColumnName = "BISMT",
                IsRequired = true,
                SapMeaning = "Staré číslo materiálu",
                DmsMeaning = "Původní / staré označení artiklu, důležité pro dohledání a vazby."
            },
            new()
            {
                TableName = "MARA",
                ColumnName = "MSTAE",
                IsRequired = true,
                SapMeaning = "Cross-plant material status",
                DmsMeaning = "Celopodnikový stav materiálu. V DMS slouží pro kontrolu použitelnosti artiklu."
            },
            new()
            {
                TableName = "MAKT",
                ColumnName = "MATNR",
                IsRequired = true,
                SapMeaning = "Číslo materiálu",
                DmsMeaning = "Klíč pro spojení MAKT s MARA."
            },
            new()
            {
                TableName = "MAKT",
                ColumnName = "MAKTX",
                IsRequired = true,
                SapMeaning = "Krátký text materiálu",
                DmsMeaning = "Popis materiálu. Používá se i pro rozpoznání dekorace, externího skla, obalových sestav a dalších typů."
            }
        };
    }

    private sealed class MaterialColumnMappingRow
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public bool IsRequired { get; init; }
        public string SapMeaning { get; init; } = string.Empty;
        public string DmsMeaning { get; init; } = string.Empty;
    }
}