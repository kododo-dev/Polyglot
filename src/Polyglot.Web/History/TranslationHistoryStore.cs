using System.Security.Claims;
using Kododo.CultureWay.Core.Model;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web.History;

/// <summary>
/// Wraps the translation store and records every value it adds, changes or removes, with the
/// signed-in user. The editor sends all translations on every save, so each write is compared with
/// what the store holds and only real differences are recorded.
/// </summary>
/// <remarks>
/// The store and the history are separate writes: the store is written first, and if the history
/// write then fails, the change stands and the failure is logged.
/// </remarks>
public sealed class TranslationHistoryStore(
    IStore inner,
    IServiceScopeFactory scopes,
    IHttpContextAccessor http,
    TimeProvider time,
    ILogger<TranslationHistoryStore> logger) : IStore
{
    public const string SystemUserName = "System";

    private const string ChangeSetItem = "polyglot.history.change-set";

    // Writes are compared with the store's state before them, so two of them must not interleave.
    private readonly SemaphoreSlim _writes = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => inner.InitializeAsync(cancellationToken);

    public Task<IReadOnlyList<Translation>> GetAllAsync(CancellationToken cancellationToken = default)
        => inner.GetAllAsync(cancellationToken);

    public async Task SetAsync(IReadOnlyCollection<Translation> translations, CancellationToken cancellationToken = default)
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var before = await CurrentValuesAsync(cancellationToken);
            var changes = new List<(string Key, string Culture, string? Old, string? New)>();

            // Last write wins per (key, culture), as in the store.
            foreach (var t in translations.GroupBy(t => (t.Key, t.Culture)).Select(g => g.Last()))
            {
                var old = before.GetValueOrDefault((t.Key, t.Culture));
                if (old != t.Value)
                    changes.Add((t.Key, t.Culture, old, t.Value));
            }

            await inner.SetAsync(translations, cancellationToken);
            await RecordAsync(changes);
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task DeleteAsync(IReadOnlyCollection<(string Key, string Culture)> keys, CancellationToken cancellationToken = default)
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var before = await CurrentValuesAsync(cancellationToken);
            var changes = keys
                .Distinct()
                .Where(before.ContainsKey)
                .Select(k => (k.Key, k.Culture, (string?)before[k], (string?)null))
                .ToList();

            await inner.DeleteAsync(keys, cancellationToken);
            await RecordAsync(changes);
        }
        finally
        {
            _writes.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetSupportedCulturesAsync(CancellationToken cancellationToken = default)
        => inner.GetSupportedCulturesAsync(cancellationToken);

    public Task AddSupportedCultureAsync(string culture, CancellationToken cancellationToken = default)
        => inner.AddSupportedCultureAsync(culture, cancellationToken);

    public Task RemoveSupportedCultureAsync(string culture, CancellationToken cancellationToken = default)
        => inner.RemoveSupportedCultureAsync(culture, cancellationToken);

    public Task<string?> GetDefaultCultureAsync(CancellationToken cancellationToken = default)
        => inner.GetDefaultCultureAsync(cancellationToken);

    public Task SetDefaultCultureAsync(string culture, CancellationToken cancellationToken = default)
        => inner.SetDefaultCultureAsync(culture, cancellationToken);

    private async Task<Dictionary<(string Key, string Culture), string>> CurrentValuesAsync(CancellationToken cancellationToken)
    {
        var all = await inner.GetAllAsync(cancellationToken);
        var values = new Dictionary<(string Key, string Culture), string>(all.Count);
        foreach (var t in all)
            values[(t.Key, t.Culture)] = t.Value;
        return values;
    }

    private async Task RecordAsync(List<(string Key, string Culture, string? Old, string? New)> changes)
    {
        if (changes.Count == 0)
            return;

        var user = http.HttpContext?.User;
        var userId = Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        var userName = user?.FindFirstValue("display_name") ?? user?.Identity?.Name ?? SystemUserName;
        var changeSetId = ChangeSetId();
        var now = time.GetUtcNow();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TranslationChanges.AddRange(changes.Select(c => new TranslationChange
            {
                ChangeSetId = changeSetId,
                Key = c.Key,
                Culture = c.Culture,
                OldValue = c.Old,
                NewValue = c.New,
                UserId = userId,
                UserName = userName,
                ChangedAt = now,
            }));
            // Not the request's token: the store is already written, so the history should follow.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record {Count} translation changes in the history.", changes.Count);
        }
    }

    // One id per request, so the deletes and updates of one save in the editor read as one change.
    private Guid ChangeSetId()
    {
        var items = http.HttpContext?.Items;
        if (items is null)
            return Guid.CreateVersion7();

        if (items[ChangeSetItem] is not Guid id)
            items[ChangeSetItem] = id = Guid.CreateVersion7();
        return id;
    }
}
