using DMS.Core.Sap;

namespace DMS.Core.Quality;

public sealed class QualityArticleCreateService
{
    private readonly JsonQualityRepository _repository;
    private readonly IReadOnlyList<SapMaterial> _sapMaterials;
    private readonly SapDecorationRuleService _decorationRuleService;
    private const string NonSapPlaceholderMaterialNumber = "1000000000";

    public QualityArticleCreateService(
        JsonQualityRepository repository,
        IReadOnlyList<SapMaterial> sapMaterials,
        SapDecorationRuleService decorationRuleService)
    {
        _repository = repository;
        _sapMaterials = sapMaterials;
        _decorationRuleService = decorationRuleService;
    }

    public QualityArticleCreateModel? TryPrepareFromSap(
        string sapMaterialNumber)
    {
        var normalizedSapNumber =
            NormalizeSapNumber(sapMaterialNumber);

        if (string.IsNullOrWhiteSpace(normalizedSapNumber))
        {
            return null;
        }

        var sapMaterial = _sapMaterials.FirstOrDefault(item =>
            string.Equals(
                NormalizeSapNumber(item.MaterialNumber),
                normalizedSapNumber,
                StringComparison.OrdinalIgnoreCase));

        if (sapMaterial is null)
        {
            return null;
        }

        var decoration =
            ResolveDecorationName(sapMaterial);

        var oldMaterialNumber =
            sapMaterial.OldMaterialNumber?.Trim()
            ?? string.Empty;

        var title =
            sapMaterial.Description?.Trim()
            ?? string.Empty;

        return new QualityArticleCreateModel
        {
            SapMaterialNumber = NormalizeSapNumber(sapMaterial.MaterialNumber),
            SapTitle = title,
            OldMaterialNumber = oldMaterialNumber,

            // Výchozí návrh: staré číslo = číslo tiskové verze.
            FullPrintVersionNumber = oldMaterialNumber,

            // Výchozí návrh: název tiskové verze = SAP popis.
            PrintVersionTitle = title,

            DecorationCode = decoration
        };
    }

    public QualityArticleCreateResult Create(
        QualityArticleCreateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sapNumber =
            NormalizeSapNumber(model.SapMaterialNumber);

        var printVersionNumber =
            model.FullPrintVersionNumber.Trim();

        if (string.IsNullOrWhiteSpace(sapNumber))
        {
            return QualityArticleCreateResult.Fail(
                "Není zadané SAP číslo materiálu.");
        }

        if (string.IsNullOrWhiteSpace(printVersionNumber))
        {
            return QualityArticleCreateResult.Fail(
                "Není zadané číslo tiskové verze.");
        }

        var isNonSapPlaceholder = IsNonSapPlaceholder(sapNumber);

        var sapMaterialExists =
            isNonSapPlaceholder ||
            _sapMaterials.Any(item =>
                string.Equals(
                    NormalizeSapNumber(item.MaterialNumber),
                    sapNumber,
                    StringComparison.OrdinalIgnoreCase));

        if (!sapMaterialExists)
        {
            return QualityArticleCreateResult.Fail(
                $"SAP materiál {sapNumber} nebyl nalezen v lokální SAP cache.");
        }

        var articles =
            _repository.LoadArticles().ToList();

        var printVersions =
            _repository.LoadPrintVersions().ToList();

        var legacyArticleNumber =
    model.OldMaterialNumber.Trim();

        if (string.IsNullOrWhiteSpace(legacyArticleNumber))
        {
            return QualityArticleCreateResult.Fail(
                "SAP materiál nemá vyplněné staré číslo materiálu. " +
                "Bez něj nelze založit quality artikl.");
        }

        var sapAlreadyExists = !isNonSapPlaceholder && printVersions.Any(item => string.Equals(
             NormalizeSapNumber(item.SapMaterialNumber),
             sapNumber,
             StringComparison.OrdinalIgnoreCase));

        if (sapAlreadyExists)
        {
            return QualityArticleCreateResult.Fail(
                $"Quality data pro SAP {sapNumber} už existují. " +
                "Použij QA02 pro změnu existujících quality dat.");
        }

        var printVersionExists = printVersions.Any(item =>
            string.Equals(
                item.FullPrintVersionNumber?.Trim(),
                printVersionNumber,
                StringComparison.OrdinalIgnoreCase));

        if (printVersionExists)
        {
            return QualityArticleCreateResult.Fail(
                $"Tisková verze {printVersionNumber} už existuje. " +
                "Číslo tiskové verze musí být unikátní.");
        }

        articles.Add(new QualityArticle
        {
            LegacyArticleNumber = legacyArticleNumber,
            Title = model.SapTitle,
            ImportantInfo = model.ImportantInfo,
            Notes = model.ArticleNotes,
            ImportedAt = DateTime.Now,
            SourceFilePath = "QA01"
        });

        printVersions.Add(new QualityPrintVersion
        {
            SapMaterialNumber = sapNumber,
            FullPrintVersionNumber = printVersionNumber,
            Title = model.PrintVersionTitle,
            Customer = model.Customer,
            DecorationCode = model.DecorationCode,
            ColorType = model.ColorType,
            GlassTreatment = model.GlassTreatment,
            QualityClass = model.QualityClass,
            HdNumber = model.HdNumber,
            SampleLocation = model.SampleLocation,
            BoardLocation = model.BoardLocation,
            GaugeLocation = model.GaugeLocation,
            HasGauge = model.HasGauge,
            HasComplaint = model.HasComplaint,
            SamplesOnCamera = model.SamplesOnCamera,
            Notes = model.PrintVersionNotes
        });

        _repository.SaveArticles(articles);
        _repository.SavePrintVersions(printVersions);

        return QualityArticleCreateResult.Ok(
            $"Quality artikl {sapNumber} a tisková verze {printVersionNumber} byly založeny.",
            sapNumber,
            printVersionNumber);
    }

    public bool ExistsSapMaterialQualityData(
    string sapMaterialNumber)
    {
        var sapNumber =
            NormalizeSapNumber(sapMaterialNumber);

        if (string.IsNullOrWhiteSpace(sapNumber))
        {
            return false;
        }

        if (IsNonSapPlaceholder(sapNumber))
        {
            return false;
        }

        return _repository
            .LoadPrintVersions()
            .Any(item =>
                string.Equals(
                    NormalizeSapNumber(item.SapMaterialNumber),
                    sapNumber,
                    StringComparison.OrdinalIgnoreCase));
    }

    public bool ExistsPrintVersion(
        string fullPrintVersionNumber)
    {
        var value =
            fullPrintVersionNumber.Trim();

        return _repository
            .LoadPrintVersions()
            .Any(item =>
                string.Equals(
                    item.FullPrintVersionNumber?.Trim(),
                    value,
                    StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveDecorationName(
        SapMaterial sapMaterial)
    {
        var lastDecorationStep =
            GetLastDecorationStep(sapMaterial);

        if (string.IsNullOrWhiteSpace(lastDecorationStep))
        {
            return string.Empty;
        }

        var decorationName =
            _decorationRuleService.GetName(lastDecorationStep);

        return string.IsNullOrWhiteSpace(decorationName)
            ? lastDecorationStep
            : decorationName;
    }

    private static string GetLastDecorationStep(
        SapMaterial sapMaterial)
    {
        // Varianta 1: pokud má SAP model přímo řetězec DecorationChain.
        var decorationChain =
            sapMaterial.GlassInfo?.DecorationChain?.Trim();

        if (!string.IsNullOrWhiteSpace(decorationChain))
        {
            return decorationChain[^1].ToString();
        }

        // Varianta 2: pokud má SAP model kolekci DecorationSteps.
        var lastStep =
            sapMaterial.GlassInfo?.DecorationSteps?
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item))
                .LastOrDefault();

        return lastStep?.Trim()
            ?? string.Empty;
    }

    private static string NormalizeSapNumber(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    private static bool IsNonSapPlaceholder(
    string? sapMaterialNumber)
    {
        return string.Equals(
            NormalizeSapNumber(sapMaterialNumber),
            NonSapPlaceholderMaterialNumber,
            StringComparison.OrdinalIgnoreCase);
    }
}