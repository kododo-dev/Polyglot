using Kododo.CultureWay;
using Kododo.CultureWay.Core.Model;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.History;

public sealed record HistoryFilter(string? Key, string? Culture, Guid? UserId);

public sealed record HistoryPage(IReadOnlyList<TranslationChange> Changes, long? OlderThan);

/// <summary>Reads the change history and undoes single changes.</summary>
public sealed class TranslationHistory(AppDbContext db, IStore store, CultureWayOptions cultureWay)
{
    public const int PageSize = 50;

    /// <summary>
    /// The newest changes matching <paramref name="filter"/>, older than <paramref name="before"/> when
    /// given. <see cref="HistoryPage.OlderThan"/> is the cursor for the next page, or null on the last one.
    /// </summary>
    public async Task<HistoryPage> ListAsync(HistoryFilter filter, long? before, CancellationToken cancellationToken = default)
    {
        var query = db.TranslationChanges.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Key))
            query = query.Where(c => EF.Functions.ILike(c.Key, "%" + EscapeLike(filter.Key.Trim()) + "%"));
        if (!string.IsNullOrWhiteSpace(filter.Culture))
            query = query.Where(c => c.Culture == filter.Culture);
        if (filter.UserId is { } userId)
            query = query.Where(c => c.UserId == userId);
        if (before is { } id)
            query = query.Where(c => c.Id < id);

        // One extra row tells whether there is another page.
        var rows = await query.OrderByDescending(c => c.Id).Take(PageSize + 1).ToListAsync(cancellationToken);
        return rows.Count > PageSize
            ? new HistoryPage(rows[..PageSize], rows[PageSize - 1].Id)
            : new HistoryPage(rows, null);
    }

    /// <summary>The cultures and users that appear in the history, for the filters.</summary>
    public async Task<(IReadOnlyList<string> Cultures, IReadOnlyList<(Guid Id, string Name)> Users)> FilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var cultures = await db.TranslationChanges
            .Select(c => c.Culture).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);

        // The latest name of each user, since a user may have been renamed.
        var users = await db.TranslationChanges
            .Where(c => c.UserId != null)
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { Id = g.Key, Name = g.OrderByDescending(c => c.Id).Select(c => c.UserName).First() })
            .ToListAsync(cancellationToken);

        return (cultures, users.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).Select(u => (u.Id, u.Name)).ToList());
    }

    /// <summary>
    /// Sets a translation back to its value before the change: restores the old value, or removes
    /// the translation when the change added it. The undo goes through the store, so it is recorded too.
    /// </summary>
    public async Task<string[]> UndoAsync(long changeId, CancellationToken cancellationToken = default)
    {
        var change = await db.TranslationChanges.AsNoTracking().FirstOrDefaultAsync(c => c.Id == changeId, cancellationToken);
        if (change is null)
            return ["This change no longer exists."];

        if (!cultureWay.SupportedCultures.Contains(change.Culture, StringComparer.OrdinalIgnoreCase))
            return [$"The language '{change.Culture}' is no longer in Polyglot, so its translations cannot be restored."];

        if (change.OldValue is null)
            await store.DeleteAsync([(change.Key, change.Culture)], cancellationToken);
        else
            await store.SetAsync([new Translation(change.Key, change.Culture, change.OldValue)], cancellationToken);

        return [];
    }

    private static string EscapeLike(string value)
        => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
