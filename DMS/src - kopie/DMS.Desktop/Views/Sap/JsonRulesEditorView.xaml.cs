using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class JsonRulesEditorView : UserControl
{
    private readonly string _filePath;
    private readonly Func<string, string>? _translate;
    private readonly Action<string, string>? _logAction;
    private string _lastLoadedJson = string.Empty;

    // Konstruktor pro XAML designer / zpětnou kompatibilitu
    public JsonRulesEditorView()
        : this("Rules Editor", string.Empty)
    {
    }

    public JsonRulesEditorView(
        string title,
        string filePath,
        Func<string, string>? translate = null,
        Action<string, string>? logAction = null)
    {
        InitializeComponent();

        _filePath = filePath;
        _translate = translate;
        _logAction = logAction;

        TxtTitle.Text = title;
        TxtFilePath.Text = filePath;

        ApplyLocalization();
        LoadJson();
    }

    private void ApplyLocalization()
    {
        BtnReload.Content = T("JsonEditor.Reload");
        BtnValidate.Content = T("JsonEditor.Validate");
        BtnSave.Content = T("JsonEditor.Save");
        BtnRevert.Content = T("JsonEditor.Revert");
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        LoadJson();
    }

    private void BtnValidate_Click(object sender, RoutedEventArgs e)
    {
        ValidateJson(showSuccessMessage: true);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateJson(showSuccessMessage: false))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            WriteUtf8WithoutBom(_filePath, TxtJson.Text);
            _lastLoadedJson = TxtJson.Text;

            TxtStatus.Text = TF("JsonEditor.Saved", _filePath);

            _logAction?.Invoke("SaveRulesFile", $"File={_filePath}");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = TF("JsonEditor.SaveFailed", ex.Message);

            _logAction?.Invoke("SaveRulesFileFailed", $"File={_filePath}; Error={ex.Message}");
        }
    }

    private void BtnRevert_Click(object sender, RoutedEventArgs e)
    {
        TxtJson.Text = _lastLoadedJson;
        TxtStatus.Text = T("JsonEditor.Reverted");

        _logAction?.Invoke("RevertRulesFile", $"File={_filePath}");
    }

    private void LoadJson()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                TxtJson.Text = string.Empty;
                _lastLoadedJson = string.Empty;
                TxtStatus.Text = T("JsonEditor.FileNotFound");
                return;
            }

            var json = ReadTextWithEncodingFallback(_filePath);

            TxtJson.Text = FormatJson(json);
            _lastLoadedJson = TxtJson.Text;

            TxtStatus.Text = TF("JsonEditor.Loaded", _filePath);

            _logAction?.Invoke("LoadRulesFile", $"File={_filePath}");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = TF("JsonEditor.LoadFailed", ex.Message);

            _logAction?.Invoke("LoadRulesFileFailed", $"File={_filePath}; Error={ex.Message}");
        }
    }

    private bool ValidateJson(bool showSuccessMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(TxtJson.Text);

            if (showSuccessMessage)
            {
                TxtStatus.Text = T("JsonEditor.JsonValid");
            }

            return true;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = TF("JsonEditor.JsonInvalid", ex.Message);
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(json);

        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static string ReadTextWithEncodingFallback(string filePath)
    {
        try
        {
            var utf8Strict = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

            return File.ReadAllText(filePath, utf8Strict);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return File.ReadAllText(filePath, Encoding.GetEncoding(1250));
        }
    }

    private static void WriteUtf8WithoutBom(string filePath, string text)
    {
        File.WriteAllText(
            filePath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        var pattern = T(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    private static bool IsMissing(string? value, string key)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
}