namespace Kododo.Polyglot.Web.Data;

/// <summary>
/// One change to one translation: a value added, changed or removed. Written by
/// <see cref="History.TranslationHistoryStore"/> for every write that reaches the translation store.
/// </summary>
public sealed class TranslationChange
{
    public long Id { get; set; }

    /// <summary>Shared by the changes made in one request, such as one save in the editor.</summary>
    public Guid ChangeSetId { get; set; }

    public string Key { get; set; } = "";

    public string Culture { get; set; } = "";

    /// <summary>Null when the translation was added.</summary>
    public string? OldValue { get; set; }

    /// <summary>Null when the translation was removed.</summary>
    public string? NewValue { get; set; }

    /// <summary>Null for changes made outside a signed-in request.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The user's display name at the time, so the entry still reads right after a rename.</summary>
    public string UserName { get; set; } = "";

    public DateTimeOffset ChangedAt { get; set; }
}
