using DMS.Core.Recipes;
using DMS.Core.Sap;
using Xunit;
using Assert = Xunit.Assert;

namespace DMS.Core.Tests;

public sealed class RecipeImportTests
{
    [Fact]
    public void SprayLayer_NormalizesTwentyKiloBatchToOneKg()
    {
        var layer = new RecipeLayer
        {
            Components = new List<RecipeComponent>
            {
                new() { SourceText = "Klar AQUA 410-90106-8", SourceGrams = 20000m },
                new() { SourceText = "Gelb WFK 57", SourceGrams = 30m },
                new() { SourceText = "Rot WFK 90", SourceGrams = 30m }
            }
        };

        new RecipeNormalizationService().NormalizeLayer(layer);

        Assert.Equal(20060m, layer.SourceTotalGrams);
        Assert.Equal(997.008973m, decimal.Round(layer.Components[0].BomGrams, 6));
        Assert.Equal(1.495513m, decimal.Round(layer.Components[1].BomGrams, 6));
        Assert.Equal(1.495513m, decimal.Round(layer.Components[2].BomGrams, 6));
        Assert.Equal(1000m, decimal.Round(layer.FinalTotalGrams, 6));
    }

    [Fact]
    public void ScreenPrint_AddsUvglHardenerAboveOneKgBase()
    {
        var layer = new RecipeLayer
        {
            Components = new List<RecipeComponent>
            {
                new() { SourceText = "UVGL188M", SourceGrams = 90m },
                new() { SourceText = "UVGL9170M", SourceGrams = 10m }
            }
        };

        var service = new RecipeNormalizationService();
        var warnings = new List<string>();
        service.NormalizeLayer(layer);
        var rule = service.ApplyScreenPrintHardener(layer, new RecipeImportSettings(), warnings);

        Assert.NotNull(rule);
        Assert.Empty(warnings);
        Assert.Equal(900m, layer.Components[0].BomGrams);
        Assert.Equal(100m, layer.Components[1].BomGrams);
        Assert.Equal(50m, layer.Components.Single(x => x.IsHardener).BomGrams);
        Assert.Equal(1050m, layer.FinalTotalGrams);
    }

    [Fact]
    public void ScreenPrint_UsesExistingHardenerInsteadOfAddingDuplicate()
    {
        var layer = new RecipeLayer
        {
            Components = new List<RecipeComponent>
            {
                new() { SourceText = "UVGL188M", SourceGrams = 90m },
                new() { SourceText = "UVGL9170M", SourceGrams = 10m },
                new() { SourceText = "UV-HV 8", SourceGrams = 5m }
            }
        };

        var service = new RecipeNormalizationService();
        var warnings = new List<string>();

        service.NormalizeLayer(layer);
        var rule = service.ApplyScreenPrintHardener(
            layer,
            new RecipeImportSettings(),
            warnings);

        Assert.NotNull(rule);
        Assert.Empty(warnings);
        Assert.Equal(3, layer.Components.Count);
        Assert.Equal(900m, layer.Components.Single(x => x.SourceText == "UVGL188M").BomGrams);
        Assert.Equal(100m, layer.Components.Single(x => x.SourceText == "UVGL9170M").BomGrams);

        var hardener = layer.Components.Single(x => x.IsHardener);
        Assert.Equal("UV-HV 8", hardener.SourceText);
        Assert.Equal(50m, hardener.BomGrams);
        Assert.Equal("SOURCE OK", hardener.HardenerStatus);
        Assert.Equal(1050m, layer.FinalTotalGrams);
    }

    [Fact]
    public void ScreenPrint_WarnsWhenExistingHardenerRatioDiffersFromRule()
    {
        var layer = new RecipeLayer
        {
            Components = new List<RecipeComponent>
            {
                new() { SourceText = "UVGL188M", SourceGrams = 90m },
                new() { SourceText = "UVGL9170M", SourceGrams = 10m },
                new() { SourceText = "UV HV 8", SourceGrams = 4m }
            }
        };

        var service = new RecipeNormalizationService();
        var warnings = new List<string>();

        service.ApplyScreenPrintHardener(
            layer,
            new RecipeImportSettings
            {
                HardenerTolerancePercent = 0.25m
            },
            warnings);

        var hardener = layer.Components.Single(x => x.IsHardener);
        Assert.Equal(40m, hardener.BomGrams);
        Assert.Equal("SOURCE MISMATCH", hardener.HardenerStatus);
        Assert.Single(warnings);
        Assert.Equal(1040m, layer.FinalTotalGrams);
    }

    [Fact]
    public void ScreenPrint_CanLeaveMissingHardenerAsWarning()
    {
        var layer = new RecipeLayer
        {
            Components = new List<RecipeComponent>
            {
                new() { SourceText = "UVGL188M", SourceGrams = 90m },
                new() { SourceText = "UVGL9170M", SourceGrams = 10m }
            }
        };

        var service = new RecipeNormalizationService();
        var warnings = new List<string>();

        service.ApplyScreenPrintHardener(
            layer,
            new RecipeImportSettings
            {
                AddMissingHardener = false
            },
            warnings);

        Assert.DoesNotContain(layer.Components, x => x.IsHardener);
        Assert.Single(warnings);
        Assert.Equal(1000m, layer.FinalTotalGrams);
    }

    [Fact]
    public void ScreenCode_SplitsFamilyAndSuffix()
    {
        var split = RecipeTextNormalizer.TrySplitScreenPrintCode("UVGL9170M");
        Assert.NotNull(split);
        Assert.Equal("UVGL", split.Value.Family);
        Assert.Equal("9170M", split.Value.Key);
    }

    [Fact]
    public void Matcher_IgnoresTokenOrderForSprayComponents()
    {
        var matcher = new RecipeComponentMatcher();
        var settings = new RecipeImportSettings();
        var component = new RecipeComponent { SourceText = "Gelb WFK 57" };
        var materials = new List<SapMaterial>
        {
            new() { MaterialNumber = "1100000001", Description = "WFK Gelb 57" }
        };

        var candidates = matcher.GetCandidates(
            component,
            RecipeImportKind.SprayCoating,
            materials,
            settings);

        Assert.Single(candidates);
        Assert.Equal(1d, candidates[0].Score);
    }

    [Fact]
    public void Matcher_MatchesScreenFamilyAndSuffixInsideSapText()
    {
        var matcher = new RecipeComponentMatcher();
        var settings = new RecipeImportSettings();
        var component = new RecipeComponent { SourceText = "UVGL188M" };
        var materials = new List<SapMaterial>
        {
            new() { MaterialNumber = "1100000001", Description = "UVGL 1003183188M" },
            new() { MaterialNumber = "1100000002", Description = "UVGL 1003329170M" }
        };

        var candidates = matcher.GetCandidates(
            component,
            RecipeImportKind.ScreenPrinting,
            materials,
            settings);

        Assert.Equal("1100000001", candidates[0].MaterialNumber);
        Assert.Equal(1d, candidates[0].Score);
    }
}
