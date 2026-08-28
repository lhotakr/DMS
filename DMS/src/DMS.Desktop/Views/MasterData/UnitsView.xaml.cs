using DMS.Desktop.UI;
using DMS.Core.Domain.Units;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.MasterData;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.MasterData;

public sealed class UnitChoice
{
    public required UnitDefinition Unit { get; init; }
    public string Code => Unit.Code;
    public string DisplayText => string.IsNullOrWhiteSpace(Unit.Name)
        ? $"{Unit.Code} — {Unit.Symbol}"
        : $"{Unit.Code} — {Unit.Symbol} — {Unit.Name}";
}

public partial class UnitsView : UserControl
{
    private readonly DmsMasterDataService _service;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;

    private List<UnitDimension> _dimensions = new();
    private List<UnitDefinition> _allUnits = new();
    private readonly ObservableCollection<UnitDefinition> _visibleUnits = new();

    private Dictionary<Guid, UnitDimension> _originalDimensions = new();
    private Dictionary<Guid, UnitDefinition> _originalUnits = new();

    public UnitsView(
        DmsMasterDataService service,
        DmsLogger logger,
        string user,
        Func<string, string>? translate = null)
    {
        InitializeComponent();
        _service = service;
        _logger = logger;
        _user = user;
        _translate = translate ?? (key => key);

        GridUnits.ItemsSource = _visibleUnits;
        ApplyLocalization();
        LoadData();
    }

    private string T(string key) => _translate(key);

    private void ApplyLocalization()
    {
        TxtDimensionsTitle.Text = T("SYS01.Units.Dimensions");
        BtnAddDimension.Content = T("SYS01.Units.AddDimension");

        TxtDimensionDetailsTitle.Text = T("SYS01.Units.DimensionDetails");
        LblDimensionCode.Text = T("SYS01.Units.DimensionCode");
        LblDimensionName.Text = T("SYS01.Units.DimensionName");
        LblBaseUnit.Text = T("SYS01.Units.BaseUnit");
        LblSortOrder.Text = T("SYS01.Units.SortOrder");
        ChkDimensionActive.Content = T("SYS01.Units.DimensionActive");
        TxtBaseUnitHint.Text = T("SYS01.Units.BaseUnitHint");

        ColCode.Header = T("SYS01.Units.Code");
        ColSymbol.Header = T("SYS01.Units.Symbol");
        ColName.Header = T("SYS01.Units.Name");
        ColScale.Header = T("SYS01.Units.ScaleToBase");
        ColOffset.Header = T("SYS01.Units.OffsetToBase");
        ColDecimals.Header = T("SYS01.Units.DecimalPlaces");
        ColDefault.Header = T("SYS01.Units.Default");
        ColActive.Header = T("SYS01.MasterData.Active");

        TxtConverterTitle.Text = T("SYS01.Units.TestConversion");
        BtnConvert.Content = T("SYS01.Units.Convert");
        BtnSave.Content = T("SYS01.Units.Save");
    }

    private void LoadData()
    {
        _dimensions = _service.LoadUnitDimensions();
        _allUnits = _service.LoadUnits();

        CaptureOriginalSnapshot();

        ListDimensions.ItemsSource = _dimensions
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToList();

        if (ListDimensions.Items.Count > 0)
        {
            ListDimensions.SelectedIndex = 0;
        }
        else
        {
            _visibleUnits.Clear();
            RefreshBaseUnitChoices();
            RefreshConverter();
        }

        TxtStatus.Text =
            $"{T("SYS01.MasterData.Files")}: {_service.UnitDimensionsPath}; {_service.UnitsPath}";
    }

    private void CaptureOriginalSnapshot()
    {
        _originalDimensions = _dimensions
            .Select(CloneDimension)
            .ToDictionary(x => x.UnitDimensionId);

        _originalUnits = _allUnits
            .Select(CloneUnit)
            .ToDictionary(x => x.UnitDefinitionId);
    }

    private void DimensionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _visibleUnits.Clear();

        if (ListDimensions.SelectedItem is not UnitDimension dimension)
        {
            RefreshBaseUnitChoices();
            RefreshConverter();
            return;
        }

        foreach (var unit in _allUnits
                     .Where(x => x.UnitDimensionId == dimension.UnitDimensionId)
                     .OrderBy(x => x.Code))
        {
            _visibleUnits.Add(unit);
        }

        RefreshBaseUnitChoices();
        RefreshConverter();
    }

    private void GridUnits_RowEditEnding(
        object sender,
        DataGridRowEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                RefreshBaseUnitChoices();
                RefreshConverter();
            }));
    }

    private void RefreshBaseUnitChoices()
    {
        var selectedBaseCode =
            ListDimensions.SelectedItem is UnitDimension dimension
                ? dimension.BaseUnitCode
                : string.Empty;

        CmbBaseUnit.ItemsSource = _visibleUnits
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new UnitChoice { Unit = x })
            .ToList();

        CmbBaseUnit.Text = selectedBaseCode;
    }

    private void RefreshConverter()
    {
        var choices = _visibleUnits
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new UnitChoice { Unit = x })
            .ToList();

        CmbSource.ItemsSource = choices;
        CmbTarget.ItemsSource = choices;

        if (choices.Count > 0)
        {
            CmbSource.SelectedIndex = 0;
            CmbTarget.SelectedIndex = Math.Min(1, choices.Count - 1);
        }
        else
        {
            TxtResult.Text = string.Empty;
        }
    }

    private void AddDimension_Click(
        object sender,
        RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);

        var rawCode = DmsTextPromptDialog.Show(
            owner,
            T("SYS01.Units.AddDimension"),
            T("SYS01.Units.DimensionCodePrompt"),
            "NEW");

        if (rawCode is null)
        {
            return;
        }

        var code = rawCode
            .Trim()
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var name = DmsTextPromptDialog.Show(
            owner,
            T("SYS01.Units.AddDimension"),
            T("SYS01.Units.DimensionNamePrompt"),
            code);

        if (name is null ||
            string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var dimension = new UnitDimension
        {
            Code = code,
            Name = name.Trim(),
            BaseUnitCode = string.Empty,
            SortOrder = _dimensions.Count == 0
                ? 10
                : _dimensions.Max(x => x.SortOrder) + 10,
            IsActive = true
        };

        _dimensions.Add(dimension);

        ListDimensions.ItemsSource = _dimensions
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToList();

        ListDimensions.SelectedItem = dimension;
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        GridUnits.CommitEdit(
            DataGridEditingUnit.Cell,
            true);

        GridUnits.CommitEdit(
            DataGridEditingUnit.Row,
            true);

        SyncSelectedUnits();
        NormalizeValues();

        if (!ValidateBeforeSave())
        {
            return;
        }

        try
        {
            _service.SaveUnits(
                _dimensions,
                _allUnits);

            WriteAuditChanges();

            _logger.AdminAction(
                "SYS01",
                "SaveUnits",
                _user,
                $"Dimensions={_dimensions.Count}; Units={_allUnits.Count}");

            TxtStatus.Text = string.Format(
                T("SYS01.Units.Status.Saved"),
                _dimensions.Count,
                _allUnits.Count);

            LoadData();
        }
        catch (Exception ex)
        {
            DmsMessage.Show(
                ex.Message,
                T("SYS01.Units.SaveErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SyncSelectedUnits()
    {
        if (ListDimensions.SelectedItem is not UnitDimension dimension)
        {
            return;
        }

        _allUnits.RemoveAll(x =>
            x.UnitDimensionId ==
            dimension.UnitDimensionId);

        foreach (var unit in _visibleUnits)
        {
            unit.UnitDimensionId =
                dimension.UnitDimensionId;

            _allUnits.Add(unit);
        }
    }

    private void NormalizeValues()
    {
        foreach (var dimension in _dimensions)
        {
            dimension.Code =
                dimension.Code
                    .Trim()
                    .ToUpperInvariant();

            dimension.Name =
                dimension.Name.Trim();

            dimension.BaseUnitCode =
                dimension.BaseUnitCode
                    .Trim()
                    .ToUpperInvariant();
        }

        foreach (var unit in _allUnits)
        {
            unit.Code =
                unit.Code
                    .Trim()
                    .ToUpperInvariant();

            unit.Symbol =
                unit.Symbol.Trim();

            unit.Name =
                unit.Name.Trim();
        }
    }

    private bool ValidateBeforeSave()
    {
        foreach (var dimension in _dimensions
                     .Where(x => x.IsActive))
        {
            if (string.IsNullOrWhiteSpace(
                    dimension.BaseUnitCode))
            {
                return ShowValidation(
                    string.Format(
                        T("SYS01.Units.Validation.BaseUnitRequired"),
                        dimension.Code));
            }

            var baseUnit = _allUnits.FirstOrDefault(unit =>
                unit.UnitDimensionId ==
                    dimension.UnitDimensionId &&
                unit.IsActive &&
                string.Equals(
                    unit.Code,
                    dimension.BaseUnitCode,
                    StringComparison.OrdinalIgnoreCase));

            if (baseUnit is null)
            {
                return ShowValidation(
                    string.Format(
                        T("SYS01.Units.Validation.BaseUnitUnknown"),
                        dimension.Code,
                        dimension.BaseUnitCode));
            }

            if (baseUnit.ScaleToBase != 1m ||
                baseUnit.OffsetToBase != 0m)
            {
                return ShowValidation(
                    string.Format(
                        T("SYS01.Units.Validation.BaseUnitIdentity"),
                        dimension.Code,
                        dimension.BaseUnitCode));
            }
        }

        return true;
    }

    private bool ShowValidation(
        string message)
    {
        DmsMessage.Show(
            message,
            T("SYS01.Units.ValidationTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private void WriteAuditChanges()
    {
        var currentDimensions =
            _dimensions.ToDictionary(
                x => x.UnitDimensionId);

        var currentUnits =
            _allUnits.ToDictionary(
                x => x.UnitDefinitionId);

        foreach (var dimension in _dimensions)
        {
            if (!_originalDimensions.TryGetValue(
                    dimension.UnitDimensionId,
                    out var original))
            {
                _logger.AuditCreated(
                    "SYS01",
                    "UnitDimension",
                    dimension.UnitDimensionId.ToString(),
                    _user,
                    $"Code={dimension.Code}; Name={dimension.Name}; BaseUnitCode={dimension.BaseUnitCode}; SortOrder={dimension.SortOrder}; Active={dimension.IsActive}");

                continue;
            }

            AuditField(
                "UnitDimension",
                dimension.UnitDimensionId,
                "Code",
                original.Code,
                dimension.Code);

            AuditField(
                "UnitDimension",
                dimension.UnitDimensionId,
                "Name",
                original.Name,
                dimension.Name);

            AuditField(
                "UnitDimension",
                dimension.UnitDimensionId,
                "BaseUnitCode",
                original.BaseUnitCode,
                dimension.BaseUnitCode);

            AuditField(
                "UnitDimension",
                dimension.UnitDimensionId,
                "SortOrder",
                original.SortOrder.ToString(
                    CultureInfo.InvariantCulture),
                dimension.SortOrder.ToString(
                    CultureInfo.InvariantCulture));

            AuditField(
                "UnitDimension",
                dimension.UnitDimensionId,
                "IsActive",
                original.IsActive.ToString(),
                dimension.IsActive.ToString());
        }

        foreach (var original in _originalDimensions.Values)
        {
            if (!currentDimensions.ContainsKey(
                    original.UnitDimensionId))
            {
                _logger.AuditDeleted(
                    "SYS01",
                    "UnitDimension",
                    original.UnitDimensionId.ToString(),
                    _user,
                    $"Code={original.Code}; Name={original.Name}");
            }
        }

        foreach (var unit in _allUnits)
        {
            if (!_originalUnits.TryGetValue(
                    unit.UnitDefinitionId,
                    out var original))
            {
                _logger.AuditCreated(
                    "SYS01",
                    "UnitDefinition",
                    unit.UnitDefinitionId.ToString(),
                    _user,
                    $"Code={unit.Code}; DimensionId={unit.UnitDimensionId}; Symbol={unit.Symbol}; Scale={unit.ScaleToBase}; Offset={unit.OffsetToBase}; Default={unit.IsDefault}; Active={unit.IsActive}");

                continue;
            }

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "UnitDimensionId",
                original.UnitDimensionId.ToString(),
                unit.UnitDimensionId.ToString());

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "Code",
                original.Code,
                unit.Code);

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "Symbol",
                original.Symbol,
                unit.Symbol);

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "Name",
                original.Name,
                unit.Name);

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "ScaleToBase",
                original.ScaleToBase.ToString(
                    CultureInfo.InvariantCulture),
                unit.ScaleToBase.ToString(
                    CultureInfo.InvariantCulture));

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "OffsetToBase",
                original.OffsetToBase.ToString(
                    CultureInfo.InvariantCulture),
                unit.OffsetToBase.ToString(
                    CultureInfo.InvariantCulture));

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "DecimalPlaces",
                original.DecimalPlaces.ToString(
                    CultureInfo.InvariantCulture),
                unit.DecimalPlaces.ToString(
                    CultureInfo.InvariantCulture));

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "IsDefault",
                original.IsDefault.ToString(),
                unit.IsDefault.ToString());

            AuditField(
                "UnitDefinition",
                unit.UnitDefinitionId,
                "IsActive",
                original.IsActive.ToString(),
                unit.IsActive.ToString());
        }

        foreach (var original in _originalUnits.Values)
        {
            if (!currentUnits.ContainsKey(
                    original.UnitDefinitionId))
            {
                _logger.AuditDeleted(
                    "SYS01",
                    "UnitDefinition",
                    original.UnitDefinitionId.ToString(),
                    _user,
                    $"Code={original.Code}; DimensionId={original.UnitDimensionId}");
            }
        }
    }

    private void AuditField(
        string entity,
        Guid entityId,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(
                oldValue,
                newValue,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger.AuditChange(
            "SYS01",
            entity,
            entityId.ToString(),
            field,
            oldValue,
            newValue,
            _user);
    }

    private static UnitDimension CloneDimension(
        UnitDimension source)
    {
        return new UnitDimension
        {
            UnitDimensionId = source.UnitDimensionId,
            Code = source.Code,
            Name = source.Name,
            BaseUnitCode = source.BaseUnitCode,
            SortOrder = source.SortOrder,
            IsActive = source.IsActive
        };
    }

    private static UnitDefinition CloneUnit(
        UnitDefinition source)
    {
        return new UnitDefinition
        {
            UnitDefinitionId = source.UnitDefinitionId,
            UnitDimensionId = source.UnitDimensionId,
            Code = source.Code,
            Symbol = source.Symbol,
            Name = source.Name,
            ScaleToBase = source.ScaleToBase,
            OffsetToBase = source.OffsetToBase,
            DecimalPlaces = source.DecimalPlaces,
            IsDefault = source.IsDefault,
            IsActive = source.IsActive
        };
    }

    private void Convert_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CmbSource.SelectedItem is not UnitChoice sourceChoice
            || CmbTarget.SelectedItem is not UnitChoice targetChoice)
        {
            return;
        }

        if (!decimal.TryParse(
                TxtValue.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var value)
            && !decimal.TryParse(
                TxtValue.Text.Replace(',', '.'),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value))
        {
            DmsMessage.Show(
                T("SYS01.Units.Validation.Number"),
                T("SYS01.Units.TestConversion"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            var target = targetChoice.Unit;

            var result =
                new UnitConversionService()
                    .Convert(
                        value,
                        sourceChoice.Unit,
                        target);

            TxtResult.Text =
                $"{Math.Round(result, target.DecimalPlaces)} {target.Symbol}";
        }
        catch (Exception ex)
        {
            DmsMessage.Show(
                ex.Message,
                T("SYS01.Units.TestConversion"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
