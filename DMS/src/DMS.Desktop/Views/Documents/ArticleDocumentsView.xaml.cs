using DMS.Desktop.Models;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Documents;

public partial class ArticleDocumentsView : UserControl
{
    private readonly List<ArticleDocumentItem> _documents = new();

    private readonly Action<string>? _documentOpened;

    public ArticleDocumentsView(
        string articleNumber,
        string articleFolderPath,
        Action<string>? documentOpened = null)
    {
        InitializeComponent();

        _documentOpened = documentOpened;

        TxtTitle.Text = $"Dokumentace artiklu {articleNumber}";
        TxtFolder.Text = articleFolderPath;

        LoadDocuments(articleFolderPath);
    }

    private void LoadDocuments(string articleFolderPath)
    {
        _documents.Clear();

        if (!Directory.Exists(articleFolderPath))
        {
            MessageBox.Show(
                $"Složka dokumentů pro artikl neexistuje.\n\n{articleFolderPath}",
                "DMS - dokumentace artiklu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            DgvDocuments.ItemsSource = _documents;
            return;
        }

        var supportedExtensions = new HashSet<string>(
            new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".txt", ".msg", ".eml" },
            StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(articleFolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => supportedExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => Path.GetFileName(file))
            .ToList();

        foreach (var file in files)
        {
            var info = new FileInfo(file);

            _documents.Add(new ArticleDocumentItem
            {
                FileName = info.Name,
                FilePath = info.FullName,
                Extension = info.Extension,
                SizeBytes = info.Length,
                LastModified = info.LastWriteTime,
                DocumentKind = DetectDocumentKind(info.Name)
            });
        }

        DgvDocuments.ItemsSource = null;
        DgvDocuments.ItemsSource = _documents;
    }

    private static string DetectDocumentKind(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        if (name.Contains("massblatt") || name.Contains("mas") || name.Contains("maß") || name.Contains("MB"))
        {
            return "Massblatt";
        }

        if (name.Contains("vykres") || name.Contains("výkres") || name.Contains("drawing") || name.Contains("zeichnung"))
        {
            return "Výkres";
        }

        if (name.Contains("tisk") || name.Contains("print"))
        {
            return "Tisková oblast";
        }

        if (name.Contains("bal") || name.Contains("verpack"))
        {
            return "Balicí předpis";
        }

        if (name.Contains("recept") || name.Contains("rez"))
        {
            return "Receptura";
        }

        if (name.Contains("Zak") || name.Contains("Zakázka"))
        {
            return "Vzorovací zakázka";
        }

        if (name.Contains("obeznik") || name.Contains("Oběžník"))
        {
            return "Oběžník";
        }

        if (name.Contains("Check") || name.Contains("Checklist"))
        {
            return "Checklist";
        }

        if (name.Contains("Kalk") || name.Contains("Kalkulace"))
        {
            return "Kalkulace";
        }

        if (name.Contains("eml") || name.Contains("email"))
        {
            return "Email";
        }

        if (name.Contains("Schválení") || name.Contains("schv"))
        {
            return "Schválení";
        }

        if (name.Contains("Musterbegleitschein") || name.Contains("Muster"))
        {
            return "Musterbegleitschein";
        }

        if (name.Contains("Výr") || name.Contains("vyr"))
        {
            return "Schválení pro výrobu";
        }

        if (name.Contains("Podeps") || name.Contains("Podepsaný"))
        {
            return "Podepsaný MB";
        }

        return "Dokument";
    }

    private void BtnOpenDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var filePath = button.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        OpenDocument(filePath);
    }

    private void DgvDocuments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgvDocuments.SelectedItem is not ArticleDocumentItem item)
        {
            return;
        }

        OpenDocument(item.FilePath);
    }

    private void OpenDocument(string filePath)
    {
        if (!File.Exists(filePath))
        {
            MessageBox.Show(
                $"Soubor neexistuje.\n\n{filePath}",
                "DMS - otevření dokumentu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            _documentOpened?.Invoke(filePath);

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Soubor se nepodařilo otevřít.\n\n{filePath}\n\n{ex.Message}",
                "DMS - otevření dokumentu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}