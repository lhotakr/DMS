using DMS.Core.Documents;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Documents;

public partial class ArticleDocumentEditView : UserControl, IUnsavedChangesGuard
{
    private readonly string _articleNumber;
    private readonly string _articleFolderPath;
    private readonly DmsArticleDocumentIndexService _indexService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private DmsArticleDocumentIndex _index = new();
    private string? _selectedSourceFilePath;
    private string? _selectedDocumentId;
    private bool _isLoadingSelection;
    private bool _hasUnsavedChanges;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public event Action<string>? TransactionRequested;

    public ArticleDocumentEditView(
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
        InitializeDocumentKinds();
        LoadIndex();
        ClearEditor();
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("DOC02.Dialog.Unsaved.Title"),
            T("DOC02.Dialog.Unsaved.Body"));
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = TF("DOC02.Title", _articleNumber);
        TxtSummary.Text = T("DOC02.Summary");
        TxtFolderLabel.Text = T("DOC02.Folder");
        TxtFolder.Text = _articleFolderPath;

        BtnOpenDoc03.Content = T("DOC02.Button.OpenDoc03");
        BtnOpenTec03.Content = T("DOC02.Button.OpenTec03");
        BtnRefresh.Content = T("DOC02.Button.Refresh");

        TxtEditorTitle.Text = T("DOC02.Editor.Title");
        TxtSourceFileLabel.Text = T("DOC02.SourceFile");
        BtnBrowse.Content = T("DOC02.Button.Browse");
        TxtKindLabel.Text = T("DOC02.Kind");
        ChkIsActive.Content = T("DOC02.Active");
        TxtDescriptionLabel.Text = T("DOC02.Description");
        BtnUploadNew.Content = T("DOC02.Button.UploadNew");
        BtnReplaceSelected.Content = T("DOC02.Button.ReplaceSelected");
        BtnSaveMetadata.Content = T("DOC02.Button.SaveMetadata");
        BtnArchive.Content = T("DOC02.Button.Archive");
        BtnClearSelection.Content = T("DOC02.Button.ClearSelection");
        TxtListTitle.Text = T("DOC02.List.Title");

        ColFileName.Header = T("DOC02.Column.FileName");
        ColKind.Header = T("DOC02.Column.Kind");
        ColDescription.Header = T("DOC02.Column.Description");
        ColActive.Header = T("DOC02.Column.Active");
        ColSize.Header = T("DOC02.Column.Size");
        ColUploadedBy.Header = T("DOC02.Column.UploadedBy");
        ColUploadedAt.Header = T("DOC02.Column.UploadedAt");
        ColChangedBy.Header = T("DOC02.Column.ChangedBy");
        ColChangedAt.Header = T("DOC02.Column.ChangedAt");
        ColPath.Header = T("DOC02.Column.Path");
    }

    private void InitializeDocumentKinds()
    {
        CmbDocumentKind.ItemsSource = new[]
        {
            "Document",
            "Massblatt",
            "Drawing",
            "Print area",
            "Packaging instruction",
            "Recipe",
            "Sample order",
            "Circular",
            "Checklist",
            "Calculation",
            "Email",
            "Approval",
            "Musterbegleitschein",
            "Production approval",
            "Signed document"
        };
    }

    private void LoadIndex()
    {
        try
        {
            _index = _indexService.LoadAndIndexPhysicalFiles(
                _articleNumber,
                _currentUserName,
                out var createdRecords);

            foreach (var record in createdRecords)
            {
                _logger?.AuditCreated(
                    "DOC02",
                    "ArticleDocumentIndexRecord",
                    record.Id,
                    _currentUserName,
                    BuildAuditDetail(record));
            }

            RefreshGrid();

            _logger?.AdminAction(
                "DOC02",
                "LoadDocumentIndex",
                _currentUserName,
                $"Article={_articleNumber}; Count={_index.Documents.Count}; IndexedExisting={createdRecords.Count}; Folder={_articleFolderPath}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC02 failed to load document index. Article={_articleNumber}; Folder={_articleFolderPath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.LoadFailed.Title"),
                TF("DOC02.Dialog.LoadFailed.Body", ex.Message, _articleFolderPath));
        }
    }

    private void RefreshGrid()
    {
        var records = _index.Documents
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.DocumentKind)
            .ThenBy(item => item.StoredFileName)
            .Select(ToDisplayItem)
            .ToList();

        DgvDocuments.ItemsSource = records;
        TxtStatus.Text = TF("DOC02.Status.Loaded", records.Count, _indexService.IndexFilePath);
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

    private void BtnOpenDoc03_Click(object sender, RoutedEventArgs e)
    {
        TransactionRequested?.Invoke($"DOC03 {_articleNumber}");
    }

    private void BtnOpenTec03_Click(object sender, RoutedEventArgs e)
    {
        TransactionRequested?.Invoke($"TEC03 {_articleNumber}");
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmNavigationAway())
        {
            return;
        }

        _hasUnsavedChanges = false;
        LoadIndex();
        ClearEditor();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("DOC02.FileDialog.Title"),
            Filter = T("DOC02.FileDialog.Filter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        _selectedSourceFilePath = dialog.FileName;
        TxtSourceFile.Text = _selectedSourceFilePath;

        if (string.IsNullOrWhiteSpace(CmbDocumentKind.Text))
        {
            CmbDocumentKind.Text = DmsArticleDocumentIndexService.DetectDocumentKind(dialog.FileName);
        }

        _hasUnsavedChanges = true;
    }

    private void BtnUploadNew_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedSourceFilePath))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.NoFile.Title"),
                T("DOC02.Dialog.NoFile.Body"));
            return;
        }

        try
        {
            var id = _indexService.CopyNewDocument(
                _index,
                _selectedSourceFilePath,
                _articleNumber,
                CmbDocumentKind.Text,
                TxtDescription.Text,
                _currentUserName);

            var record = _index.Documents.First(item => item.Id == id);

            _logger?.AuditCreated(
                "DOC02",
                "ArticleDocument",
                record.Id,
                _currentUserName,
                BuildAuditDetail(record));

            _logger?.AdminAction(
                "DOC02",
                "UploadArticleDocument",
                _currentUserName,
                $"Article={_articleNumber}; DocumentId={record.Id}; File={record.StoredFileName}");

            _hasUnsavedChanges = false;
            ClearEditor();
            RefreshGrid();

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.Uploaded.Title"),
                TF("DOC02.Dialog.Uploaded.Body", record.StoredFileName));
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC02 failed to upload document. Article={_articleNumber}; Source={_selectedSourceFilePath}", ex);
            ShowOperationFailed(ex);
        }
    }

    private void BtnReplaceSelected_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDocumentId))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.NoSelection.Title"),
                T("DOC02.Dialog.NoSelection.Body"));
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSourceFilePath))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.NoFile.Title"),
                T("DOC02.Dialog.NoFile.Body"));
            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("DOC02.Dialog.Replace.Title"),
            T("DOC02.Dialog.Replace.Body"));

        if (!confirm)
        {
            return;
        }

        try
        {
            var before = Clone(SelectedRecordRequired());
            var after = _indexService.ReplaceDocumentFile(
                _index,
                _selectedDocumentId,
                _selectedSourceFilePath,
                _currentUserName);

            LogDocumentRecordChanges(before, after);

            _logger?.AdminAction(
                "DOC02",
                "ReplaceArticleDocumentFile",
                _currentUserName,
                $"Article={_articleNumber}; DocumentId={after.Id}; File={after.StoredFileName}; Source={_selectedSourceFilePath}");

            _hasUnsavedChanges = false;
            RefreshGrid();
            Reselect(after.Id);
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC02 failed to replace document file. Article={_articleNumber}; DocumentId={_selectedDocumentId}; Source={_selectedSourceFilePath}", ex);
            ShowOperationFailed(ex);
        }
    }

    private void BtnSaveMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDocumentId))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.NoSelection.Title"),
                T("DOC02.Dialog.NoSelection.Body"));
            return;
        }

        try
        {
            var before = Clone(SelectedRecordRequired());
            var after = _indexService.UpdateMetadata(
                _index,
                _selectedDocumentId,
                CmbDocumentKind.Text,
                TxtDescription.Text,
                ChkIsActive.IsChecked == true,
                _currentUserName);

            LogDocumentRecordChanges(before, after);

            _logger?.AdminAction(
                "DOC02",
                "SaveArticleDocumentMetadata",
                _currentUserName,
                $"Article={_articleNumber}; DocumentId={after.Id}; File={after.StoredFileName}");

            _hasUnsavedChanges = false;
            RefreshGrid();
            Reselect(after.Id);
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC02 failed to save document metadata. Article={_articleNumber}; DocumentId={_selectedDocumentId}", ex);
            ShowOperationFailed(ex);
        }
    }

    private void BtnArchive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDocumentId))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC02.Dialog.NoSelection.Title"),
                T("DOC02.Dialog.NoSelection.Body"));
            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("DOC02.Dialog.Archive.Title"),
            T("DOC02.Dialog.Archive.Body"));

        if (!confirm)
        {
            return;
        }

        try
        {
            var before = Clone(SelectedRecordRequired());
            var after = _indexService.ArchiveDocument(
                _index,
                _selectedDocumentId,
                _currentUserName);

            _logger?.AuditDeleted(
                "DOC02",
                "ArticleDocument",
                after.Id,
                _currentUserName,
                BuildAuditDetail(after));

            LogDocumentRecordChanges(before, after);

            _hasUnsavedChanges = false;
            RefreshGrid();
            ClearEditor();
        }
        catch (Exception ex)
        {
            _logger?.Error($"DOC02 failed to archive document. Article={_articleNumber}; DocumentId={_selectedDocumentId}", ex);
            ShowOperationFailed(ex);
        }
    }

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
        DgvDocuments.SelectedItem = null;
    }

    private void EditorChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSelection)
        {
            return;
        }

        _hasUnsavedChanges = true;
    }

    private void DgvDocuments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelection)
        {
            return;
        }

        if (DgvDocuments.SelectedItem is not ArticleDocumentDisplayItem item)
        {
            return;
        }

        ShowRecord(item.Record);
    }

    private void DgvDocuments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgvDocuments.SelectedItem is not ArticleDocumentDisplayItem item)
        {
            return;
        }

        var path = item.FullPath;

        if (!File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void ShowRecord(DmsArticleDocumentRecord record)
    {
        _isLoadingSelection = true;

        try
        {
            _selectedDocumentId = record.Id;
            CmbDocumentKind.Text = record.DocumentKind;
            TxtDescription.Text = record.Description;
            ChkIsActive.IsChecked = record.IsActive;
            TxtSelectedInfo.Text = TF("DOC02.Selected", record.StoredFileName, record.Id);
            _hasUnsavedChanges = false;
        }
        finally
        {
            _isLoadingSelection = false;
        }
    }

    private void ClearEditor()
    {
        _isLoadingSelection = true;

        try
        {
            _selectedDocumentId = null;
            _selectedSourceFilePath = null;
            TxtSourceFile.Text = string.Empty;
            CmbDocumentKind.Text = "Document";
            TxtDescription.Text = string.Empty;
            ChkIsActive.IsChecked = true;
            TxtSelectedInfo.Text = T("DOC02.NoSelection");
            _hasUnsavedChanges = false;
        }
        finally
        {
            _isLoadingSelection = false;
        }
    }

    private DmsArticleDocumentRecord SelectedRecordRequired()
    {
        var record = _index.Documents.FirstOrDefault(item =>
            string.Equals(item.Id, _selectedDocumentId, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            throw new InvalidOperationException("Selected document record was not found.");
        }

        return record;
    }

    private void Reselect(string documentId)
    {
        foreach (var item in DgvDocuments.Items.OfType<ArticleDocumentDisplayItem>())
        {
            if (!string.Equals(item.Id, documentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DgvDocuments.SelectedItem = item;
            DgvDocuments.ScrollIntoView(item);
            return;
        }
    }

    private void LogDocumentRecordChanges(
        DmsArticleDocumentRecord before,
        DmsArticleDocumentRecord after)
    {
        LogFieldChange(after.Id, "DocumentKind", before.DocumentKind, after.DocumentKind);
        LogFieldChange(after.Id, "Description", before.Description, after.Description);
        LogFieldChange(after.Id, "OriginalFileName", before.OriginalFileName, after.OriginalFileName);
        LogFieldChange(after.Id, "Extension", before.Extension, after.Extension);
        LogFieldChange(after.Id, "SizeBytes", before.SizeBytes.ToString(), after.SizeBytes.ToString());
        LogFieldChange(after.Id, "Sha256", before.Sha256, after.Sha256);
        LogFieldChange(after.Id, "IsActive", before.IsActive.ToString(), after.IsActive.ToString());
        LogFieldChange(after.Id, "ChangedBy", before.ChangedBy, after.ChangedBy);
        LogFieldChange(after.Id, "ChangedAt", before.ChangedAt?.ToString("O"), after.ChangedAt?.ToString("O"));
    }

    private void LogFieldChange(
        string documentId,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "DOC02",
            "ArticleDocument",
            documentId,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private static DmsArticleDocumentRecord Clone(DmsArticleDocumentRecord source)
    {
        return new DmsArticleDocumentRecord
        {
            Id = source.Id,
            ArticleNumber = source.ArticleNumber,
            StoredFileName = source.StoredFileName,
            OriginalFileName = source.OriginalFileName,
            DocumentKind = source.DocumentKind,
            Description = source.Description,
            Extension = source.Extension,
            SizeBytes = source.SizeBytes,
            Sha256 = source.Sha256,
            UploadedBy = source.UploadedBy,
            UploadedAt = source.UploadedAt,
            ChangedBy = source.ChangedBy,
            ChangedAt = source.ChangedAt,
            IsActive = source.IsActive
        };
    }

    private static string BuildAuditDetail(DmsArticleDocumentRecord record)
    {
        return $"Article={record.ArticleNumber}; File={record.StoredFileName}; OriginalFile={record.OriginalFileName}; Kind={record.DocumentKind}; UploadedBy={record.UploadedBy}; UploadedAt={record.UploadedAt:O}; ChangedBy={record.ChangedBy}; ChangedAt={record.ChangedAt:O}; Active={record.IsActive}";
    }

    private void ShowOperationFailed(Exception ex)
    {
        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("DOC02.Dialog.OperationFailed.Title"),
            TF("DOC02.Dialog.OperationFailed.Body", ex.Message));
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
