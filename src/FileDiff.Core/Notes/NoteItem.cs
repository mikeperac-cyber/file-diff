namespace FileDiff.Core.Notes;

/// <summary>One sidebar note. <see cref="DueLocal"/> set means reminder; <see cref="IsDone"/> tracks to-dos.</summary>
public sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    /// <summary>Local due date (date part only). Null means no reminder.</summary>
    public DateTime? DueLocal { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsOverdue(DateTime nowLocal) =>
        !IsDone && DueLocal.HasValue && DueLocal.Value.Date < nowLocal.Date;

    public bool IsDueToday(DateTime nowLocal) =>
        !IsDone && DueLocal.HasValue && DueLocal.Value.Date == nowLocal.Date;
}
