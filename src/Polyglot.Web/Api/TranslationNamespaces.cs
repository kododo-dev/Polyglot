namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// Namespaces as the CultureWay editor derives them: CultureWay has no namespace entity, a key's
/// namespace is everything before its last dot. Kept in step with the editor's <c>extractNamespace</c>
/// and its subtree filter, so that the API selects what the editor shows.
/// </summary>
public static class TranslationNamespaces
{
    public static string Of(string key)
    {
        var i = key.LastIndexOf('.');
        return i > 0 ? key[..i] : "";
    }

    /// <summary>Whether <paramref name="key"/> lies in any of <paramref name="namespaces"/> or under one; true when there are none.</summary>
    public static bool Matches(string key, IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count == 0)
            return true;

        var ns = Of(key);
        foreach (var filter in namespaces)
        {
            if (ns == filter || ns.StartsWith(filter + ".", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>A usable filter: not empty, and no empty segment (<c>.Checkout</c>, <c>A..B</c>, <c>Checkout.</c>).</summary>
    public static bool IsValid(string ns)
        => ns.Length > 0 && ns.Split('.').All(segment => segment.Length > 0);
}
