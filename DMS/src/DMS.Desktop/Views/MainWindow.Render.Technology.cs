using DMS.Core.Sap;
using DMS.Core.Sap.Validation;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderTechnicalArticleSummary(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var panel = CreateWorkspaceStack();

        try
        {
            var normalizedArticleNumber = NormalizeTechMaterialNumber(articleNumber);

            panel.Children.Add(CreateTitle($"TEC03 - Technologický souhrn artiklu {normalizedArticleNumber}"));

            var basePath = @"Z:\SAP\DMS-db\DEV";

            var storagePaths = new SapStoragePaths(basePath);
            storagePaths.EnsureDirectories();

            var materialsPath = storagePaths.SapMaterialsFilePath;
            var bomsPath = Path.Combine(basePath, "Data", "sap-boms.json");
            var routingsPath = Path.Combine(basePath, "Data", "sap-routings.json");
            var workCentersPath = Path.Combine(basePath, "Data", "sap-work-centers.json");

            var materials = new JsonSapMaterialRepository(materialsPath).LoadAll();
            var boms = new JsonSapBomRepository(bomsPath).LoadAll();
            var routings = new JsonSapRoutingRepository(routingsPath).LoadAll();
            var workCenters = new JsonSapWorkCenterRepository(workCentersPath).LoadAll();

            var validationRulesPath = Path.Combine(basePath, "Config", "sap-validation-rules.json");

            var validationRules = new JsonSapValidationRuleRepository(validationRulesPath)
                .Load();

            var service = new SapTechnicalArticleSummaryService(
                materials,
                boms,
                routings,
                workCenters,
                validationRules);

            var summary = service.Build(normalizedArticleNumber);

            panel.Children.Add(CreateTechStatusCard(summary));
            panel.Children.Add(CreateTechMaterialCard(summary));

            panel.Children.Add(CreateTechSectionTitle("Technologické varianty"));

            var variantsByPlant = summary.Variants
                .OrderBy(item => item.Plant)
                .ThenBy(item => item.Alternative)
                .GroupBy(item => item.Plant)
                .ToList();

            if (variantsByPlant.Count == 0)
            {
                panel.Children.Add(CreateTechMutedText("Nebyly nalezeny žádné technologické varianty."));
            }
            else
            {
                foreach (var plantGroup in variantsByPlant)
                {
                    panel.Children.Add(CreateTechPlantPanel(service, plantGroup.Key, plantGroup.ToList()));
                }
            }

            panel.Children.Add(CreateTechMessagesPanel(summary));
        }
        catch (Exception ex)
        {
            panel.Children.Add(CreateTitle("TEC03 - Technologický souhrn"));

            panel.Children.Add(CreateTechCard(
                "Chyba při načítání technologického souhrnu",
                ex.Message));
        }
        if (panel.Parent is null)
        {
            WorkspacePanel.Children.Add(panel);
        }

        ResetWorkspaceScroll();
    }

    private UIElement CreateTechStatusCard(SapTechnicalArticleSummary summary)
    {
        var statusText = summary.StatusText;

        var detail =
            $"Artikl: {summary.ArticleNumber}\n" +
            $"Kritické chyby: {summary.CriticalErrors.Count}\n" +
            $"Upozornění: {summary.Warnings.Count}";

        return CreateTechCard($"Stav: {statusText}", detail);
    }

    private UIElement CreateTechMaterialCard(SapTechnicalArticleSummary summary)
    {
        if (summary.Material is null)
        {
            return CreateTechCard(
                "Materiál",
                "Materiál nebyl nalezen v importované SAP cache.");
        }

        var material = summary.Material;

        var text =
            $"SAP číslo: {material.MaterialNumber}\n" +
            $"Popis: {material.Description}\n" +
            $"Staré číslo: {material.OldMaterialNumber}\n" +
            $"Status: {material.MaterialStatus}\n" +
            $"Typ: {material.MaterialKind}";

        return CreateTechCard("Materiál", text);
    }

    private UIElement CreateTechBomHeaderPanel(IReadOnlyList<SapBom> boms, string plant)
    {
        if (boms.Count == 0)
        {
            return CreateTechCard(
                $"Hlavička kusovníku {plant}",
                "Kusovník nebyl nalezen.");
        }

        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        foreach (var bom in boms)
        {
            var baseQuantityText = bom.BaseQuantity?.ToString("0.##") ?? "";

            var info =
                $"Kusovník: {bom.BomNumber}\n" +
                $"Alternativa: {NormalizeAlternativeDisplay(bom.Alternative)}\n" +
                $"Použití: {bom.BomUsage}\n" +
                $"Základní množství: {baseQuantityText} {bom.BaseUnit}\n" +
                $"Počet položek: {bom.Items.Count}";

            if (plant == "2000" && bom.BaseQuantity != 10000m)
            {
                info += "\nUpozornění: základní množství by mělo být 10000.";
            }

            panel.Children.Add(CreateTechCard($"Hlavička kusovníku {plant}", info));
        }

        return panel;
    }

    private UIElement CreateTechBomGrid(IReadOnlyList<SapTechnicalBomItemRow> rows, string plant)
    {

        if (rows.Count == 0)
        {
            return CreateTechMutedText("Nenalezeny žádné položky kusovníku.");
        }

        var grid = CreateTechBaseGrid();

        grid.Columns.Add(CreateTextColumn("Položka", "Position", 80));
        grid.Columns.Add(CreateTextColumn("Typ", "ItemCategory", 60));
        grid.Columns.Add(CreateTextColumn("Označení komponenty", "ComponentDescription", 280));
        grid.Columns.Add(CreateTextColumn("Číslo komponenty", "ComponentNumber", 130));
        grid.Columns.Add(CreateTextColumn("Množství", "Quantity", 100));
        grid.Columns.Add(CreateTextColumn("Pevné množství", "IsFixedQuantity", 120));

        if (plant == "9200")
        {
            grid.Columns.Add(CreateTextColumn("Zmetkovitost %", "ScrapPercent", 120));
        }

        grid.Columns.Add(CreateTextColumn("Jednotka", "Unit", 80));

        grid.ItemsSource = rows;

        return grid;
    }

    private UIElement CreateTechRoutingGrid(IReadOnlyList<SapTechnicalRoutingOperationRow> rows, string plant)
    {
        if (rows.Count == 0)
        {
            return CreateTechMutedText("Nenalezeny žádné operace pracovního postupu.");
        }

        var grid = CreateTechBaseGrid();

        grid.Columns.Add(CreateTextColumn("Operace", "OperationNumber", 80));
        grid.Columns.Add(CreateTextColumn("Pracoviště", "WorkCenterDisplay", 260));
        grid.Columns.Add(CreateTextColumn("Popis", "Description", 260));

        if (plant == "2000")
        {
            grid.Columns.Add(CreateTextColumn("Zmetkovitost %", "ScrapPercent", 120));
            grid.Columns.Add(CreateTextColumn("Plánovaná přestavba", "SetupTime", 150));
            grid.Columns.Add(CreateTextColumn("Takt / směna", "ShiftTakt", 120));
            grid.Columns.Add(CreateTextColumn("Počet personálu", "PersonnelCount", 130));
        }
        else
        {
            grid.Columns.Add(CreateTextColumn("Takt / směna", "ShiftTakt", 120));
            grid.Columns.Add(CreateTextColumn("Inforecord", "InfoRecord", 140));
        }

        grid.ItemsSource = rows;

        return grid;
    }

    private UIElement CreateTechMessagesPanel(SapTechnicalArticleSummary summary)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0)
        };

        panel.Children.Add(CreateTechSectionTitle("Kontroly / upozornění"));

        if (summary.CriticalErrors.Count == 0 && summary.Warnings.Count == 0)
        {
            panel.Children.Add(CreateTechCard(
                "Výsledek kontrol",
                "Nebyla nalezena žádná kritická chyba ani upozornění. Paráda."));
            return panel;
        }

        if (summary.CriticalErrors.Count > 0)
        {
            panel.Children.Add(CreateTechCard(
                "Kritické chyby",
                string.Join("\n", summary.CriticalErrors)));
        }

        if (summary.Warnings.Count > 0)
        {
            panel.Children.Add(CreateTechCard(
                "Upozornění",
                string.Join("\n", summary.Warnings)));
        }

        return panel;
    }

    private DataGrid CreateTechBaseGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 0, 0, 16),
            MinHeight = 120,
            MaxHeight = 360,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };

        grid.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "DmsBorderBrush");

        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(grid, true);

        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(
        string header,
        string binding,
        double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(binding),
            Width = new DataGridLength(width)
        };
    }

    private UIElement CreateTechSectionTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        return block;
    }

    private UIElement CreateTechMutedText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        return block;
    }

    private UIElement CreateTechCard(string title, string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var bodyBlock = new TextBox
        {
            Text = body,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        bodyBlock.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        bodyBlock.SetResourceReference(TextBox.ForegroundProperty, "DmsMutedForegroundBrush");
        bodyBlock.SetResourceReference(TextBox.CaretBrushProperty, "DmsForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBlock);

        border.Child = panel;

        return border;
    }

    private static string NormalizeTechMaterialNumber(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
    }

    private UIElement CreateTechVariantExpander(
     SapTechnicalArticleSummaryService service,
     SapTechnicalVariantSummary variant)
    {
        var alternativeText = string.IsNullOrWhiteSpace(variant.Alternative)
            ? "bez alternativy"
            : variant.Alternative;

        var bomCount = variant.Boms.Count;
        var routingCount = variant.Routings.Count;

        var expander = new Expander
        {
            Header = $"Alternativa {alternativeText}    |    kusovníků: {bomCount}    |    postupů: {routingCount}",
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 12)
        };

        expander.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        expander.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");

        var outer = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 0),
            BorderThickness = new Thickness(1)
        };

        outer.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        if (variant.Boms.Count == 0)
        {
            panel.Children.Add(CreateTechMutedText("Pro tuto alternativu nebyl nalezen kusovník."));
        }
        else
        {
            foreach (var bom in variant.Boms
                         .OrderBy(item => item.BomNumber)
                         .ThenBy(item => NormalizeVariantSortKey(item.Alternative)))
            {
                panel.Children.Add(CreateTechSubTitle(
                    $"Kusovník {variant.Plant} / {bom.BomNumber} / alternativa {NormalizeAlternativeDisplay(bom.Alternative)}"));

                panel.Children.Add(CreateTechBomHeaderPanel(
                    new List<SapBom> { bom },
                    variant.Plant));

                var bomRows = service.BuildBomRows(
                    new List<SapBom> { bom },
                    variant.Plant);

                panel.Children.Add(CreateTechBomGrid(bomRows, variant.Plant));
            }
        }

        if (variant.Routings.Count == 0)
        {
            panel.Children.Add(CreateTechMutedText("Pro tuto alternativu nebyl nalezen pracovní postup."));
        }
        else
        {
            foreach (var routing in variant.Routings
                         .OrderBy(item => item.GroupNumber)
                         .ThenBy(item => NormalizeVariantSortKey(item.Alternative)))
            {
                panel.Children.Add(CreateTechSubTitle(
                    $"Pracovní postup {variant.Plant} / skupina {routing.GroupNumber} / alternativa {NormalizeAlternativeDisplay(routing.Alternative)}"));

                var routingRows = service.BuildRoutingRows(
                    new List<SapRouting> { routing },
                    variant.Plant);

                panel.Children.Add(CreateTechRoutingGrid(routingRows, variant.Plant));
            }
        }

        outer.Child = panel;
        expander.Content = outer;

        return expander;
    }


    private UIElement CreateTechSubTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 8)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        return block;
    }

    private UIElement CreateTechPlantPanel(
    SapTechnicalArticleSummaryService service,
    string plant,
    IReadOnlyList<SapTechnicalVariantSummary> variants)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 18),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var panel = new StackPanel();

        panel.Children.Add(CreateTechSectionTitle($"Závod {plant}"));

        foreach (var variant in variants
                     .OrderBy(item => NormalizeVariantSortKey(item.Alternative)))
        {
            panel.Children.Add(CreateTechVariantExpander(service, variant));
        }

        border.Child = panel;
        return border;
    }

    private static string NormalizeAlternativeDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "bez alternativy";
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("00")
            : text;
    }

    private static string NormalizeVariantSortKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "9999";
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("0000")
            : text;
    }
}