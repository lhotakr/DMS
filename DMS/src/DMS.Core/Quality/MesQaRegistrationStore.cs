using System.Text;
using System.Text.Json;

namespace DMS.Core.Quality;

/// <summary>Read/write queue for plant 2000 -> Quality requests. It never writes FASTEC or SAP.</summary>
public sealed class MesQaRegistration
{
    public string Order2000 { get; set; } = string.Empty;
    public string Order9200 { get; set; } = string.Empty;
    public string SapMaterialNumber { get; set; } = string.Empty;
    public decimal TargetQuantity { get; set; }
    public string Status { get; set; } = "Pending";
    public string RegisteredBy { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public string AcceptedBy { get; set; } = string.Empty;
    public DateTime? AcceptedAt { get; set; }
}

public sealed class MesQaRegistrationStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public MesQaRegistrationStore(string dataRootPath)
    {
        var paths = new QualityStoragePaths(dataRootPath);
        _path = Path.Combine(paths.QualityPath, "mes-qa-registrations.json");
    }

    public IReadOnlyList<MesQaRegistration> Load()
    {
        if (!File.Exists(_path)) return Array.Empty<MesQaRegistration>();
        return JsonSerializer.Deserialize<List<MesQaRegistration>>(File.ReadAllText(_path, Encoding.UTF8), JsonOptions)
            ?? new List<MesQaRegistration>();
    }

    private T InExclusiveLock<T>(Func<List<MesQaRegistration>, T> change)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // One writer at a time across DMS clients using a shared Quality folder.
        using var streamLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var rows = Load().ToList();
        var result = change(rows);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(rows, JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return result;
    }

    public MesQaRegistration Register(string order2000, string sapMaterial, decimal targetQuantity, string user)
    {
        if (string.IsNullOrWhiteSpace(order2000)) throw new ArgumentException("MES order 2000 is required.");
        if (targetQuantity <= 0 || decimal.Truncate(targetQuantity) != targetQuantity || targetQuantity > int.MaxValue)
            throw new InvalidOperationException("MES target quantity must be a positive whole number within Int32 range.");
        return InExclusiveLock(rows =>
        {
            var existing = rows.FirstOrDefault(r => string.Equals(r.Order2000, order2000.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                // Never silently overwrite a pending or accepted request from another user.
                if (existing.TargetQuantity != targetQuantity ||
                    !string.Equals(existing.SapMaterialNumber, sapMaterial?.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Registration already exists with different MES data. Resolve it in Quality.");
                return existing;
            }
            var request = new MesQaRegistration
            {
                Order2000 = order2000.Trim(), SapMaterialNumber = sapMaterial?.Trim() ?? "",
                TargetQuantity = targetQuantity, RegisteredBy = user, RegisteredAt = DateTime.Now
            };
            rows.Add(request);
            return request;
        });
    }

    public void MarkAccepted(string order2000, string order9200, string user)
    {
        if (string.IsNullOrWhiteSpace(order9200)) throw new ArgumentException("Order 9200 is required.");
        InExclusiveLock(rows =>
        {
            var item = rows.SingleOrDefault(x => string.Equals(x.Order2000, order2000, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("MES registration was not found.");
            if (item.Status == "Accepted" && item.Order9200 == order9200) return 0;
            if (item.Status != "Pending") throw new InvalidOperationException("Registration is already assigned elsewhere.");
            if (rows.Any(x => !ReferenceEquals(x, item) &&
                string.Equals(x.Order9200, order9200, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Order 9200 is already linked to another MES order.");
            item.Order9200 = order9200.Trim();
            item.Status = "Accepted";
            item.AcceptedBy = user;
            item.AcceptedAt = DateTime.Now;
            return 0;
        });
    }
}
