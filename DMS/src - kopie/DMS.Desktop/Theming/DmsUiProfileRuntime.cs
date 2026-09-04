using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace DMS.Desktop.Theming;

public sealed class DmsUiProfileRuntime
{
    private ResourceDictionary? _globalOverlay;
    private ResourceDictionary? _scopeOverlay;
    private FrameworkElement? _scopeHost;
    private ResourceDictionary? _previewOverlay;
    private FrameworkElement? _previewHost;

    private readonly List<AppliedProperty> _appliedProperties = new();
    private readonly List<AppliedProperty> _previewProperties = new();

    public void ApplyGlobal(DmsUiProfile? profile)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (_globalOverlay is not null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_globalOverlay);
            _globalOverlay = null;
        }

        if (profile is null || !HasLayerContent(profile.Global))
        {
            return;
        }

        _globalOverlay = BuildOverlayDictionary(profile.Global);
        Application.Current.Resources.MergedDictionaries.Add(_globalOverlay);
    }

    public IReadOnlyList<DmsUiApplyIssue> PrepareScope(
        FrameworkElement host,
        DmsUiProfile? profile,
        string moduleCode,
        string transactionCode)
    {
        ClearScope();
        _scopeHost = host;

        if (profile is null)
        {
            return Array.Empty<DmsUiApplyIssue>();
        }

        try
        {
            var overlay = new ResourceDictionary();
            var hasContent = false;

            if (profile.Modules.TryGetValue(moduleCode, out var moduleLayer))
            {
                MergeLayerIntoDictionary(overlay, moduleLayer);
                hasContent |= HasLayerContent(moduleLayer);
            }

            if (profile.Transactions.TryGetValue(transactionCode, out var transactionLayer))
            {
                MergeLayerIntoDictionary(overlay, transactionLayer);
                hasContent |= HasLayerContent(transactionLayer);
            }

            if (!hasContent)
            {
                return Array.Empty<DmsUiApplyIssue>();
            }

            _scopeOverlay = overlay;
            host.Resources.MergedDictionaries.Add(_scopeOverlay);
            return Array.Empty<DmsUiApplyIssue>();
        }
        catch (Exception ex)
        {
            return new[]
            {
                new DmsUiApplyIssue(
                    $"{moduleCode}/{transactionCode}",
                    "RESOURCE_DICTIONARY",
                    string.Empty,
                    ex.Message)
            };
        }
    }

    public IReadOnlyList<DmsUiApplyIssue> ApplyProperties(
        FrameworkElement globalRoot,
        FrameworkElement workspaceRoot,
        DmsUiProfile? profile,
        string moduleCode,
        string transactionCode)
    {
        RestoreAppliedProperties(_appliedProperties);

        if (profile is null)
        {
            return Array.Empty<DmsUiApplyIssue>();
        }

        var issues = new List<DmsUiApplyIssue>();

        issues.AddRange(ApplyPropertyRules(
            globalRoot,
            profile.Global.Properties,
            "GLOBAL",
            _appliedProperties));

        if (profile.Modules.TryGetValue(moduleCode, out var moduleLayer))
        {
            issues.AddRange(ApplyPropertyRules(
                workspaceRoot,
                moduleLayer.Properties,
                $"MODULE:{moduleCode}",
                _appliedProperties));
        }

        if (profile.Transactions.TryGetValue(transactionCode, out var transactionLayer))
        {
            issues.AddRange(ApplyPropertyRules(
                workspaceRoot,
                transactionLayer.Properties,
                $"TRANSACTION:{transactionCode}",
                _appliedProperties));
        }

        return issues;
    }

    public IReadOnlyList<DmsUiApplyIssue> ApplyPreview(
        FrameworkElement host,
        DmsUiLayer layer) =>
        ApplyPreview(host, new[] { layer });

    public IReadOnlyList<DmsUiApplyIssue> ApplyPreview(
        FrameworkElement host,
        IEnumerable<DmsUiLayer> layers)
    {
        ClearPreview();
        _previewHost = host;

        var issues = new List<DmsUiApplyIssue>();
        var layerList = layers.ToList();

        try
        {
            if (layerList.Any(HasLayerContent))
            {
                _previewOverlay = new ResourceDictionary();

                foreach (var layer in layerList)
                {
                    MergeLayerIntoDictionary(_previewOverlay, layer);
                }

                host.Resources.MergedDictionaries.Add(_previewOverlay);
            }
        }
        catch (Exception ex)
        {
            issues.Add(new DmsUiApplyIssue(
                "PREVIEW",
                "RESOURCE_DICTIONARY",
                string.Empty,
                ex.Message));
        }

        foreach (var layer in layerList)
        {
            issues.AddRange(ApplyPropertyRules(
                host,
                layer.Properties,
                "PREVIEW",
                _previewProperties));
        }

        return issues;
    }

    public void ClearScope()
    {
        if (_scopeOverlay is not null && _scopeHost is not null)
        {
            _scopeHost.Resources.MergedDictionaries.Remove(_scopeOverlay);
        }

        _scopeOverlay = null;
        _scopeHost = null;
    }

    public void ClearPreview()
    {
        RestoreAppliedProperties(_previewProperties);

        if (_previewOverlay is not null && _previewHost is not null)
        {
            _previewHost.Resources.MergedDictionaries.Remove(_previewOverlay);
        }

        _previewOverlay = null;
        _previewHost = null;
    }

    public void ClearAll()
    {
        ClearScope();
        ClearPreview();
        RestoreAppliedProperties(_appliedProperties);

        if (_globalOverlay is not null && Application.Current is not null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_globalOverlay);
        }

        _globalOverlay = null;
    }

    public static ResourceDictionary BuildOverlayDictionary(DmsUiLayer layer)
    {
        var dictionary = new ResourceDictionary();
        MergeLayerIntoDictionary(dictionary, layer);
        return dictionary;
    }

    public static IReadOnlyList<DmsUiResourceDescriptor> GetApplicationResourceInventory()
    {
        if (Application.Current is null)
        {
            return Array.Empty<DmsUiResourceDescriptor>();
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        CollectResources(Application.Current.Resources, values);

        return values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new DmsUiResourceDescriptor(
                pair.Key,
                pair.Value?.GetType().Name ?? "null",
                ConvertResourceToString(pair.Value)))
            .ToList();
    }

    public static string ConvertResourceToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            SolidColorBrush brush => brush.Color.ToString(),
            Thickness thickness => new ThicknessConverter().ConvertToInvariantString(thickness) ?? thickness.ToString(),
            CornerRadius radius => new CornerRadiusConverter().ConvertToInvariantString(radius) ?? radius.ToString(),
            GridLength length => length.ToString(),
            FontFamily family => family.Source,
            FontWeight weight => weight.ToString(),
            double number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            bool flag => flag.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static bool TryValidateResourceValue(
        string key,
        string value,
        out string detail)
    {
        try
        {
            _ = ConvertResourceValue(key, value);
            detail = "OK";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static void MergeLayerIntoDictionary(
        ResourceDictionary dictionary,
        DmsUiLayer layer)
    {
        foreach (var pair in layer.Resources)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            dictionary[pair.Key] = ConvertResourceValue(pair.Key, pair.Value);
        }

        if (!string.IsNullOrWhiteSpace(layer.AdvancedXaml))
        {
            var advanced = DmsUiXamlValidator.Parse(layer.AdvancedXaml);

            foreach (var key in advanced.Keys)
            {
                dictionary[key] = advanced[key];
            }

            foreach (var merged in advanced.MergedDictionaries)
            {
                dictionary.MergedDictionaries.Add(merged);
            }
        }
    }

    private static object ConvertResourceValue(string key, string rawValue)
    {
        var value = rawValue.Trim();
        var current = Application.Current?.TryFindResource(key);
        var targetType = current?.GetType();

        if (targetType == typeof(SolidColorBrush) ||
            (targetType is null && value.StartsWith("#", StringComparison.Ordinal)))
        {
            return new BrushConverter().ConvertFromInvariantString(value)
                   ?? throw new InvalidOperationException($"'{value}' is not a valid brush.");
        }

        if (targetType == typeof(Thickness))
        {
            return new ThicknessConverter().ConvertFromInvariantString(value)
                   ?? throw new InvalidOperationException($"'{value}' is not a valid Thickness.");
        }

        if (targetType == typeof(CornerRadius))
        {
            return new CornerRadiusConverter().ConvertFromInvariantString(value)
                   ?? throw new InvalidOperationException($"'{value}' is not a valid CornerRadius.");
        }

        if (targetType == typeof(GridLength))
        {
            return new GridLengthConverter().ConvertFromInvariantString(value)
                   ?? throw new InvalidOperationException($"'{value}' is not a valid GridLength.");
        }

        if (targetType == typeof(FontFamily))
        {
            return new FontFamily(value);
        }

        if (targetType == typeof(FontWeight))
        {
            return new FontWeightConverter().ConvertFromInvariantString(value)
                   ?? throw new InvalidOperationException($"'{value}' is not a valid FontWeight.");
        }

        if (targetType == typeof(double))
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool))
        {
            return bool.Parse(value);
        }

        if (current is Style)
        {
            throw new InvalidOperationException(
                $"Resource '{key}' is a Style. Override it in Advanced XAML.");
        }

        if (current is ControlTemplate || current is DataTemplate)
        {
            throw new InvalidOperationException(
                $"Resource '{key}' is a template. Override it in Advanced XAML.");
        }

        if (current is not null)
        {
            var converter = TypeDescriptor.GetConverter(current.GetType());

            if (converter.CanConvertFrom(typeof(string)))
            {
                var converted = converter.ConvertFromInvariantString(value);

                if (converted is not null)
                {
                    return converted;
                }
            }
        }

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var numeric))
        {
            return numeric;
        }

        return value;
    }

    private static bool HasLayerContent(DmsUiLayer layer) =>
        layer.Resources.Count > 0 ||
        !string.IsNullOrWhiteSpace(layer.AdvancedXaml);

    private static IReadOnlyList<DmsUiApplyIssue> ApplyPropertyRules(
        FrameworkElement root,
        IEnumerable<DmsUiPropertyOverride> rules,
        string scope,
        ICollection<AppliedProperty> applied)
    {
        var issues = new List<DmsUiApplyIssue>();
        var elements = EnumerateElements(root).ToList();

        foreach (var rule in rules.Where(x => x.IsActive))
        {
            if (string.IsNullOrWhiteSpace(rule.Selector) ||
                string.IsNullOrWhiteSpace(rule.Property))
            {
                issues.Add(new DmsUiApplyIssue(
                    scope,
                    rule.Selector,
                    rule.Property,
                    "Selector and property are required."));
                continue;
            }

            var matches = elements.Where(element => Matches(element, rule)).ToList();

            if (matches.Count == 0)
            {
                issues.Add(new DmsUiApplyIssue(
                    scope,
                    rule.Selector,
                    rule.Property,
                    "No matching element in the current visual tree."));
                continue;
            }

            foreach (var element in matches)
            {
                try
                {
                    ApplyProperty(element, rule, applied);
                }
                catch (Exception ex)
                {
                    issues.Add(new DmsUiApplyIssue(
                        scope,
                        rule.Selector,
                        rule.Property,
                        $"{element.GetType().Name}: {ex.Message}"));
                }
            }
        }

        return issues;
    }

    private static void ApplyProperty(
        FrameworkElement element,
        DmsUiPropertyOverride rule,
        ICollection<AppliedProperty> applied)
    {
        var descriptor = TypeDescriptor.GetProperties(element)[rule.Property];

        if (descriptor is null || descriptor.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"Property '{rule.Property}' is not writable on {element.GetType().Name}.");
        }

        var original = descriptor.GetValue(element);
        object? converted;

        if (descriptor.PropertyType == typeof(double) &&
            string.Equals(rule.Value, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            converted = double.NaN;
        }
        else
        {
            var converter = descriptor.Converter;

            if (!converter.CanConvertFrom(typeof(string)))
            {
                converter = TypeDescriptor.GetConverter(descriptor.PropertyType);
            }

            if (!converter.CanConvertFrom(typeof(string)))
            {
                throw new InvalidOperationException(
                    $"Property '{rule.Property}' does not support text conversion.");
            }

            converted = converter.ConvertFromInvariantString(rule.Value);
        }

        applied.Add(new AppliedProperty(
            new WeakReference<FrameworkElement>(element),
            descriptor,
            original));

        descriptor.SetValue(element, converted);
    }

    private static bool Matches(
        FrameworkElement element,
        DmsUiPropertyOverride rule)
    {
        if (string.Equals(rule.SelectorKind, "NAME", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                element.Name,
                rule.Selector,
                StringComparison.OrdinalIgnoreCase);
        }

        var type = element.GetType();

        while (type is not null && type != typeof(object))
        {
            if (string.Equals(type.Name, rule.Selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type.FullName, rule.Selector, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static IEnumerable<FrameworkElement> EnumerateElements(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            yield return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            foreach (var descendant in EnumerateElements(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RestoreAppliedProperties(ICollection<AppliedProperty> applied)
    {
        foreach (var item in applied.Reverse())
        {
            try
            {
                if (item.Element.TryGetTarget(out var element))
                {
                    item.Property.SetValue(element, item.OriginalValue);
                }
            }
            catch
            {
            }
        }

        applied.Clear();
    }

    private static void CollectResources(
        ResourceDictionary dictionary,
        IDictionary<string, object?> result)
    {
        foreach (var key in dictionary.Keys)
        {
            if (key is string textKey)
            {
                result[textKey] = dictionary[key];
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            CollectResources(merged, result);
        }
    }

    private sealed record AppliedProperty(
        WeakReference<FrameworkElement> Element,
        PropertyDescriptor Property,
        object? OriginalValue);
}

public static class DmsUiXamlValidator
{
    private static readonly HashSet<string> ForbiddenAttributeNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Class",
            "Code",
            "Source",
            "Click",
            "Loaded",
            "Unloaded",
            "SelectionChanged",
            "TextChanged",
            "Checked",
            "Unchecked",
            "KeyDown",
            "KeyUp",
            "MouseDown",
            "MouseUp",
            "MouseMove",
            "MouseLeftButtonDown",
            "MouseLeftButtonUp",
            "PreviewMouseDown",
            "PreviewMouseUp",
            "PreviewKeyDown",
            "PreviewKeyUp"
        };

    public static ResourceDictionary Parse(string xaml)
    {
        ValidateText(xaml);

        var parsed = XamlReader.Parse(xaml);

        return parsed as ResourceDictionary
               ?? throw new InvalidOperationException(
                   "Advanced XAML must have ResourceDictionary as its root.");
    }

    public static IReadOnlyList<string> Validate(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return Array.Empty<string>();
        }

        try
        {
            _ = Parse(xaml);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            return new[] { ex.Message };
        }
    }

    private static void ValidateText(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Invalid XML/XAML: {ex.Message}",
                ex);
        }

        var root = document.Root
                   ?? throw new InvalidOperationException(
                       "Advanced XAML has no root element.");

        if (!string.Equals(root.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Advanced XAML root must be ResourceDictionary.");
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration &&
                    attribute.Value.Contains("clr-namespace:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "clr-namespace declarations are not allowed in distributed UI overrides.");
                }

                if (ForbiddenAttributeNames.Contains(attribute.Name.LocalName))
                {
                    throw new InvalidOperationException(
                        $"Attribute '{attribute.Name.LocalName}' is not allowed in Advanced XAML.");
                }
            }
        }

        var forbiddenElements = new HashSet<string>(
            new[] { "ObjectDataProvider", "EventSetter" },
            StringComparer.OrdinalIgnoreCase);

        var forbidden = root.Descendants()
            .FirstOrDefault(element => forbiddenElements.Contains(element.Name.LocalName));

        if (forbidden is not null)
        {
            throw new InvalidOperationException(
                $"Element '{forbidden.Name.LocalName}' is not allowed in Advanced XAML.");
        }
    }
}

public static class DmsUiProfileValidator
{
    public static IReadOnlyList<DmsUiValidationIssue> Validate(DmsUiProfile profile)
    {
        var issues = new List<DmsUiValidationIssue>();

        if (string.IsNullOrWhiteSpace(profile.Code))
        {
            issues.Add(new DmsUiValidationIssue(
                "ERROR",
                "PROFILE",
                "CODE",
                "Profile code is empty."));
        }

        ValidateLayer(issues, "GLOBAL", profile.Global);

        foreach (var pair in profile.Modules)
        {
            ValidateLayer(issues, $"MODULE:{pair.Key}", pair.Value);
        }

        foreach (var pair in profile.Transactions)
        {
            ValidateLayer(issues, $"TRANSACTION:{pair.Key}", pair.Value);
        }

        return issues;
    }

    private static void ValidateLayer(
        ICollection<DmsUiValidationIssue> issues,
        string scope,
        DmsUiLayer layer)
    {
        foreach (var pair in layer.Resources)
        {
            if (!DmsUiProfileRuntime.TryValidateResourceValue(
                    pair.Key,
                    pair.Value,
                    out var detail))
            {
                issues.Add(new DmsUiValidationIssue(
                    "ERROR",
                    scope,
                    $"RESOURCE:{pair.Key}",
                    detail));
            }
        }

        foreach (var detail in DmsUiXamlValidator.Validate(layer.AdvancedXaml))
        {
            issues.Add(new DmsUiValidationIssue(
                "ERROR",
                scope,
                "ADVANCED_XAML",
                detail));
        }

        foreach (var rule in layer.Properties)
        {
            if (!rule.IsActive)
            {
                continue;
            }

            if (!string.Equals(rule.SelectorKind, "TYPE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rule.SelectorKind, "NAME", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DmsUiValidationIssue(
                    "ERROR",
                    scope,
                    $"PROPERTY:{rule.Id}",
                    "SelectorKind must be TYPE or NAME."));
            }

            if (string.IsNullOrWhiteSpace(rule.Selector) ||
                string.IsNullOrWhiteSpace(rule.Property))
            {
                issues.Add(new DmsUiValidationIssue(
                    "ERROR",
                    scope,
                    $"PROPERTY:{rule.Id}",
                    "Active property override requires Selector and Property."));
            }
        }
    }
}
