using DMS.Core.Checklists;
using System.IO;

namespace DMS.Desktop.Services.Checklists;

public sealed class ChecklistDefinitionRepository
{
    private readonly string _definitionsRoot;

    public ChecklistDefinitionRepository(string definitionsRoot)
    {
        _definitionsRoot = definitionsRoot;
        Directory.CreateDirectory(_definitionsRoot);
        EnsureAndUpgradeVzrMet();
    }

    public IReadOnlyList<ChecklistDefinition> LoadAll()
    {
        var definitions = Directory
            .EnumerateFiles(_definitionsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(AtomicChecklistJsonStore.Load<ChecklistDefinition>)
            .Where(x => x is not null)
            .Cast<ChecklistDefinition>()
            .ToList();

        foreach (var definition in definitions)
            NormalizeDefinitionGraph(definition);

        return definitions
            .OrderBy(x => x.Code)
            .ToList();
    }

    public ChecklistDefinition? Find(string code) => LoadAll().FirstOrDefault(x =>
        string.Equals(x.Code, Normalize(code), StringComparison.OrdinalIgnoreCase));

    public void Save(ChecklistDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        NormalizeDefinitionGraph(definition);
        Validate(definition);
        definition.Code = Normalize(definition.Code);
        AtomicChecklistJsonStore.Save(Path.Combine(_definitionsRoot, definition.Code + ".json"), definition);
    }

    private void EnsureAndUpgradeVzrMet()
    {
        var path = Path.Combine(_definitionsRoot, "VZRMET.json");
        var desired = ChecklistSeedFactory.CreateVzrMet();

        if (!File.Exists(path))
        {
            Save(desired);
            return;
        }

        var existing = AtomicChecklistJsonStore.Load<ChecklistDefinition>(path);
        if (existing is null)
        {
            Save(desired);
            return;
        }

        NormalizeDefinitionGraph(existing);
        NormalizeDefinitionGraph(desired);

        var changed = MergeMissing(existing, desired);
        if (changed)
        {
            existing.Version = Math.Max(existing.Version, 2);
            Save(existing);
        }
    }

    private static bool MergeMissing(ChecklistDefinition target, ChecklistDefinition source)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(target.NumberPrefix))
        {
            target.NumberPrefix = source.NumberPrefix;
            changed = true;
        }

        foreach (var sourceSection in source.Sections.OrderBy(x => x.SortOrder))
        {
            var targetSection = target.Sections.FirstOrDefault(x =>
                string.Equals(x.Code, sourceSection.Code, StringComparison.OrdinalIgnoreCase));

            if (targetSection is null)
            {
                target.Sections.Add(sourceSection);
                changed = true;
                continue;
            }

            foreach (var sourceField in sourceSection.Fields.OrderBy(x => x.SortOrder))
            {
                var targetField = targetSection.Fields.FirstOrDefault(x =>
                    string.Equals(x.Code, sourceField.Code, StringComparison.OrdinalIgnoreCase));

                if (targetField is null)
                {
                    targetSection.Fields.Add(sourceField);
                    changed = true;
                    continue;
                }

                // Upgrade legacy inline catalogs to CHLSET references without replacing user labels.
                if (targetField.FieldType == ChecklistFieldType.CatalogValue &&
                    string.IsNullOrWhiteSpace(targetField.CatalogCode) &&
                    !string.IsNullOrWhiteSpace(sourceField.CatalogCode))
                {
                    targetField.CatalogCode = sourceField.CatalogCode;
                    targetField.AllowMultipleValues = sourceField.AllowMultipleValues;
                    changed = true;
                }

                if (targetField.FieldType == ChecklistFieldType.RepeatingGroup &&
                    targetField.ChildFields.Count == 0 &&
                    sourceField.ChildFields.Count > 0)
                {
                    targetField.ChildFields = sourceField.ChildFields;
                    changed = true;
                }
            }
        }

        target.Sections = target.Sections.OrderBy(x => x.SortOrder).ToList();
        foreach (var section in target.Sections)
            section.Fields = section.Fields.OrderBy(x => x.SortOrder).ToList();

        return changed;
    }


    private static void NormalizeDefinitionGraph(ChecklistDefinition definition)
    {
        definition.Code ??= string.Empty;
        definition.Name ??= string.Empty;
        definition.Description ??= string.Empty;
        definition.NumberPrefix ??= string.Empty;
        definition.Sections ??= new List<ChecklistSectionDefinition>();

        for (var sectionIndex = definition.Sections.Count - 1; sectionIndex >= 0; sectionIndex--)
        {
            var section = definition.Sections[sectionIndex];
            if (section is null)
            {
                definition.Sections.RemoveAt(sectionIndex);
                continue;
            }

            section.Code ??= string.Empty;
            section.Title ??= string.Empty;
            section.Fields ??= new List<ChecklistFieldDefinition>();

            for (var fieldIndex = section.Fields.Count - 1; fieldIndex >= 0; fieldIndex--)
            {
                var field = section.Fields[fieldIndex];
                if (field is null)
                {
                    section.Fields.RemoveAt(fieldIndex);
                    continue;
                }

                NormalizeField(field);
            }
        }
    }

    private static void NormalizeField(ChecklistFieldDefinition field)
    {
        field.Code ??= string.Empty;
        field.Label ??= string.Empty;
        field.CatalogValues ??= new List<string>();
        field.ChildFields ??= new List<ChecklistFieldDefinition>();

        for (var childIndex = field.ChildFields.Count - 1; childIndex >= 0; childIndex--)
        {
            var child = field.ChildFields[childIndex];
            if (child is null)
            {
                field.ChildFields.RemoveAt(childIndex);
                continue;
            }

            NormalizeField(child);
        }
    }

    private static string Normalize(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static void Validate(ChecklistDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Code))
            throw new InvalidOperationException("Checklist code is required.");
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new InvalidOperationException("Checklist name is required.");

        var fieldCodes = definition.Sections.SelectMany(x => x.Fields)
            .Select(x => x.Code.Trim()).ToList();

        if (fieldCodes.Any(string.IsNullOrWhiteSpace) ||
            fieldCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fieldCodes.Count)
            throw new InvalidOperationException("Checklist field codes must be filled and unique.");
    }
}

internal static class ChecklistSeedFactory
{
    public static ChecklistDefinition CreateVzrMet()
    {
        var definition = new ChecklistDefinition
        {
            Code = "VZRMET",
            Name = "Vzorování metalizace / postřiku",
            Description = "Elektronický vzorovací checklist pro metalizaci a postřik.",
            NumberPrefix = "VzrMet",
            Version = 2,
            SubjectType = ChecklistSubjectType.SapArticle,
            SubjectMaterialKind = "GlassArticle",
            AllowMultipleInstancesPerSubject = true,
            SupportsCopy = true,
            RequiresReview = true
        };

        definition.Sections.Add(Section("BASIC", "Základní údaje", 10,
            Field("SAP_ARTICLE", "SAP číslo", ChecklistFieldType.SapMaterial, 10, required: true, readOnly: true, source: "Sap.MaterialNumber"),
            Field("BOTTLE_NUMBER", "Číslo lahve", ChecklistFieldType.Text, 20, source: "Sap.GlassInfo.MoldNumber"),
            Field("PROJECT_NAME", "Název projektu", ChecklistFieldType.Text, 30),
            Catalog("SAMPLING_TYPE", "Typ vzorování", 40, "SAMPLING_TYPE"),
            Catalog("DECORATION_TYPE", "Typ dekorace", 50, "DECORATION_TYPE"),
            Catalog("EXECUTION_TYPE", "Provedení", 60, "EXECUTION_TYPE"),
            Field("SAMPLING_SEQUENCE", "Pořadí vzorování", ChecklistFieldType.Integer, 70),
            Field("REPEAT_REASON", "Pokud se jedná o opakované vzorování, jaké jsou důvody", ChecklistFieldType.MultilineText, 80),
            Measurement("VOLUME", "Objem", 90, "VOLUME", "ML", source: "Sap.GlassInfo.VolumeMl")));

        definition.Sections.Add(Section("PREPARATION", "Příprava", 20,
            Field("PRETREATMENT_ENABLED", "Krok č. 1 – předúprava", ChecklistFieldType.Boolean, 10),
            Catalog("PRETREATMENT", "Předúprava", 20, "PRETREATMENT_TYPE", multiple: true),
            Catalog("TIP_TYPE", "Typ špiček", 30, "TIP_TYPE"),
            Field("NEW_TIPS_BURNED", "Nové špičky vypáleny", ChecklistFieldType.Boolean, 40),
            Field("TIPS_MODIFIED", "Špičky modifikovány", ChecklistFieldType.Boolean, 50)));

        definition.Sections.Add(Section("SPRAY_1", "První postřik", 30,
            Field("SPRAY_1_ENABLED", "První nástřik", ChecklistFieldType.Boolean, 10),
            RepeatingGuns("SPRAY_1_GUNS", "Pistole", 20),
            Field("SPRAY_1_NOTES", "Poznámky k nastavení kabiny a pistolí", ChecklistFieldType.MultilineText, 30)));

        definition.Sections.Add(Section("OVEN_1", "Výpal v první peci", 40,
            Field("OVEN_1_ENABLED", "Výpal v první peci", ChecklistFieldType.Boolean, 10),
            Field("OVEN_1_TEMPERATURE_MEASURED", "Provedeno měření teplot", ChecklistFieldType.Boolean, 20),
            Field("OVEN_1_DOCUMENT_AVAILABLE", "Dokument z měření k dispozici", ChecklistFieldType.Boolean, 30),
            Field("OVEN_1_PARAMETERS", "Parametry a teploty nastavení pece", ChecklistFieldType.MultilineText, 40),
            Field("OVEN_1_APPROVED_BY", "Kdo schválil vzorky po nástřiku", ChecklistFieldType.Person, 50),
            Field("OVEN_1_SPRAYED_QUANTITY", "Množství nastříkaného skla", ChecklistFieldType.Integer, 60)));

        definition.Sections.Add(Section("METALLIZATION", "Metalizace", 50,
            Field("KOLZER_ENABLED", "Kolzer", ChecklistFieldType.Boolean, 10),
            Catalog("MASK_TYPE", "Typ masky", 20, "MASK_TYPE"),
            Catalog("KOLZER_MACHINE", "Použitý Kolzer", 30, "KOLZER_MACHINE", multiple: true),
            Catalog("KOLZER_LOAD_TYPE", "Použitý typ", 40, "KOLZER_LOAD_TYPE"),
            Field("KOLZER_PARAMETERS", "Program a parametry nastavení Kolzeru", ChecklistFieldType.MultilineText, 50),
            Measurement("ALUMINUM_AMOUNT", "Množství použitého hliníku", 60, "MASS", "G"),
            Measurement("METALLIZATION_TIME", "Doba metalizace", 70, "TIME", "S"),
            Field("METALLIZED_QUANTITY", "Množství metalizovaného skla", ChecklistFieldType.Integer, 80)));

        definition.Sections.Add(Section("SPRAY_2", "Druhý postřik", 60,
            Field("OPAL_BEFORE_SPRAY_2", "Opal před druhým nástřikem", ChecklistFieldType.Boolean, 10),
            Field("SPRAY_2_ENABLED", "Druhý nástřik", ChecklistFieldType.Boolean, 20),
            RepeatingGuns("SPRAY_2_GUNS", "Pistole", 30),
            Field("SPRAY_2_NOTES", "Poznámky k nastavení kabiny a pistolí", ChecklistFieldType.MultilineText, 40)));

        definition.Sections.Add(Section("OVEN_2", "Výpal v druhé peci", 70,
            Field("OVEN_2_ENABLED", "Výpal v druhé peci", ChecklistFieldType.Boolean, 10),
            Field("OVEN_2_TEMPERATURE_MEASURED", "Provedeno měření teplot", ChecklistFieldType.Boolean, 20),
            Field("OVEN_2_DOCUMENT_AVAILABLE", "Dokument z měření k dispozici", ChecklistFieldType.Boolean, 30),
            Field("OVEN_2_PARAMETERS", "Parametry a teploty nastavení pece", ChecklistFieldType.MultilineText, 40),
            Field("OVEN_2_APPROVED_BY", "Kdo schválil vzorky po nástřiku", ChecklistFieldType.Person, 50),
            Field("OVEN_2_SPRAYED_QUANTITY", "Množství nastříkaného skla", ChecklistFieldType.Integer, 60)));

        definition.Sections.Add(Section("RESULT", "Výsledek", 80,
            Field("FINAL_STD_APPROVED_BY", "Kdo schválil finální STD vzorky", ChecklistFieldType.Person, 10),
            Field("FINAL_MIN_APPROVED_BY", "Kdo schválil finální MIN vzorky", ChecklistFieldType.Person, 20),
            Field("FINAL_MAX_APPROVED_BY", "Kdo schválil finální MAX vzorky", ChecklistFieldType.Person, 30),
            Field("RECIPE_RECORDED", "Receptura zapsaná do vzorovacího sešitu", ChecklistFieldType.Boolean, 40),
            Field("COATING_THICKNESS_MEASURED", "Tloušťka laku změřena", ChecklistFieldType.Boolean, 50),
            Measurement("COATING_THICKNESS", "Tloušťka laku", 60, "THICKNESS", "UM"),
            Field("MIN_MAX_SAMPLES_CREATED", "MIN a MAX vzorky vytvořeny", ChecklistFieldType.Boolean, 70),
            Field("NOTES", "Poznámka", ChecklistFieldType.MultilineText, 80)));

        return definition;
    }

    private static ChecklistSectionDefinition Section(string code, string title, int order, params ChecklistFieldDefinition[] fields) => new()
    {
        Code = code,
        Title = title,
        SortOrder = order,
        Fields = fields.ToList()
    };

    private static ChecklistFieldDefinition Field(string code, string label, ChecklistFieldType type, int order, bool required = false, bool readOnly = false, string? source = null) => new()
    {
        Code = code,
        Label = label,
        FieldType = type,
        SortOrder = order,
        IsRequired = required,
        IsReadOnly = readOnly,
        SourceBinding = source
    };

    private static ChecklistFieldDefinition Measurement(string code, string label, int order, string dimension, string unit, string? source = null) => new()
    {
        Code = code,
        Label = label,
        FieldType = ChecklistFieldType.Measurement,
        SortOrder = order,
        UnitDimensionCode = dimension,
        DefaultUnitCode = unit,
        SourceBinding = source
    };

    private static ChecklistFieldDefinition Catalog(string code, string label, int order, string catalogCode, bool multiple = false) => new()
    {
        Code = code,
        Label = label,
        FieldType = ChecklistFieldType.CatalogValue,
        SortOrder = order,
        CatalogCode = catalogCode,
        AllowMultipleValues = multiple
    };

    private static ChecklistFieldDefinition RepeatingGuns(string code, string label, int order) => new()
    {
        Code = code,
        Label = label,
        FieldType = ChecklistFieldType.RepeatingGroup,
        SortOrder = order,
        ChildFields = new List<ChecklistFieldDefinition>
        {
            Field("GUN_NUMBER", "Pistole", ChecklistFieldType.Integer, 10, required: true),
            Measurement("PRESSURE", "Tlak", 20, "PRESSURE", "KPA")
        }
    };
}
