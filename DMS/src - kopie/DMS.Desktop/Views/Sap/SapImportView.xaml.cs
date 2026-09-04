using DMS.Desktop.Configuration;
using DMS.Core.Sap;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapImportView : UserControl
{
    private readonly SapStoragePaths _storagePaths;
    private readonly string _materialRulesPath;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly Action<string, string>? _logAction;

    public SapImportView()
        : this(
            new SapStoragePaths(DmsStoragePathPolicy.GetEnvironmentRoot("DEV")),
            Path.Combine(AppContext.BaseDirectory, "Config", "sap-material-rules.json"))
    {
    }

    public SapImportView(
        SapStoragePaths storagePaths,
        string materialRulesPath,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null,
        Action<string, string>? logAction = null)
    {
        InitializeComponent();

        _storagePaths = storagePaths;
        _materialRulesPath = materialRulesPath;
        _translate = translate;
        _translateFormat = translateFormat;
        _logAction = logAction;

        ApplyLocalization();
        DgvColumnMapping.ItemsSource = GetMaterialColumnMappings();

        TxtResult.Text = T("SAP00.Materials.Ready");
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.Materials.Title");
        TxtSubtitle.Text = T("SAP00.Materials.Subtitle");
        BtnSelectMara.Content = T("SAP00.Materials.SelectMara");
        BtnSelectMakt.Content = T("SAP00.Materials.SelectMakt");
        BtnImport.Content = T("SAP00.Materials.RunImport");
        ExpColumnMapping.Header = T("SAP00.Materials.ColumnMappingHeader");

        ColTable.Header = T("SAP00.Materials.Column.Table");
        ColColumn.Header = T("SAP00.Materials.Column.Column");
        ColRequired.Header = T("SAP00.Materials.Column.Required");
        ColSapMeaning.Header = T("SAP00.Materials.Column.SapMeaning");
        ColDmsMeaning.Header = T("SAP00.Materials.Column.DmsMeaning");
    }

    private void BtnSelectMara_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile(T("SAP00.Materials.SelectMaraDialog"));

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            TxtMaraFile.Text = filePath;
        }
    }

    private void BtnSelectMakt_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile(T("SAP00.Materials.SelectMaktDialog"));

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
            _storagePaths.EnsureDirectories();

            var outputPath = _storagePaths.SapMaterialsFilePath;

            _logAction?.Invoke(
                "StartMaterialImport",
                $"MARA={TxtMaraFile.Text.Trim()}; MAKT={TxtMaktFile.Text.Trim()}; Rules={_materialRulesPath}; Output={outputPath}");

            var rules = new SapMaterialRulesLoader().LoadFromJson(_materialRulesPath);

            var numberRuleCount = rules.MaterialNumberRules?.Count ?? 0;
            var textRuleCount = rules.TextClassificationRules?.Count ?? 0;

            if (numberRuleCount == 0)
            {
                TxtResult.Text = TF(
                    "SAP00.Materials.ImportStoppedNoMaterialRules",
                    _materialRulesPath);

                _logAction?.Invoke(
                    "MaterialImportStopped",
                    $"Reason=NoMaterialNumberRules; Rules={_materialRulesPath}");

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
                Environment.NewLine + Environment.NewLine +
                TF("SAP00.Materials.RulesUsed", _materialRulesPath) +
                Environment.NewLine +
                TF("SAP00.Materials.NumberRuleCount", numberRuleCount) +
                Environment.NewLine +
                TF("SAP00.Materials.TextRuleCount", textRuleCount) +
                Environment.NewLine + Environment.NewLine +
                TF("SAP00.Materials.OutputFile", outputPath);

            _logAction?.Invoke(
                "MaterialImportCompleted",
                $"Rules={_materialRulesPath}; NumberRules={numberRuleCount}; TextRules={textRuleCount}; Output={outputPath}");
        }
        catch (Exception ex)
        {
            TxtResult.Text = TF("SAP00.Materials.ImportFailed", ex.Message);

            _logAction?.Invoke(
                "MaterialImportFailed",
                $"Error={ex.Message}");
        }
    }

    private bool ValidateInputFiles()
    {
        if (!File.Exists(TxtMaraFile.Text.Trim()))
        {
            ShowInfo("SAP00.Materials.ValidationTitle", "SAP00.Materials.InvalidMara");

            _logAction?.Invoke("MaterialImportValidationFailed", "MissingOrInvalid=MARA");

            return false;
        }

        if (!File.Exists(TxtMaktFile.Text.Trim()))
        {
            ShowInfo("SAP00.Materials.ValidationTitle", "SAP00.Materials.InvalidMakt");

            _logAction?.Invoke("MaterialImportValidationFailed", "MissingOrInvalid=MAKT");

            return false;
        }

        return true;
    }

    private void ShowInfo(string titleKey, string messageKey)
    {
        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T(titleKey),
            T(messageKey));
    }

    private string? SelectExcelFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = T("SAP00.ExcelFilter")
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private IReadOnlyList<MaterialColumnMappingRow> GetMaterialColumnMappings()
    {
        return new List<MaterialColumnMappingRow>
        {
            new()
            {
                TableName = "MARA",
                ColumnName = "MATNR",
                IsRequired = true,
                SapMeaning = T("SAP00.Materials.Mapping.MARA.MATNR.Sap"),
                DmsMeaning = T("SAP00.Materials.Mapping.MARA.MATNR.Dms")
            },
            new()
            {
                TableName = "MARA",
                ColumnName = "BISMT",
                IsRequired = true,
                SapMeaning = T("SAP00.Materials.Mapping.MARA.BISMT.Sap"),
                DmsMeaning = T("SAP00.Materials.Mapping.MARA.BISMT.Dms")
            },
            new()
            {
                TableName = "MARA",
                ColumnName = "MSTAE",
                IsRequired = true,
                SapMeaning = T("SAP00.Materials.Mapping.MARA.MSTAE.Sap"),
                DmsMeaning = T("SAP00.Materials.Mapping.MARA.MSTAE.Dms")
            },
            new()
            {
                TableName = "MAKT",
                ColumnName = "MATNR",
                IsRequired = true,
                SapMeaning = T("SAP00.Materials.Mapping.MAKT.MATNR.Sap"),
                DmsMeaning = T("SAP00.Materials.Mapping.MAKT.MATNR.Dms")
            },
            new()
            {
                TableName = "MAKT",
                ColumnName = "MAKTX",
                IsRequired = true,
                SapMeaning = T("SAP00.Materials.Mapping.MAKT.MAKTX.Sap"),
                DmsMeaning = T("SAP00.Materials.Mapping.MAKT.MAKTX.Dms")
            }
        };
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;

        return IsMissing(value, key)
            ? key
            : value;
    }

    private string TF(string key, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);

        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
        {
            return value;
        }

        var pattern = T(key);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
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
