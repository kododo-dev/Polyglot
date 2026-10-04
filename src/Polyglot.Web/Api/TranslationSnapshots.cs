using System.Collections.Concurrent;
using Kododo.CultureWay.Core.Store;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// Every translation of every culture, read once and reused for <see cref="ApiOptions.SnapshotCacheSeconds"/>,
/// so that a poll does not select the whole store. A TTL rather than invalidation on write: with several
/// replicas, a write handled by one cannot invalidate the others.
/// </summary>
public sealed class TranslationSnapshots(
    IStore store,
    IEnumerable<IReadOnlySource> readOnlySources,
    ApiOptions api,
    TimeProvider time)
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private TranslationSnapshot? _current;

    public async Task<TranslationSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_current))
            return _current!;

        // One reader refreshes while the others wait for its result, instead of all selecting at once.
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (!IsFresh(_current))
                _current = new TranslationSnapshot(await ReadAsync(cancellationToken), time.GetUtcNow());

            return _current!;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private bool IsFresh(TranslationSnapshot? snapshot)
        => snapshot is not null
           && time.GetUtcNow() - snapshot.TakenAt < TimeSpan.FromSeconds(api.SnapshotCacheSeconds);

    // The editor's read path: the store merged over the read-only defaults, store values winning.
    private async Task<Dictionary<string, Dictionary<string, string>>> ReadAsync(CancellationToken cancellationToken)
    {
        var byCulture = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> Culture(string culture)
        {
            if (!byCulture.TryGetValue(culture, out var values))
                byCulture[culture] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            return values;
        }

        foreach (var t in await store.GetAllAsync(cancellationToken))
            Culture(t.Culture)[t.Key] = t.Value;

        foreach (var source in readOnlySources)
        {
            foreach (var t in await source.GetDefaultsAsync(cancellationToken))
                Culture(t.Culture).TryAdd(t.Key, t.Value);
        }

        return byCulture;
    }
}

/// <summary>
/// One read of the store, with the responses resolved from it memoised for as long as it lives.
/// </summary>
public sealed class TranslationSnapshot(
    IReadOnlyDictionary<string, Dictionary<string, string>> byCulture,
    DateTimeOffset takenAt)
{
    private readonly ConcurrentDictionary<string, ResolvedTranslations> _resolved = new(StringComparer.Ordinal);

    public DateTimeOffset TakenAt { get; } = takenAt;

    /// <summary>
    /// The translations an app sees in the first culture of <paramref name="chain"/>: each key takes its
    /// value from the earliest culture that has one, as CultureWay's localizer resolves it.
    /// </summary>
    /// <param name="chain">The culture, its parents, then the default culture.</param>
    /// <param name="namespaces">Sorted, distinct namespace filters; empty for the whole culture.</param>
    public ResolvedTranslations Resolve(IReadOnlyList<string> chain, IReadOnlyList<string> namespaces)
        => _resolved.GetOrAdd(
            string.Join('\n', chain) + "\n\n" + string.Join('\n', namespaces),
            _ => ResolveUncached(chain, namespaces));

    private ResolvedTranslations ResolveUncached(IReadOnlyList<string> chain, IReadOnlyList<string> namespaces)
    {
        var translations = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var culture in chain)
        {
            if (!byCulture.TryGetValue(culture, out var values))
                continue;

            foreach (var (key, value) in values)
            {
                if (TranslationNamespaces.Matches(key, namespaces))
                    translations.TryAdd(key, value);
            }
        }

        var version = ContentVersion.Of(translations.SelectMany(t => new[] { t.Key, t.Value }));
        return new ResolvedTranslations(version, translations);
    }
}

public sealed record ResolvedTranslations(string Version, IReadOnlyDictionary<string, string> Translations);
