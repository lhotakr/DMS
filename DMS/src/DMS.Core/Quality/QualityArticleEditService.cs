namespace DMS.Core.Quality;

public sealed class QualityArticleEditService
{
    private readonly JsonQualityRepository _repository;

    public QualityArticleEditService(JsonQualityRepository repository)
    {
        _repository = repository;
    }

    public QualityArticleEditModel? Load(string query)
    {
        query = Normalize(query);

        var articles = _repository.LoadArticles();
        var printVersions = _repository.LoadPrintVersions();

        var normalizedSapNumber = NormalizeSapNumber(query);

        var matchedPrintVersions = printVersions
            .Where(item =>
                string.Equals(
                    item.FullPrintVersionNumber,
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    NormalizeSapNumber(item.SapMaterialNumber),
                    normalizedSapNumber,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    item.LegacyArticleNumber,
                    query,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FullPrintVersionNumber)
            .ToList();

        var legacyArticleNumber =
            matchedPrintVersions.FirstOrDefault()?.LegacyArticleNumber
            ?? TryExtractLegacyArticleNumber(query)
            ?? string.Empty;

        var article = articles.FirstOrDefault(item =>
            string.Equals(
                item.LegacyArticleNumber,
                legacyArticleNumber,
                StringComparison.OrdinalIgnoreCase));

        if (article is null && matchedPrintVersions.Count == 0)
        {
            return null;
        }

        var model = new QualityArticleEditModel
        {
            Query = query,
            LegacyArticleNumber = legacyArticleNumber,
            ImportantInfo = article?.ImportantInfo ?? string.Empty,
            ArticleNotes = article?.Notes ?? string.Empty
        };

        foreach (var printVersion in matchedPrintVersions)
        {
            model.PrintVersions.Add(ToEditModel(printVersion));
        }

        return model;
    }

    public QualityArticleEditResult Save(
        QualityArticleEditModel model,
        QualityPrintVersionEditModel selectedPrintVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    selectedPrintVersion.FullPrintVersionNumber))
            {
                return QualityArticleEditResult.Fail(
                    "Číslo tiskové verze nesmí být prázdné.");
            }

            SaveArticle(model);
            SavePrintVersion(selectedPrintVersion);

            return QualityArticleEditResult.Ok(
                $"Quality data tiskové verze " +
                $"{selectedPrintVersion.FullPrintVersionNumber} byla uložena.");
        }
        catch (Exception ex)
        {
            return QualityArticleEditResult.Fail(
                $"Uložení quality dat se nepodařilo.\n\n{ex.Message}");
        }
    }

    private void SaveArticle(QualityArticleEditModel model)
    {
        var articles = _repository.LoadArticles().ToList();

        var index = articles.FindIndex(item =>
            string.Equals(
                item.LegacyArticleNumber,
                model.LegacyArticleNumber,
                StringComparison.OrdinalIgnoreCase));

        QualityArticle updatedArticle;

        if (index >= 0)
        {
            var original = articles[index];

            updatedArticle = new QualityArticle
            {
                LegacyArticleNumber = original.LegacyArticleNumber,
                Title = original.Title,
                Prefix = original.Prefix,
                ArticleNumberPart = original.ArticleNumberPart,
                ImportantInfo = model.ImportantInfo,
                Notes = model.ArticleNotes,
                ImportedAt = original.ImportedAt,
                SourceFilePath = original.SourceFilePath
            };

            articles[index] = updatedArticle;
        }
        else
        {
            updatedArticle = new QualityArticle
            {
                LegacyArticleNumber = model.LegacyArticleNumber,
                ImportantInfo = model.ImportantInfo,
                Notes = model.ArticleNotes,
                ImportedAt = DateTime.Now,
                SourceFilePath = "QA02"
            };

            articles.Add(updatedArticle);
        }

        _repository.SaveArticles(
            articles.OrderBy(item => item.LegacyArticleNumber));
    }

    private void SavePrintVersion(
        QualityPrintVersionEditModel editModel)
    {
        var printVersions =
            _repository.LoadPrintVersions().ToList();

        var index = printVersions.FindIndex(item =>
            string.Equals(
                item.FullPrintVersionNumber,
                editModel.OriginalPrintVersionNumber,
                StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Tisková verze " +
                $"{editModel.OriginalPrintVersionNumber} nebyla nalezena.");
        }

        var original = printVersions[index];

        var originalTasksByNumber = original.Tasks
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.Number)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        var updated = new QualityPrintVersion
        {
            FullPrintVersionNumber =
                editModel.FullPrintVersionNumber.Trim(),

            LegacyArticleNumber =
                original.LegacyArticleNumber,

            GlassType =
                original.GlassType,

            VersionNumber =
                original.VersionNumber,

            SapMaterialNumber =
                NormalizeSapNumber(editModel.SapMaterialNumber),

            Title =
                editModel.Title.Trim(),

            Customer =
                editModel.Customer.Trim(),

            ColorType =
                editModel.ColorType.Trim(),

            GlassTreatment =
                editModel.GlassTreatment.Trim(),

            DecorationCode =
                editModel.DecorationCode.Trim(),

            HdNumber =
                editModel.HdNumber.Trim(),

            SampleLocation =
                editModel.SampleLocation.Trim(),

            BoardLocation =
                editModel.BoardLocation.Trim(),

            GaugeLocation =
                editModel.GaugeLocation.Trim(),

            HasGauge =
                editModel.HasGauge,

            HasComplaint =
                editModel.HasComplaint,

            SamplesOnCamera =
                editModel.SamplesOnCamera,

            Notes =
                editModel.Notes,

            Tasks = editModel.Tasks
    .Where(task =>
        !string.IsNullOrWhiteSpace(task.Text) ||
        task.DueDate.HasValue ||
        task.CreatedAt.HasValue ||
        !string.IsNullOrWhiteSpace(task.CreatedBy) ||
        task.CompletedAt.HasValue ||
        !string.IsNullOrWhiteSpace(task.CompletedBy))
    .Select(task =>
    {
        var hasText =
            !string.IsNullOrWhiteSpace(task.Text);

        var existedBefore =
            originalTasksByNumber.ContainsKey(task.Number);

        var isNewTask =
            hasText && !existedBefore;

        var createdAt =
            isNewTask && task.CreatedAt is null
                ? DateTime.Today
                : task.CreatedAt;

        var createdBy =
            isNewTask && string.IsNullOrWhiteSpace(task.CreatedBy)
                ? Environment.UserName
                : task.CreatedBy.Trim();

        var completedBy =
            task.CompletedAt.HasValue &&
            string.IsNullOrWhiteSpace(task.CompletedBy)
                ? Environment.UserName
                : task.CompletedBy.Trim();

        return new QualityTask
        {
            Number = task.Number,
            Text = task.Text.Trim(),
            DueDate = task.DueDate,

            CreatedAt = createdAt,
            CreatedBy = createdBy,

            CompletedAt = task.CompletedAt,
            CompletedBy = completedBy
        };
    })
    .ToList(),

            ImportedAt =
                DateTime.Now,

            SourceFilePath =
                original.SourceFilePath
        };

        printVersions[index] = updated;

        _repository.SavePrintVersions(
            printVersions.OrderBy(
                item => item.FullPrintVersionNumber));
    }

    private static QualityPrintVersionEditModel ToEditModel(
        QualityPrintVersion source)
    {
        var model = new QualityPrintVersionEditModel
        {
            OriginalPrintVersionNumber =
                source.FullPrintVersionNumber,

            FullPrintVersionNumber =
                source.FullPrintVersionNumber,

            SapMaterialNumber =
                source.SapMaterialNumber,

            Title =
                source.Title,

            Customer =
                source.Customer,

            ColorType =
                source.ColorType,

            GlassTreatment =
                source.GlassTreatment,

            DecorationCode =
                source.DecorationCode,

            HdNumber =
                source.HdNumber,

            SampleLocation =
                source.SampleLocation,

            BoardLocation =
                source.BoardLocation,

            GaugeLocation =
                source.GaugeLocation,

            HasGauge =
                source.HasGauge,

            HasComplaint =
                source.HasComplaint,

            SamplesOnCamera =
                source.SamplesOnCamera,

            Notes =
                source.Notes
        };

        for (var number = 1; number <= 8; number++)
        {
            var sourceTask = source.Tasks.FirstOrDefault(
                task => task.Number == number);

            model.Tasks.Add(new QualityTaskEditModel
            {
                Number = number,
                Text = sourceTask?.Text ?? string.Empty,
                DueDate = sourceTask?.DueDate,
                CreatedAt = sourceTask?.CreatedAt,
                CreatedBy = sourceTask?.CreatedBy ?? string.Empty,
                CompletedAt = sourceTask?.CompletedAt,
                CompletedBy = sourceTask?.CompletedBy ?? string.Empty
            });
        }

        return model;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeSapNumber(string? value)
    {
        var text = Normalize(value);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    private static string? TryExtractLegacyArticleNumber(
        string value)
    {
        if (value.Length >= 7 &&
            value.Take(7).All(char.IsDigit))
        {
            return value[..7];
        }

        return null;
    }
}