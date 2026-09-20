namespace Kododo.Polyglot.Web;

/// <summary>
/// Instance settings read from the <c>Polyglot</c> configuration section
/// (environment variables use the <c>Polyglot__</c> prefix).
/// </summary>
public sealed class PolyglotOptions
{
    public const string SectionName = "Polyglot";

    /// <summary>Comma-separated cultures the instance starts with, e.g. <c>en,pl,de</c>.</summary>
    public string Cultures { get; set; } = "en";

    /// <summary>Default culture. Added to <see cref="Cultures"/> if missing.</summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>Path the translation editor is mounted at.</summary>
    public string EditorPath { get; set; } = "/translations";

    /// <summary>Optional path base when the app is served under a sub-path behind a reverse proxy.</summary>
    public string PathBase { get; set; } = "";

    public string[] GetCultures()
    {
        var cultures = Cultures
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Prepend(DefaultCulture.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return cultures.Length > 0 ? cultures : ["en"];
    }

    public string GetEditorPath()
    {
        var path = "/" + EditorPath.Trim().Trim('/');
        return path;
    }
}
