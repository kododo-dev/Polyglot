using Microsoft.AspNetCore.Html;

namespace Kododo.Polyglot.Web.Pages.Shared;

/// <summary>
/// Inline SVG icons, drawn with strokes so they take the text color. Shapes follow Feather (MIT).
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["globe"] = """<circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>""",
        ["users"] = """<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>""",
        ["user"] = """<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>""",
        ["key"] = """<circle cx="7.5" cy="15.5" r="5.5"/><path d="M11.4 11.6 21 2"/><path d="m16 7 3 3"/><path d="m19 4 2 2"/>""",
        ["home"] = """<path d="M3 9.5 12 3l9 6.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/>""",
        ["logout"] = """<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="m16 17 5-5-5-5"/><path d="M21 12H9"/>""",
        ["sun"] = """<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41"/>""",
        ["moon"] = """<path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>""",
        ["menu"] = """<path d="M3 6h18M3 12h18M3 18h18"/>""",
        ["plus"] = """<path d="M12 5v14M5 12h14"/>""",
        ["copy"] = """<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>""",
        ["check"] = """<path d="M20 6 9 17l-5-5"/>""",
        ["alert"] = """<circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/>""",
        ["info"] = """<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>""",
        ["lock"] = """<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>""",
        ["arrow-right"] = """<path d="M5 12h14M12 5l7 7-7 7"/>""",
    };

    public static IHtmlContent Get(string name, string? label = null)
    {
        var a11y = label is null ? "aria-hidden=\"true\"" : $"role=\"img\" aria-label=\"{System.Net.WebUtility.HtmlEncode(label)}\"";
        return new HtmlString($"""<svg class="icon" viewBox="0 0 24 24" {a11y}>{Paths[name]}</svg>""");
    }
}

/// <summary>The model of the <c>_Alerts</c> partial: a success message and any errors.</summary>
public sealed record Alerts(string? Message, IReadOnlyList<string> Errors);
