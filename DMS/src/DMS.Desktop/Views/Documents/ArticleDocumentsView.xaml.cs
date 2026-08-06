using DMS.Core.Documents;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Documents;

public partial class ArticleDocumentsView : UserControl, INotifyPropertyChanged
{
    private readonly string _articleNumber;
    private readonly string _articleFolderPath;
    private readonly DmsArticleDocumentIndexService _indexService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? TransactionRequested;

    public string OpenButtonText => T("DOC03.Button.Open");

    public ArticleDocumentsView(
        string articleNumber,
        string articleFolderPath,
        Action<string>? documentOpened = null)
        : this(articleNumber, articleFolderPath, null, null, null, null)
    {
        if (documentOpened is not null)
        {
            _documentOpened = documentOpened;
        }
    }

    private Action<string>? _documentOpened;

    public ArticleDocumentsView(
        string articleNumber,
        string articleFolderPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _articleNumber = articleNumber;
        _articleFolderPath = articleFolderPath;
        _indexService = new DmsArticleDocumentIndexService(articleFolderPath);
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        LoadDocuments();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = TF("DOC03.Title", _articleNumber);
        TxtSummary.Text = T("DOC03.Summary");
        TxtFolderLabel.Text = T("DOC03.Folder");
        TxtFolder.Text = _articleFolderPath;

        BtnOpenTec03.Content = T("DOC03.Button.OpenTec03");
        BtnOpenDoc02.Content = T("DOC03.Button.OpenDoc02");
        BtnRefresh.Content = T("DOC03.Button.Refresh");

        ColFileName.Header = T("DOC03.Column.FileName");
        ColKind.Header = T("DOC03.Column.Kind");
        ColDescription.Header = T("DOC03.Column.Description");
        ColExtension.Header = T("DOC03.Column.Extension");
        ColSize.Header = T("DOC03.Column.Size");
        ColUploadedBy.Header = T("DOC03.Column.UploadedBy");
        ColUploadedAt.Header = T("DOC03.Column.UploadedAt");
        ColChangedBy.Header = T("DOC03.Column.ChangedBy");
        ColChangedAt.Header = T("DOC03.Column.ChangedAt");
        ColPath.Header = T("DOC03.Column.Path");
        ColAction.Header = T("DOC03.Column.Action");

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenButtonText)));
    }

    private void LoadDocuments()
    {
        try
        {
            var records = _indexService.LoadDisplayRecords(_articleNumber)
                .Select(ToDisplayItem)
                .ToList();

            DgvDocuments.ItemsSource = records;

            TxtStatus.Text = Directory.Exists(_articleFolderPath)
                ? TF("DOC03.Status.Loaded", records.Count)
                : TF("DOC03.Status.FolderMissing", _articleFolderPath);

            _logger?.AdminAction(
                "DOC03",
                "LoadArticleDocuments",
                _currentUserName,
                $"Article={_articleNumber}; Count={records.Count}; Folder={_articleFolderPath}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC03 failed to load article documents. Article={_articleNumber}; Folder={_articleFolderPath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC03.Dialog.LoadFailed.Title"),
                TF("DOC03.Dialog.LoadFailed.Body", ex.Message, _articleFolderPath));
        }
    }

    private ArticleDocumentDisplayItem ToDisplayItem(DmsArticleDocumentRecord record)
    {
        return new ArticleDocumentDisplayItem
        {
            Record = record,
            FullPath = _indexService.GetPhysicalPath(record),
            IsActiveText = record.IsActive ? T("Common.Yes") : T("Common.No")
        };
    }

    private void BtnOpenTec03_Click(object sender, RoutedEventArgs e)
    {
        TransactionRequested?.Invoke($"TEC03 {_articleNumber}");
    }

    private void BtnOpenDoc02_Click(object sender, RoutedEventArgs e)
    {
        TransactionRequested?.Invoke($"DOC02 {_articleNumber}");
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDocuments();
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
        if (DgvDocuments.SelectedItem is not ArticleDocumentDisplayItem item)
        {
            return;
        }

        OpenDocument(item.FullPath);
    }

    private void OpenDocument(string filePath)
    {
        if (!File.Exists(filePath))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC03.Dialog.FileMissing.Title"),
                TF("DOC03.Dialog.FileMissing.Body", filePath));

            return;
        }

        try
        {
            _documentOpened?.Invoke(filePath);
            _logger?.OpenDocument(filePath, _currentUserName);

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC03 failed to open document. File={filePath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC03.Dialog.OpenFailed.Title"),
                TF("DOC03.Dialog.OpenFailed.Body", filePath, ex.Message));
        }
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
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
}
