using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.History;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages;

[Authorize(Policy = PolyglotPolicies.Editor)]
public class HistoryModel(TranslationHistory history) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Key { get; set; }

    [BindProperty(SupportsGet = true)] public string? Culture { get; set; }

    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserId { get; set; }

    [BindProperty(SupportsGet = true)] public long? Before { get; set; }

    [TempData] public string? Message { get; set; }

    public IReadOnlyList<TranslationChange> Changes { get; private set; } = [];

    public long? OlderThan { get; private set; }

    public IReadOnlyList<string> Cultures { get; private set; } = [];

    public IReadOnlyList<(Guid Id, string Name)> Users { get; private set; } = [];

    public string[] Errors { get; private set; } = [];

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Key) || !string.IsNullOrWhiteSpace(Culture) || UserId is not null;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostUndoAsync(long id)
    {
        var errors = await history.UndoAsync(id, HttpContext.RequestAborted);
        if (errors.Length > 0)
        {
            Errors = errors;
            await LoadAsync();
            return Page();
        }

        Message = "Change undone. The undo is in the history too.";
        return RedirectToPage(new { key = Key, culture = Culture, user = UserId });
    }

    /// <summary>The filters as route values, plus the page cursor when given.</summary>
    public object Query(long? before = null) => new { key = Key, culture = Culture, user = UserId, before };

    private async Task LoadAsync()
    {
        var page = await history.ListAsync(new HistoryFilter(Key, Culture, UserId), Before, HttpContext.RequestAborted);
        Changes = page.Changes;
        OlderThan = page.OlderThan;
        (Cultures, Users) = await history.FilterOptionsAsync(HttpContext.RequestAborted);
    }
}
