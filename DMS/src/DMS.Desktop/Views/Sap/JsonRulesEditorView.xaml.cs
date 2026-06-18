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
    private string _lastLoadedJson = string.Empty;

    public JsonRulesEditorView(string title, string filePath)
    {
        InitializeComponent();

        _filePath = filePath;

        TxtTitle.Text = title;
        TxtFilePath.Text = filePath;

        LoadJson();
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

            TxtStatus.Text = $"Uloženo: {_filePath}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Chyba při ukládání:\n{ex.Message}";
        }
    }

    private void BtnRevert_Click(object sender, RoutedEventArgs e)
    {
        TxtJson.Text = _lastLoadedJson;
        TxtStatus.Text = "Změny byly vráceny na poslední načtenou verzi.";
    }

    private void LoadJson()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                TxtJson.Text = string.Empty;
                _lastLoadedJson = string.Empty;

                TxtStatus.Text =
                    "Soubor zatím neexistuje.\n" +
                    "Po vložení platného JSONu ho můžeš uložit.";

                return;
            }

            var json = ReadTextWithEncodingFallback(_filePath);

            TxtJson.Text = FormatJson(json);
            _lastLoadedJson = TxtJson.Text;

            TxtStatus.Text = $"Načteno: {_filePath}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Chyba při načítání:\n{ex.Message}";
        }
    }

    private bool ValidateJson(bool showSuccessMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(TxtJson.Text);

            if (showSuccessMessage)
            {
                TxtStatus.Text = "JSON je platný.";
            }

            return true;
        }
        catch (Exception ex)
        {
            TxtStatus.Text =
                "JSON není platný.\n\n" +
                ex.Message;

            return false;
        }
    }

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
            // Přísné UTF-8 čtení – pokud soubor není validní UTF-8, spadne to do catch.
            var utf8Strict = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

            return File.ReadAllText(filePath, utf8Strict);
        }
        catch (DecoderFallbackException)
        {
            // Starší české soubory mohou být ve Windows-1250.
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
}