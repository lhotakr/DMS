namespace DMS.Core.Quality;

public sealed class QualityOrderMaintenanceService
{
    private readonly JsonQualityRepository _repository;

    public QualityOrderMaintenanceService(JsonQualityRepository repository)
    {
        _repository = repository;
    }

    public QualityOrderFormModel PrepareCreate(string? query)
    {
        var model = new QualityOrderFormModel
        {
            IsCreateMode = true,
            Query = Normalize(query),
            Released = false,
            ReleaseStatusCode = "Blocked"
        };

        if (string.IsNullOrWhiteSpace(model.Query))
        {
            ApplyCalculatedState(model);
            return model;
        }

        var printVersion = FindPrintVersion(model.Query);

        if (printVersion is not null)
        {
            FillFromPrintVersion(model, printVersion);
            ApplyCalculatedState(model);
            return model;
        }

        if (LooksLikeOrderNumber(model.Query))
        {
            model.OrderNumber = model.Query;
        }

        ApplyCalculatedState(model);
        return model;
    }

    public QualityOrderFormModel? PrepareEdit(string? orderNumber)
    {
        var order = FindOrder(orderNumber);

        if (order is null)
        {
            return null;
        }

        var model = new QualityOrderFormModel
        {
            IsCreateMode = false,
            Query = Normalize(orderNumber),
            OriginalOrder = order,
            OrderNumber = order.OrderNumber,
            PrintVersionNumber = order.PrintVersionNumber,
            SapMaterialNumber = order.SapMaterialNumber,
            Machine = order.Machine,
            ColorType = order.ColorType,
            ProductionStart = order.ProductionStart,
            ProductionEnd = order.ProductionEnd,
            OrderedQuantity = order.OrderedQuantity,
            ProducedQuantity = order.ProducedQuantity,
            QualityClass = order.QualityClass,
            LabOrderNumber = order.LabOrderNumber,
            LorealLabOrder = order.LorealLabOrder,
            Loreal = order.Loreal,
            SortingInHd = order.SortingInHd,
            StaysInHd = order.StaysInHd,
            Released = order.Released,
            Finished = IsFinished(order),
            Notes = order.Notes
        };

        var printVersion = FindPrintVersion(order.PrintVersionNumber)
            ?? FindPrintVersion(order.SapMaterialNumber);

        if (printVersion is not null)
        {
            FillFromPrintVersion(model, printVersion);

            model.Machine = string.IsNullOrWhiteSpace(order.Machine)
                ? model.Machine
                : order.Machine;
            model.ColorType = string.IsNullOrWhiteSpace(order.ColorType)
                ? model.ColorType
                : order.ColorType;
            model.QualityClass = string.IsNullOrWhiteSpace(order.QualityClass)
                ? model.QualityClass
                : order.QualityClass;
            model.ProductionStart = order.ProductionStart;
            model.ProductionEnd = order.ProductionEnd;
            model.OrderedQuantity = order.OrderedQuantity;
            model.ProducedQuantity = order.ProducedQuantity;
            model.LabOrderNumber = order.LabOrderNumber;
            model.LorealLabOrder = order.LorealLabOrder;
            model.Loreal = order.Loreal;
            model.SortingInHd = order.SortingInHd;
            model.StaysInHd = order.StaysInHd;
            model.Released = order.Released;
            model.Notes = order.Notes;
        }

        ApplyCalculatedState(model);
        return model;
    }

    public QualityOrder? FindOrder(string? orderNumber)
    {
        var query = Normalize(orderNumber);

        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        return _repository.LoadOrders()
            .FirstOrDefault(order =>
                string.Equals(
                    order.OrderNumber,
                    query,
                    StringComparison.OrdinalIgnoreCase));
    }

    public QualityPrintVersion? FindPrintVersion(string? query)
    {
        var value = Normalize(query);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedSap = NormalizeSapNumber(value);

        return _repository.LoadPrintVersions()
            .OrderBy(item => item.FullPrintVersionNumber)
            .FirstOrDefault(item =>
                string.Equals(
                    item.FullPrintVersionNumber,
                    value,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    item.LegacyArticleNumber,
                    value,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    NormalizeSapNumber(item.SapMaterialNumber),
                    normalizedSap,
                    StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<QualityOrderListRow> BuildOrderListRows()
    {
        var printVersions = _repository.LoadPrintVersions()
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPrintVersionNumber))
            .GroupBy(item => item.FullPrintVersionNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var qualityArticles = _repository.LoadArticles()
            .Where(item => !string.IsNullOrWhiteSpace(item.LegacyArticleNumber))
            .GroupBy(item => item.LegacyArticleNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        return _repository.LoadOrders()
            .OrderBy(order => order.Released)
            .ThenBy(order => IsFinished(order))
            .ThenByDescending(order => GetCreatedAt(order) ?? order.ProductionStart ?? order.ImportedAt)
            .ThenBy(order => order.OrderNumber)
            .Select(order => ToListRow(order, printVersions, qualityArticles))
            .ToList();
    }

    public QualityOrderSaveResult Save(QualityOrderFormModel model, string currentUserName)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(model.OrderNumber))
        {
            return QualityOrderSaveResult.Fail("Order number is required.");
        }

        if (string.IsNullOrWhiteSpace(model.PrintVersionNumber))
        {
            return QualityOrderSaveResult.Fail("Print version number is required.");
        }

        if (model.ProductionEnd.HasValue && !model.ProductionStart.HasValue)
        {
            return QualityOrderSaveResult.Fail("Production end cannot be set before production start. Unplanned orders cannot be finished.");
        }

        var orders = _repository.LoadOrders().ToList();
        var normalizedOrderNumber = Normalize(model.OrderNumber);

        var index = orders.FindIndex(order =>
            string.Equals(
                order.OrderNumber,
                normalizedOrderNumber,
                StringComparison.OrdinalIgnoreCase));

        if (model.IsCreateMode && index >= 0)
        {
            return QualityOrderSaveResult.Fail($"Order {normalizedOrderNumber} already exists.");
        }

        if (!model.IsCreateMode && index < 0)
        {
            return QualityOrderSaveResult.Fail($"Order {normalizedOrderNumber} does not exist.");
        }

        var original = index >= 0 ? orders[index] : null;
        var now = DateTime.Now;
        var isCreate = original is null;

        var createdAt = original?.CreatedAt ?? original?.Metadata.CreatedAt;
        if (!createdAt.HasValue || createdAt.Value == default)
        {
            createdAt = now;
        }

        var createdBy = !string.IsNullOrWhiteSpace(original?.CreatedBy)
            ? original.CreatedBy
            : !string.IsNullOrWhiteSpace(original?.Metadata.CreatedBy)
                ? original.Metadata.CreatedBy
                : currentUserName;

        var metadata = new QualityRecordMetadata
        {
            CreatedBy = createdBy,
            CreatedAt = createdAt.Value,
            ModifiedBy = isCreate ? string.Empty : currentUserName,
            ModifiedAt = isCreate ? null : now
        };

        var saved = new QualityOrder
        {
            Metadata = metadata,
            OrderNumber = normalizedOrderNumber,
            PrintVersionNumber = Normalize(model.PrintVersionNumber),
            SapMaterialNumber = NormalizeSapNumber(model.SapMaterialNumber),
            Machine = Normalize(model.Machine),
            Released = original?.Released ?? false,
            ProductionStart = model.ProductionStart,
            ProductionEnd = model.ProductionEnd,
            OrderedQuantity = model.OrderedQuantity,
            ProducedQuantity = model.ProducedQuantity,
            LabOrderNumber = Normalize(model.LabOrderNumber),
            LorealLabOrder = Normalize(model.LorealLabOrder),
            Loreal = model.Loreal,
            SortingInHd = model.SortingInHd,
            StaysInHd = model.StaysInHd,
            QualityClass = Normalize(model.QualityClass),
            SortingNumber = original?.SortingNumber ?? string.Empty,
            ColorType = Normalize(model.ColorType),
            Notes = model.Notes?.Trim() ?? string.Empty,
            DefectReport = original?.DefectReport ?? string.Empty,
            TesaTest = original?.TesaTest ?? false,
            AcetoneTest = original?.AcetoneTest ?? false,
            GridTest = original?.GridTest ?? false,
            VisualCheck = original?.VisualCheck ?? false,
            Approved = original?.Approved ?? false,
            ReleaseNotes = original?.ReleaseNotes ?? string.Empty,
            ReleasedBy = original?.ReleasedBy ?? string.Empty,
            ReleasedAt = original?.ReleasedAt,
            BlockedBy = original?.BlockedBy ?? string.Empty,
            BlockedAt = original?.BlockedAt,
            Finished = model.ProductionStart.HasValue && model.ProductionEnd.HasValue,
            ImportedAt = original?.ImportedAt ?? now,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            SourceFilePath = original?.SourceFilePath ?? "QO01"
        };

        if (index >= 0)
        {
            orders[index] = saved;
        }
        else
        {
            orders.Add(saved);
        }

        _repository.SaveOrders(orders.OrderBy(order => order.OrderNumber));

        return QualityOrderSaveResult.Ok(
            saved,
            model.IsCreateMode
                ? $"Order {saved.OrderNumber} was created."
                : $"Order {saved.OrderNumber} was updated.");
    }

    public QualityOrderSaveResult SetReleased(
        string orderNumber,
        bool released,
        string currentUserName,
        bool tesaTest = false,
        bool acetoneTest = false,
        bool gridTest = false,
        bool visualCheck = false,
        bool approved = false,
        string? releaseNotes = null)
    {
        var normalizedOrderNumber = Normalize(orderNumber);
        var orders = _repository.LoadOrders().ToList();
        var index = orders.FindIndex(order =>
            string.Equals(order.OrderNumber, normalizedOrderNumber, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return QualityOrderSaveResult.Fail($"Order {normalizedOrderNumber} does not exist.");
        }

        var original = orders[index];

        if (released && (!tesaTest || !acetoneTest || !gridTest || !visualCheck || !approved))
        {
            return QualityOrderSaveResult.Fail("All release checks and approval must be completed before releasing the order.");
        }

        var now = DateTime.Now;
        var createdAt = original.CreatedAt ?? original.Metadata.CreatedAt;
        if (createdAt == default)
        {
            createdAt = original.ImportedAt;
        }

        var createdBy = !string.IsNullOrWhiteSpace(original.CreatedBy)
            ? original.CreatedBy
            : original.Metadata.CreatedBy;

        var updated = new QualityOrder
        {
            Metadata = new QualityRecordMetadata
            {
                CreatedBy = createdBy,
                CreatedAt = createdAt,
                ModifiedBy = currentUserName,
                ModifiedAt = now
            },
            OrderNumber = original.OrderNumber,
            PrintVersionNumber = original.PrintVersionNumber,
            SapMaterialNumber = original.SapMaterialNumber,
            Machine = original.Machine,
            Released = released,
            ProductionStart = original.ProductionStart,
            ProductionEnd = original.ProductionEnd,
            OrderedQuantity = original.OrderedQuantity,
            ProducedQuantity = original.ProducedQuantity,
            LabOrderNumber = original.LabOrderNumber,
            LorealLabOrder = original.LorealLabOrder,
            Loreal = original.Loreal,
            SortingInHd = original.SortingInHd,
            StaysInHd = original.StaysInHd,
            QualityClass = original.QualityClass,
            SortingNumber = original.SortingNumber,
            ColorType = original.ColorType,
            Notes = original.Notes,
            DefectReport = original.DefectReport,
            TesaTest = released ? tesaTest : original.TesaTest,
            AcetoneTest = released ? acetoneTest : original.AcetoneTest,
            GridTest = released ? gridTest : original.GridTest,
            VisualCheck = released ? visualCheck : original.VisualCheck,
            Approved = released ? approved : original.Approved,
            ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes)
                ? original.ReleaseNotes
                : releaseNotes.Trim(),
            ReleasedBy = released
                ? currentUserName
                : original.ReleasedBy,
            ReleasedAt = released
                ? now
                : original.ReleasedAt,
            BlockedBy = released
                ? original.BlockedBy
                : currentUserName,
            BlockedAt = released
                ? original.BlockedAt
                : now,
            Finished = IsFinished(original),
            ImportedAt = original.ImportedAt,
            CreatedAt = original.CreatedAt,
            CreatedBy = original.CreatedBy,
            SourceFilePath = original.SourceFilePath
        };

        orders[index] = updated;
        _repository.SaveOrders(orders.OrderBy(order => order.OrderNumber));

        return QualityOrderSaveResult.Ok(
            updated,
            released
                ? $"Order {updated.OrderNumber} was released."
                : $"Order {updated.OrderNumber} was blocked.");
    }

    public static string GetScheduleStatusCode(QualityOrder order)
    {
        if (!order.ProductionStart.HasValue)
        {
            return "Unplanned";
        }

        return order.ProductionEnd.HasValue
            ? "Finished"
            : "Scheduled";
    }

    public static bool IsFinished(QualityOrder order)
    {
        return order.ProductionStart.HasValue && order.ProductionEnd.HasValue;
    }

    private QualityOrderListRow ToListRow(
        QualityOrder order,
        IReadOnlyDictionary<string, QualityPrintVersion> printVersions,
        IReadOnlyDictionary<string, QualityArticle> qualityArticles)
    {
        printVersions.TryGetValue(order.PrintVersionNumber, out var printVersion);

        QualityArticle? qualityArticle = null;
        if (!string.IsNullOrWhiteSpace(printVersion?.LegacyArticleNumber))
        {
            qualityArticles.TryGetValue(printVersion.LegacyArticleNumber, out qualityArticle);
        }

        var openTasks = GetOpenTasks(printVersion);
        var scheduleCode = GetScheduleStatusCode(order);
        var createdAt = GetCreatedAt(order);

        return new QualityOrderListRow
        {
            OrderNumber = order.OrderNumber,
            PrintVersionNumber = order.PrintVersionNumber,
            SapMaterialNumber = order.SapMaterialNumber,
            Customer = printVersion?.Customer ?? string.Empty,
            Title = printVersion?.Title ?? string.Empty,
            ArticleTitle = qualityArticle?.Title ?? string.Empty,
            OpenTasksText = FormatOpenTasksInline(openTasks),
            OpenTaskCount = openTasks.Count,
            Machine = NormalizeMultiValue(order.Machine),
            ColorType = NormalizeMultiValue(order.ColorType),
            ProductionStartText = FormatDate(order.ProductionStart),
            ProductionEndText = FormatDate(order.ProductionEnd),
            OrderedQuantity = order.OrderedQuantity,
            ProducedQuantity = order.ProducedQuantity,
            QualityClass = order.QualityClass,
            LorealText = ToYesNo(order.Loreal),
            ReleasedText = ToYesNo(order.Released),
            BlockedText = ToYesNo(!order.Released),
            ReleaseIcon = order.Released ? "✓" : "⊘",
            ReleaseStatusCode = order.Released ? "Released" : "Blocked",
            ScheduleStatusCode = scheduleCode,
            ScheduleStatusText = scheduleCode,
            ScheduleSemaphore = scheduleCode switch
            {
                "Finished" => "🟢",
                "Scheduled" => "🟠",
                _ => "🔴"
            },
            FinishedText = ToYesNo(IsFinished(order)),
            CreatedAtDate = createdAt,
            CreatedAtText = FormatDateTime(createdAt),
            Notes = order.Notes,
            Source = order
        };
    }

    private void FillFromPrintVersion(QualityOrderFormModel model, QualityPrintVersion printVersion)
    {
        model.SourcePrintVersion = printVersion;
        model.LegacyArticleNumber = printVersion.LegacyArticleNumber;
        model.PrintVersionNumber = printVersion.FullPrintVersionNumber;
        model.SapMaterialNumber = printVersion.SapMaterialNumber;
        model.Customer = printVersion.Customer;
        model.Title = printVersion.Title;
        model.SampleLocation = printVersion.SampleLocation;
        model.BoardLocation = printVersion.BoardLocation;
        model.HdNumber = printVersion.HdNumber;
        model.GaugeLocation = printVersion.GaugeLocation;
        model.HasGauge = printVersion.HasGauge;
        model.SamplesOnCamera = printVersion.SamplesOnCamera;
        model.HasComplaint = printVersion.HasComplaint;
        model.PrintVersionNotes = printVersion.Notes;
        model.ColorType = printVersion.ColorType;
        model.QualityClass = printVersion.QualityClass;

        var qualityArticle = FindQualityArticle(printVersion.LegacyArticleNumber);
        model.ArticleTitle = qualityArticle?.Title ?? string.Empty;
        model.ArticleImportantInfo = qualityArticle?.ImportantInfo ?? string.Empty;
        model.ArticleNotes = qualityArticle?.Notes ?? string.Empty;
        model.Loreal = IsLorealCustomer(printVersion.Customer) ||
                       ContainsLoreal(printVersion.Customer) ||
                       ContainsLoreal(printVersion.Notes) ||
                       ContainsLoreal(printVersion.Title);

        var realTasks = printVersion.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Text))
            .ToList();

        model.AllTasksCompleted = realTasks.Count == 0 ||
                                  realTasks.All(task => task.CompletedAt.HasValue);

        var openTasks = realTasks
            .Where(task => !task.CompletedAt.HasValue)
            .OrderBy(task => task.Number)
            .ToList();

        model.OpenTasks = openTasks;
        model.OpenTasksText = FormatOpenTasksMultiline(openTasks);

        model.TaskSummary = realTasks.Count == 0
            ? "No tasks defined."
            : $"{realTasks.Count(task => task.CompletedAt.HasValue)}/{realTasks.Count}";
    }

    private QualityArticle? FindQualityArticle(string? legacyArticleNumber)
    {
        var normalized = Normalize(legacyArticleNumber);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return _repository.LoadArticles()
            .FirstOrDefault(item =>
                string.Equals(item.LegacyArticleNumber, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsLorealCustomer(string? customerText)
    {
        if (string.IsNullOrWhiteSpace(customerText))
        {
            return false;
        }

        var normalizedCustomer = NormalizeCustomerKey(customerText);

        return _repository.LoadCustomers()
            .Where(customer => customer.IsActive && customer.IsLoreal)
            .Any(customer =>
            {
                var code = NormalizeCustomerKey(customer.Code);
                var name = NormalizeCustomerKey(customer.Name);

                return (!string.IsNullOrWhiteSpace(code) &&
                        string.Equals(normalizedCustomer, code, StringComparison.OrdinalIgnoreCase)) ||
                       (!string.IsNullOrWhiteSpace(name) &&
                        (string.Equals(normalizedCustomer, name, StringComparison.OrdinalIgnoreCase) ||
                         normalizedCustomer.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                         name.Contains(normalizedCustomer, StringComparison.OrdinalIgnoreCase)));
            });
    }

    private static void ApplyCalculatedState(QualityOrderFormModel model)
    {
        model.Finished = model.ProductionStart.HasValue && model.ProductionEnd.HasValue;
        model.ScheduleStatusCode = !model.ProductionStart.HasValue
            ? "Unplanned"
            : model.ProductionEnd.HasValue
                ? "Finished"
                : "Scheduled";
        model.ReleaseStatusCode = model.Released ? "Released" : "Blocked";
    }

    private static bool ContainsLoreal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace("´", string.Empty);

        return normalized.Contains("loreal", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("l oreal", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("loréal", StringComparison.OrdinalIgnoreCase);
    }

    private static List<QualityTask> GetOpenTasks(QualityPrintVersion? printVersion)
    {
        return printVersion?.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Text) && !task.CompletedAt.HasValue)
            .OrderBy(task => task.Number)
            .ToList()
            ?? new List<QualityTask>();
    }

    private static string FormatOpenTasksInline(IReadOnlyList<QualityTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" | ", tasks.Select(FormatTaskShort));
    }

    private static string FormatOpenTasksMultiline(IReadOnlyList<QualityTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, tasks.Select(FormatTaskLine));
    }

    private static string FormatTaskShort(QualityTask task)
    {
        var text = Normalize(task.Text);
        var prefix = task.Number > 0 ? $"{task.Number}: " : string.Empty;
        return $"{prefix}{text}".Trim();
    }

    private static string FormatTaskLine(QualityTask task)
    {
        var line = FormatTaskShort(task);

        if (task.DueDate.HasValue)
        {
            line += $" ({task.DueDate.Value:dd.MM.yyyy})";
        }

        if (!string.IsNullOrWhiteSpace(task.CreatedBy))
        {
            line += $" - {task.CreatedBy}";
        }

        return line;
    }

    private static bool LooksLikeOrderNumber(string value)
    {
        return value.Length is >= 4 and <= 8 && value.All(char.IsDigit);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string NormalizeCustomerKey(string? value)
    {
        return Normalize(value)
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace("´", string.Empty)
            .Replace(".", string.Empty)
            .Replace(",", string.Empty)
            .Trim();
    }

    private static string NormalizeSapNumber(string? value)
    {
        var text = Normalize(value);

        if (text.Length >= 10 && text.All(char.IsDigit))
        {
            return text[^10..];
        }

        return text;
    }

    private static DateTime? GetCreatedAt(QualityOrder order)
    {
        if (order.CreatedAt.HasValue && order.CreatedAt.Value != default)
        {
            return order.CreatedAt.Value;
        }

        if (order.Metadata.CreatedAt != default)
        {
            return order.Metadata.CreatedAt;
        }

        return order.ImportedAt == default
            ? null
            : order.ImportedAt;
    }

    private static string FormatDate(DateTime? date)
    {
        return date?.ToString("dd.MM.yyyy") ?? string.Empty;
    }

    private static string FormatDateTime(DateTime? date)
    {
        return date?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
    }

    private static string ToYesNo(bool value)
    {
        return value ? "✓" : "—";
    }

    private static string NormalizeMultiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            value.Split(
                    new[] { ";#", ";", "," },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
