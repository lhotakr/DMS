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
    public string DisplayText => string.IsNullOrWhiteSpace(Unit.Name)
        ? Unit.Symbol
        : $"{Unit.Symbol} — {Unit.Name}";
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
        ListDimensions.ItemsSource = _dimensions.OrderBy(x => x.SortOrder).ToList();

        if (ListDimensions.Items.Count > 0)
        {
            ListDimensions.SelectedIndex = 0;
        }

        TxtStatus.Text = $"{T("SYS01.MasterData.Files")}: {_service.UnitDimensionsPath}; {_service.UnitsPath}";
    }

    private void DimensionChanged(object sender, SelectionChangedEventArgs e)
    {
        _visibleUnits.Clear();

        if (ListDimensions.SelectedItem is not UnitDimension dimension)
        {
            return;
        }

        foreach (var unit in _allUnits
                     .Where(x => x.UnitDimensionId == dimension.UnitDimensionId)
                     .OrderBy(x => x.Code))
        {
            _visibleUnits.Add(unit);
        }

        RefreshConverter();
    }

    private void RefreshConverter()
    {
        var choices = _visibleUnits
            .Where(x => x.IsActive)
            .Select(x => new UnitChoice { Unit = x })
            .ToList();

        CmbSource.ItemsSource = choices;
        CmbTarget.ItemsSource = choices;

        if (choices.Count > 0)
        {
            CmbSource.SelectedIndex = 0;
            CmbTarget.SelectedIndex = Math.Min(1, choices.Count - 1);
        }
    }

    private void AddDimension_Click(object sender, RoutedEventArgs e)
    {
        var code = Microsoft.VisualBasic.Interaction.InputBox(
            T("SYS01.Units.DimensionCodePrompt"),
            T("SYS01.Units.AddDimension"),
            "NEW").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox(
            T("SYS01.Units.DimensionNamePrompt"),
            T("SYS01.Units.AddDimension"),
            code).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var dimension = new UnitDimension
        {
            Code = code,
            Name = name,
            SortOrder = (_dimensions.Count + 1) * 10
        };

        _dimensions.Add(dimension);
        ListDimensions.ItemsSource = _dimensions.OrderBy(x => x.SortOrder).ToList();
        ListDimensions.SelectedItem = dimension;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        GridUnits.CommitEdit(DataGridEditingUnit.Cell, true);
        GridUnits.CommitEdit(DataGridEditingUnit.Row, true);

        if (ListDimensions.SelectedItem is UnitDimension dimension)
        {
            _allUnits.RemoveAll(x => x.UnitDimensionId == dimension.UnitDimensionId);

            foreach (var unit in _visibleUnits)
            {
                unit.UnitDimensionId = dimension.UnitDimensionId;
                unit.Code = unit.Code.Trim().ToUpperInvariant();
                _allUnits.Add(unit);
            }
        }

        try
        {
            _service.SaveUnits(_dimensions, _allUnits);
            _logger.AdminAction(
                "SYS01",
                "SaveUnits",
                _user,
                $"Dimensions={_dimensions.Count}; Units={_allUnits.Count}");
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("SYS01.Units.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (CmbSource.SelectedItem is not UnitChoice sourceChoice
            || CmbTarget.SelectedItem is not UnitChoice targetChoice)
        {
            return;
        }

        if (!decimal.TryParse(TxtValue.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            && !decimal.TryParse(
                TxtValue.Text.Replace(',', '.'),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value))
        {
            MessageBox.Show(
                T("SYS01.Units.Validation.Number"),
                T("SYS01.Units.TestConversion"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var target = targetChoice.Unit;
            var result = new UnitConversionService().Convert(value, sourceChoice.Unit, target);
            TxtResult.Text = $"{Math.Round(result, target.DecimalPlaces)} {target.Symbol}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("SYS01.Units.TestConversion"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
