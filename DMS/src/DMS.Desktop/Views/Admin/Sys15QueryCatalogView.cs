using DMS.Desktop.Logging;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Admin;

/// <summary>
/// SYS15 v0.4: centrally inventoried query locations and versioned editable DRAFTS.
/// No drafts are executed or installed into MES06/ORD10/other live services.
/// </summary>
public sealed class Sys15QueryCatalogView : UserControl
{
    private readonly DmsQueryDraftCatalog _catalog;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly string _configRoot;
    private readonly Func<string, string> _translate;
    private readonly ObservableCollection<QueryRow> _rows = new();
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single, MinWidth = 360
    };
    private readonly TextBox _title = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _sql = new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 12, AcceptsTab = true,
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 160
    };
    private readonly TextBox _notes = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 53,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBox _relations = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 76,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly ListBox _revisions = new() { Height = 93 };
    private readonly TextBlock _origin = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _filter = new() { MinWidth = 130 };
    private List<DmsQueryDraft> _drafts = new();
    private string _fingerprint = string.Empty;
    private string? _selectedCode;
    private bool _loading;
    private bool _dirty;
    private sealed class QueryRow
    {
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Transaction { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public DmsQueryDescriptor? BuiltIn { get; init; }
        public DmsQueryDraft? Draft { get; init; }
    }
    private sealed record RevisionItem(DmsQueryRevision Revision)
    {
        public override string ToString() => $"v{Revision.Version} · {Revision.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm} · {Revision.User}";
    }

    public Sys15QueryCatalogView(string configRoot, DmsLogger logger, string user, Func<string, string> translate)
    {
        _configRoot = configRoot;
        _logger = logger;
        _user = user;
        _translate = translate;
        _catalog = new DmsQueryDraftCatalog(configRoot);
        BuildUi();
        Reload();
        _logger.AdminAction("SYS15", "QUERY_CATALOG_OPEN", _user,
            "Read-only inventory and versioned query draft editor opened");
    }

    private string T(string key, string fallback)
    {
        var result = _translate(key);
        return string.IsNullOrWhiteSpace(result) || result == key || result.StartsWith("[[", StringComparison.Ordinal)
            ? fallback : result;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 6, 5), MinHeight = 30 };
        button.Click += click;
        return button;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 6, 0, 4)
    };

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var information = new TextBlock
        {
            Text = T("SYS15.Query.Info", "Katalog ukazuje ověřená místa použití dotazů. Návrhy jsou pouze textové verze: uložení nikdy nemění SQL běžící v DMS."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        top.Children.Add(information);
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        buttons.Children.Add(Button(T("SYS15.Query.New", "Nový návrh"), (_, _) => NewDraft()));
        buttons.Children.Add(Button(T("SYS15.Query.Save", "Uložit novou verzi"), (_, _) => SaveDraft()));
        buttons.Children.Add(Button(T("SYS15.Query.Validate", "Zkontrolovat text"), (_, _) => ValidateSql()));
        buttons.Children.Add(Button(T("SYS15.Query.Restore", "Načíst vybranou verzi"), (_, _) => RestoreRevision()));
        buttons.Children.Add(Button(T("SYS15.Query.Revert", "Odebrat návrh"), (_, _) => DeleteDraft()));
        buttons.Children.Add(Button(T("SYS15.Query.Reload", "Obnovit katalog"), (_, _) => ReloadWithPrompt()));
        top.Children.Add(_status);
        var body = new Grid(); root.Children.Add(body);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel(); Grid.SetColumn(left, 0); body.Children.Add(left);
        var searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        DockPanel.SetDock(searchPanel, Dock.Top); left.Children.Add(searchPanel);
        searchPanel.Children.Add(Label(T("SYS15.Query.Search", "Hledání podle transakce, názvu nebo souboru")));
        searchPanel.Children.Add(_filter);
        _filter.TextChanged += (_, _) => FilterRows();
        searchPanel.Children.Add(_summary);
        _grid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Query.Code", "Kód"),
            Binding = new Binding(nameof(QueryRow.Code)), Width = new DataGridLength(155) });
        _grid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Query.Transaction", "Transakce"),
            Binding = new Binding(nameof(QueryRow.Transaction)), Width = new DataGridLength(86) });
        _grid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Query.Status", "Stav"),
            Binding = new Binding(nameof(QueryRow.Status)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => SelectionChanged();
        left.Children.Add(_grid);

        var right = new Grid(); Grid.SetColumn(right, 2); body.Children.Add(right);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel(); Grid.SetRow(header, 0); right.Children.Add(header);
        header.Children.Add(Label(T("SYS15.Query.Title", "Název dotazu")));
        header.Children.Add(_title);
        header.Children.Add(_origin);
        right.Children.Add(_sql); Grid.SetRow(_sql, 2);
        var sqlHeading = Label(T("SYS15.Query.Sql", "Text SQL (návrh – není aktivní)"));
        Grid.SetRow(sqlHeading, 1); right.Children.Add(sqlHeading);
        var lower = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        Grid.SetRow(lower, 3); right.Children.Add(lower);
        lower.Children.Add(Label(T("SYS15.Query.Note", "Poznámka ke změně")));
        lower.Children.Add(_notes);
        lower.Children.Add(Label(T("SYS15.Query.Relations", "Vazby výslovně uvedené v SQL (orientační rozbor)")));
        lower.Children.Add(_relations);
        var historyPanel = new StackPanel(); Grid.SetRow(historyPanel, 4); right.Children.Add(historyPanel);
        historyPanel.Children.Add(Label(T("SYS15.Query.History", "Uložené verze")));
        historyPanel.Children.Add(_revisions);
        _title.TextChanged += (_, _) => MarkDirty();
        _sql.TextChanged += (_, _) => { MarkDirty(); UpdateRelations(); };
        _notes.TextChanged += (_, _) => MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || _selectedCode is null) return;
        _dirty = true;
        _status.Text = T("SYS15.Query.Unsaved", "Neuložené úpravy – ulož novou verzi.");
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        return MessageBox.Show(T("SYS15.Query.Discard", "Zahodit neuložené úpravy?"),
            T("SYS15.Query.Confirm", "Potvrzení"), MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void ReloadWithPrompt()
    {
        if (ConfirmDiscard()) Reload();
    }

    private void Reload()
    {
        try
        {
            var (drafts, fingerprint) = _catalog.Load();
            _drafts = drafts;
            _fingerprint = fingerprint;
            var previous = _selectedCode;
            _loading = true;
            _rows.Clear();
            foreach (var seed in DmsQueryDraftCatalog.Inventory)
            {
                var draft = _drafts.FirstOrDefault(x => x.Code.Equals(seed.Code, StringComparison.OrdinalIgnoreCase));
                _rows.Add(new QueryRow { Code = seed.Code, Title = draft?.Title ?? seed.Title,
                    Transaction = seed.Transaction, SourceFile = seed.SourceFile,
                    BuiltIn = seed, Draft = draft,
                    Status = draft is null ? (seed.HasBuiltInSql ? T("SYS15.Query.Builtin", "V kódu")
                        : T("SYS15.Query.Metadata", "Odkaz"))
                        : T("SYS15.Query.Draft", "Návrh v") + draft.Version });
            }
            foreach (var draft in _drafts.Where(d => DmsQueryDraftCatalog.Inventory.All(s =>
                         !s.Code.Equals(d.Code, StringComparison.OrdinalIgnoreCase))))
                _rows.Add(new QueryRow { Code = draft.Code, Title = draft.Title,
                    Transaction = draft.Transaction, SourceFile = draft.SourceFile,
                    Draft = draft, Status = T("SYS15.Query.Draft", "Návrh v") + draft.Version });
            _grid.ItemsSource = _rows;
            FilterRows();
            _grid.SelectedItem = _rows.FirstOrDefault(x => x.Code == previous) ?? _rows.FirstOrDefault();
            _selectedCode = null;
            _dirty = false;
            _loading = false;
            ShowSelected();
            _status.Text = T("SYS15.Query.Loaded", "Katalog a návrhy byly načteny.");
        }
        catch (Exception ex)
        {
            _loading = false;
            _status.Text = ex.Message;
            _logger.Error("SYS15 query draft catalog load failed", ex);
        }
    }

    private void FilterRows()
    {
        if (_grid.ItemsSource is null) return;
        var filter = _filter.Text.Trim();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_grid.ItemsSource);
        view.Filter = item => item is QueryRow row && (filter.Length == 0 ||
            new[] { row.Code, row.Title, row.Transaction, row.SourceFile }
            .Any(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _summary.Text = $"{T("SYS15.Query.Count", "Počet položek")}: {view.Cast<object>().Count()}";
    }

    private void SelectionChanged()
    {
        if (_loading) return;
        var next = (_grid.SelectedItem as QueryRow)?.Code;
        if (_selectedCode == next) return;
        if (!ConfirmDiscard())
        {
            _loading = true;
            _grid.SelectedItem = _rows.FirstOrDefault(x => x.Code == _selectedCode);
            _loading = false;
            return;
        }
        ShowSelected();
    }

    private void ShowSelected()
    {
        _loading = true;
        var row = _grid.SelectedItem as QueryRow;
        _selectedCode = row?.Code;
        _title.Text = row?.Draft?.Title ?? row?.Title ?? string.Empty;
        _notes.Text = row?.Draft?.Notes ?? string.Empty;
        _sql.Text = row?.Draft?.Sql ?? GetOriginalSql(row);
        _origin.Text = row is null ? string.Empty :
            $"{T("SYS15.Query.Origin", "Zdroj")}: {row.SourceFile}  |  {T("SYS15.Query.Method", "Metoda")}: {row.BuiltIn?.Method ?? row.Draft?.Method ?? "–"}\n" +
            $"{T("SYS15.Query.Safe", "Návrhy se nikdy nespouštějí ani neaktivují. Stávající MES06 SQL se spravuje na vedlejší kartě.")}";
        _revisions.ItemsSource = (row?.Draft?.History ?? new()).OrderByDescending(x => x.Version).Select(x => new RevisionItem(x)).ToList();
        _loading = false;
        _dirty = false;
        UpdateRelations();
    }

    private string GetOriginalSql(QueryRow? row)
    {
        var code = row?.BuiltIn?.BuiltInSqlCode;
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        try
        {
            var active = MesSqlOverrideCatalog.Load(MesSqlOverrideCatalog.FilePath(_configRoot));
            if (active.TryGetValue(code, out var sql) && !string.IsNullOrWhiteSpace(sql)) return sql;
            return MesSqlOverrideCatalog.BuiltIn(code);
        }
        catch (Exception ex)
        {
            _logger.Error("SYS15 query catalog failed to load MES06 baseline SQL", ex);
            return MesSqlOverrideCatalog.BuiltIn(code);
        }
    }

    private void NewDraft()
    {
        if (!ConfirmDiscard()) return;
        var next = Enumerable.Range(1, 999).Select(x => $"CUSTOM_{x:000}")
            .FirstOrDefault(code => _rows.All(r => !r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));
        if (next is null) { _status.Text = T("SYS15.Query.Full", "Katalog je plný."); return; }
        _loading = true;
        _filter.Text = string.Empty;
        var row = new QueryRow { Code = next, Title = T("SYS15.Query.NewTitle", "Nový dotaz"),
            Transaction = "CUSTOM", Status = T("SYS15.Query.New", "Nový"), SourceFile = string.Empty };
        _rows.Add(row);
        _grid.SelectedItem = row;
        _loading = false;
        ShowSelected();
        _dirty = true;
        _sql.Focus();
    }

    private void ValidateSql()
    {
        if (_grid.SelectedItem is not QueryRow row) return;
        try
        {
            CheckDraftText(_sql.Text);
            if (row.BuiltIn?.BuiltInSqlCode is string code)
            {
                MesSqlOverrideCatalog.Validate(_sql.Text);
                _status.Text = T("SYS15.Query.StructureWarning", "Prošel statický test MES06. Živý test sloupců a aktivaci proveď na kartě SQL MES06.");
            }
            else _status.Text = T("SYS15.Query.StaticOnly", "Prošel pouze základní textový test. SQL nebylo spuštěno ani ověřeno proti databázi.");
            _logger.AdminAction("SYS15", "QUERY_DRAFT_VALIDATE", _user, $"Code={row.Code}; Executed=False");
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _logger.Warning($"SYS15 query draft validation rejected. Code={row.Code}; Reason={ex.Message}");
        }
    }

    private static void CheckDraftText(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 50_000)
            throw new InvalidOperationException("SQL draft is empty or exceeds 50,000 characters.");
        if (sql.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t')))
            throw new InvalidOperationException("SQL draft contains control characters.");
        // Advisory text check only; NEVER use regex as an authorization boundary for running SQL.
        if (!Regex.IsMatch(sql.TrimStart(), @"\A(?:SELECT|WITH)\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Draft must start with SELECT or WITH.");
    }

    private void SaveDraft()
    {
        if (_grid.SelectedItem is not QueryRow row) return;
        try
        {
            CheckDraftText(_sql.Text);
            var previous = _drafts.FirstOrDefault(x => x.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
            var sql = _sql.Text;
            var title = _title.Text.Trim();
            if (title.Length == 0 || title.Length > 180) throw new InvalidOperationException("Query title must contain 1–180 characters.");
            var note = _notes.Text.Trim();
            if (note.Length > 2000) throw new InvalidOperationException("Revision notes exceed 2,000 characters.");
            if (previous is not null && previous.Sql == sql && previous.Notes == note && previous.Title == title)
            { _status.Text = T("SYS15.Query.Unchanged", "Nebyla provedena žádná změna."); return; }
            var version = (previous?.Version ?? 0) + 1;
            var revision = new DmsQueryRevision
            {
                Version = version, Sql = sql, Notes = note, User = _user,
                SavedUtc = DateTimeOffset.UtcNow, SqlSha256 = DmsQueryDraftCatalog.HashSql(sql)
            };
            var draft = new DmsQueryDraft
            {
                Code = row.Code, Title = title,
                Transaction = row.BuiltIn?.Transaction ?? previous?.Transaction ?? "CUSTOM",
                SourceFile = row.BuiltIn?.SourceFile ?? previous?.SourceFile ?? string.Empty,
                Method = row.BuiltIn?.Method ?? previous?.Method ?? string.Empty,
                DataSource = row.BuiltIn?.DataSource ?? previous?.DataSource ?? "MES",
                Version = version, Sql = sql, Notes = note,
                UpdatedBy = _user, UpdatedUtc = revision.SavedUtc,
                History = (previous?.History ?? new()).Concat(new[] { revision })
                    .OrderByDescending(x => x.Version).Take(30).OrderBy(x => x.Version).ToList()
            };
            var updated = _drafts.Where(x => !x.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            updated.Add(draft);
            _fingerprint = _catalog.Save(updated, _fingerprint);
            if (previous is null)
                _logger.AuditCreated("SYS15", "QueryDraft", row.Code, _user,
                    $"Version={version}; Transaction={draft.Transaction}; SQL_SHA256={revision.SqlSha256}; Executed=False");
            else
                _logger.AuditChange("SYS15", "QueryDraft", row.Code, "Version/SQL_SHA256",
                    $"v{previous.Version}/{DmsQueryDraftCatalog.HashSql(previous.Sql)}",
                    $"v{version}/{revision.SqlSha256}", _user);
            _logger.AdminAction("SYS15", "QUERY_DRAFT_SAVE", _user,
                $"Code={row.Code}; Version={version}; Executed=False; Activated=False");
            _selectedCode = row.Code;
            Reload();
            _status.Text = $"{T("SYS15.Query.Saved", "Návrh uložen")}: {row.Code} v{version}. {T("SYS15.Query.NotActive", "Žádný živý dotaz nebyl změněn.")}";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _logger.Error("SYS15 query draft save failed", ex);
        }
    }

    private void RestoreRevision()
    {
        if (_revisions.SelectedItem is not RevisionItem chosen) return;
        _loading = true;
        _sql.Text = chosen.Revision.Sql;
        _notes.Text = chosen.Revision.Notes;
        _loading = false;
        _dirty = true;
        UpdateRelations();
        _status.Text = T("SYS15.Query.RestoredToEditor", "Starší verze načtena do editoru. Ulož ji jako novou verzi.");
    }

    private void DeleteDraft()
    {
        if (_grid.SelectedItem is not QueryRow row) return;
        var previous = _drafts.FirstOrDefault(x => x.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
        if (previous is null) return;
        if (MessageBox.Show(T("SYS15.Query.DeleteConfirm", "Odebrat uložený návrh a jeho historii?"),
                T("SYS15.Query.Confirm", "Potvrzení"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var updated = _drafts.Where(x => !x.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            _fingerprint = _catalog.Save(updated, _fingerprint);
            _logger.AuditDeleted("SYS15", "QueryDraft", row.Code, _user,
                $"Removed version={previous.Version}; SQL_SHA256={DmsQueryDraftCatalog.HashSql(previous.Sql)}; no execution changes");
            _selectedCode = row.Code;
            _dirty = false;
            Reload();
            _status.Text = T("SYS15.Query.Deleted", "Návrh odebrán, zdrojový SQL kód zůstal nedotčen.");
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _logger.Error("SYS15 query draft delete failed", ex);
        }
    }

    private void UpdateRelations()
    {
        // Only explicit JOIN ... ON equality seen in the SQL text; no guessed foreign keys.
        try
        {
        var matches = Regex.Matches(_sql.Text,
            @"\bJOIN\s+(?<table>(?:\[[^\]]+\]\.)?\[[^\]]+\])\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s+ON\s+(?<left>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<right>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
        var relations = matches.Cast<Match>().Take(30)
            .Select(m => $"{m.Groups["left"].Value} = {m.Groups["right"].Value}   [{m.Groups["table"].Value}]")
            .ToList();
        _relations.Text = relations.Count == 0
            ? T("SYS15.Query.NoRelations", "Žádné jednoduché vazby JOIN ... ON se nepodařilo ověřit z textu.")
            : string.Join(Environment.NewLine, relations);
        }
        catch (RegexMatchTimeoutException)
        {
            _relations.Text = T("SYS15.Query.RelationTimeout", "Rozbor vazeb překročil časový limit.");
        }
    }
}
