using System;
using System.Collections.Generic;
using System.Linq;
using DMS.Core.Documents;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Documents;

public partial class ArticleDocumentCreateView : UserControl
{
    private readonly string _articleNumber;
    private readonly string _dmsRootPath;
    private readonly string _articleFolderPath;
    private readonly DmsArticleDocumentIndexService _indexService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private SapMaterial? _sapMaterial;

    public event Action<string>? TransactionRequested;

    public ArticleDocumentCreateView(
        string articleNumber,
        string dmsRootPath,
        string articleFolderPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _articleNumber = articleNumber;
        _dmsRootPath = dmsRootPath;
        _articleFolderPath = articleFolderPath;
        _indexService = new DmsArticleDocumentIndexService(articleFolderPath);
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        LoadSapMaterialAndDocumentState();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = TF("DOC01.Title", _articleNumber);
        TxtSummary.Text = T("DOC01.Summary");
        TxtFolderLabel.Text = T("DOC01.Folder");
        TxtFolder.Text = _articleFolderPath;
        TxtIndexLabel.Text = T("DOC01.IndexFile");
        TxtIndexPath.Text = _indexService.IndexFilePath;

        BtnCreate.Content = T("DOC01.Button.Create");
        BtnOpenDoc02.Content = T("DOC01.Button.OpenDoc02");
        BtnOpenDoc03.Content = T("DOC01.Button.OpenDoc03");
        BtnOpenTec03.Content = T("DOC01.Button.OpenTec03");
        BtnRefresh.Content = T("DOC01.Button.Refresh");

        TxtSapTitle.Text = T("DOC01.SapTitle");
        TxtDocumentStateTitle.Text = T("DOC01.DocumentStateTitle");
        TxtEmptyDocumentsTitle.Text = TOrDefault("DOC01.EmptyDocuments.Title", "Žádné dokumenty k zobrazení");
        TxtEmptyDocumentsBody.Text = TOrDefault("DOC01.EmptyDocuments.Body", "Jakmile bude ve složce nebo indexu alespoò jeden dokument, zobrazí se tady pøehledná tabulka.");

        TxtFieldMaterialNumberLabel.Text = T("DOC01.Field.MaterialNumber");
        TxtFieldOldNumberLabel.Text = T("DOC01.Field.OldNumber");
        TxtFieldStatusLabel.Text = T("DOC01.Field.Status");
        TxtFieldKindLabel.Text = T("DOC01.Field.MaterialKind");
        TxtFieldImportedAtLabel.Text = T("DOC01.Field.ImportedAt");
        TxtFieldDescriptionLabel.Text = T("DOC01.Field.Description");

        ColFileName.Header = T("DOC01.Column.FileName");
        ColKind.Header = T("DOC01.Column.Kind");
        ColSize.Header = T("DOC01.Column.Size");
        ColUploadedBy.Header = T("DOC01.Column.UploadedBy");
        ColUploadedAt.Header = T("DOC01.Column.UploadedAt");
        ColChangedBy.Header = T("DOC01.Column.ChangedBy");
        ColChangedAt.Header = T("DOC01.Column.ChangedAt");
        ColFullPath.Header = T("DOC01.Column.Path");
    }

    private void LoadSapMaterialAndDocumentState()
    {
        try
        {
            var sapStoragePaths = new SapStoragePaths(_dmsRootPath);
            var sapMaterialRepository = new JsonSapMaterialRepository(sapStoragePaths.SapMaterialsFilePath);

            _sapMaterial = sapMaterialRepository.FindByMaterialNumber(_articleNumber);

            if (_sapMaterial is null)
            {
                ClearSapFields();
                BtnCreate.IsEnabled = false;
                TxtStatus.Text = TF("DOC01.Status.SapMissing", _articleNumber, sapStoragePaths.SapMaterialsFilePath);
                SetExistingFileRows(Array.Empty<ArticleDocumentCreateDisplayRow>());

                _logger?.Warning(
                    $"DOC01 SAP material was not found. Article={_articleNumber}; SapMaterialsFile={sapStoragePaths.SapMaterialsFilePath}");

                return;
            }

            BtnCreate.IsEnabled = true;
            FillSapFields(_sapMaterial);
            RefreshDocumentState();

            _logger?.AdminAction(
                "DOC01",
                "LoadArticleDocumentationInitialization",
                _currentUserName,
                $"Article={_articleNumber}; SapMaterialsFile={sapStoragePaths.SapMaterialsFilePath}; Folder={_articleFolderPath}");
        }
        catch (Exception ex)
        {
            BtnCreate.IsEnabled = false;
            TxtStatus.Text = TF("DOC01.Status.LoadFailed", ex.Message);
            SetExistingFileRows(Array.Empty<ArticleDocumentCreateDisplayRow>());

            _logger?.Error(
                $"DOC01 failed to load article documentation initialization. Article={_articleNumber}; Root={_dmsRootPath}; Folder={_articleFolderPath}",
                ex);
        }
    }

    private void RefreshDocumentState()
    {
        var folderExists = Directory.Exists(_articleFolderPath);
        var indexExists = File.Exists(_indexService.IndexFilePath);

        List<ArticleDocumentCreateDisplayRow> rows;

        try
        {
            rows = _indexService.LoadDisplayRecords(_articleNumber)
                .Select(ToDisplayRow)
                .ToList();
        }
        catch
        {
            rows = new List<ArticleDocumentCreateDisplayRow>();
        }

        SetExistingFileRows(rows);

        TxtStatus.Text = indexExists
            ? TF("DOC01.Status.IndexExists", rows.Count, _indexService.IndexFilePath)
            : folderExists
                ? TF("DOC01.Status.FolderExistsNoIndex", rows.Count, _articleFolderPath)
                : TF("DOC01.Status.NotInitialized", _articleFolderPath);

        BtnCreate.Content = indexExists
            ? T("DOC01.Button.Reindex")
            : T("DOC01.Button.Create");
    }

    private void SetExistingFileRows(IReadOnlyCollection<ArticleDocumentCreateDisplayRow> rows)
    {
        DgvExistingFiles.ItemsSource = rows;

        var hasRows = rows.Count > 0;

        DgvExistingFiles.Visibility = hasRows
            ? Visibility.Visible
            : Visibility.Collapsed;

        EmptyDocumentsPanel.Visibility = hasRows
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_sapMaterial is null)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC01.Dialog.NoSap.Title"),
                TF("DOC01.Dialog.NoSap.Body", _articleNumber));
            return;
        }

        try
        {
            var indexAlreadyExisted = File.Exists(_indexService.IndexFilePath);
            Directory.CreateDirectory(_articleFolderPath);

            var index = _indexService.Load(_articleNumber);

            if (!indexAlreadyExisted)
            {
                var now = DateTime.Now;
                index.ArticleNumber = _articleNumber;
                index.CreatedAt = now;
                index.UpdatedAt = now;
                _indexService.Save(index);

                _logger?.AuditCreated(
                    "DOC01",
                    "ArticleDocumentIndex",
                    _articleNumber,
                    _currentUserName,
                    BuildIndexAuditDetail(_sapMaterial));
            }

            _indexService.LoadAndIndexPhysicalFiles(
                _articleNumber,
                _currentUserName,
                out var createdRecords);

            foreach (var record in createdRecords)
            {
                _logger?.AuditCreated(
                    "DOC01",
                    "ArticleDocument",
                    record.Id,
                    _currentUserName,
                    BuildDocumentAuditDetail(record));
            }

            _logger?.AdminAction(
                "DOC01",
                indexAlreadyExisted ? "ReindexArticleDocumentation" : "InitializeArticleDocumentation",
                _currentUserName,
                $"Article={_articleNumber}; Folder={_articleFolderPath}; IndexFile={_indexService.IndexFilePath}; NewRecords={createdRecords.Count}");

            RefreshDocumentState();

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC01.Dialog.Created.Title"),
                indexAlreadyExisted
                    ? TF("DOC01.Dialog.Reindexed.Body", createdRecords.Count)
                    : TF("DOC01.Dialog.Created.Body", _articleNumber, createdRecords.Count));
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"DOC01 failed to initialize article documentation. Article={_articleNumber}; Folder={_articleFolderPath}",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("DOC01.Dialog.Failed.Title"),
                TF("DOC01.Dialog.Failed.Body", ex.Message));
        }
    }

    private void BtnOpenDoc02_Click(object sender, RoutedEventArgs e)
    {
        TransactionRequested?.Invoke($"DOC02 {_articleNumber}");
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
        LoadSapMaterialAndDocumentState();
    }

    private void FillSapFields(SapMaterial material)
    {
        TxtFieldMaterialNumber.Text = NullDash(material.MaterialNumber);
        TxtFieldOldNumber.Text = NullDash(material.OldMaterialNumber);
        TxtFieldStatus.Text = NullDash(material.MaterialStatus);
        TxtFieldKind.Text = NullDash(material.MaterialKind);
        TxtFieldImportedAt.Text = material.ImportedAt.ToString("dd.MM.yyyy HH:mm:ss");
        TxtFieldDescription.Text = NullDash(material.Description);
    }

    private void ClearSapFields()
    {
        TxtFieldMaterialNumber.Text = _articleNumber;
        TxtFieldOldNumber.Text = "-";
        TxtFieldStatus.Text = "-";
        TxtFieldKind.Text = "-";
        TxtFieldImportedAt.Text = "-";
        TxtFieldDescription.Text = "-";
    }

    private ArticleDocumentCreateDisplayRow ToDisplayRow(DmsArticleDocumentRecord record)
    {
        return new ArticleDocumentCreateDisplayRow
        {
            StoredFileName = record.StoredFileName,
            DocumentKind = record.DocumentKind,
            SizeText = FormatFileSize(record.SizeBytes),
            UploadedBy = record.UploadedBy,
            UploadedAtText = FormatDate(record.UploadedAt),
            ChangedBy = record.ChangedBy,
            ChangedAtText = FormatDate(record.ChangedAt),
            FullPath = _indexService.GetPhysicalPath(record)
        };
    }

    private string BuildIndexAuditDetail(SapMaterial material)
    {
        return string.Join(
            "; ",
            new[]
            {
                $"Article={_articleNumber}",
                $"SapMaterial={material.MaterialNumber}",
                $"OldMaterialNumber={material.OldMaterialNumber}",
                $"MaterialKind={material.MaterialKind}",
                $"Status={material.MaterialStatus}",
                $"Description={material.Description}",
                $"Folder={_articleFolderPath}",
                $"IndexFile={_indexService.IndexFilePath}"
            });
    }

    private string BuildDocumentAuditDetail(DmsArticleDocumentRecord record)
    {
        return string.Join(
            "; ",
            new[]
            {
                $"Article={record.ArticleNumber}",
                $"StoredFileName={record.StoredFileName}",
                $"OriginalFileName={record.OriginalFileName}",
                $"Kind={record.DocumentKind}",
                $"Extension={record.Extension}",
                $"SizeBytes={record.SizeBytes}",
                $"Sha256={record.Sha256}",
                $"UploadedBy={record.UploadedBy}",
                $"UploadedAt={FormatDate(record.UploadedAt)}",
                $"ChangedBy={record.ChangedBy}",
                $"ChangedAt={FormatDate(record.ChangedAt)}",
                $"Active={record.IsActive}"
            });
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TOrDefault(string key, string fallback)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? fallback : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var formatted = _translateFormat(key, args);
            return IsMissing(formatted, key) ? key : formatted;
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
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("dd.MM.yyyy HH:mm:ss")
            : "-";
    }

    private static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
        {
            return $"{sizeBytes} B";
        }

        if (sizeBytes < 1024 * 1024)
        {
            return $"{sizeBytes / 1024d:0.0} KB";
        }

        return $"{sizeBytes / 1024d / 1024d:0.0} MB";
    }

    private sealed class ArticleDocumentCreateDisplayRow
    {
        public string StoredFileName { get; init; } = string.Empty;

        public string DocumentKind { get; init; } = string.Empty;

        public string SizeText { get; init; } = string.Empty;

        public string UploadedBy { get; init; } = string.Empty;

        public string UploadedAtText { get; init; } = string.Empty;

        public string ChangedBy { get; init; } = string.Empty;

        public string ChangedAtText { get; init; } = string.Empty;

        public string FullPath { get; init; } = string.Empty;
    }
}
