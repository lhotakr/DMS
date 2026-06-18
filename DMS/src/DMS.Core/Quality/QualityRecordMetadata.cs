public sealed class QualityRecordMetadata
{
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }

    public string ModifiedBy { get; init; } = string.Empty;
    public DateTime? ModifiedAt { get; init; }
}