using Kododo.CultureWay;
using Kododo.CultureWay.Core.Model;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Demo;

/// <summary>Fills a new demo instance with the translations of a made-up shop, its translators and their history.</summary>
public static class DemoSeeder
{
    // The final values; the history below ends at these. Some German ones are missing on purpose,
    // so the editor has gaps to show.
    private static readonly (string Key, string En, string Pl, string? De)[] Translations =
    [
        ("Common.Save", "Save", "Zapisz", "Speichern"),
        ("Common.Cancel", "Cancel", "Anuluj", "Abbrechen"),
        ("Common.Search", "Search", "Szukaj", "Suchen"),
        ("Common.Loading", "Loading…", "Ładowanie…", "Wird geladen…"),
        ("Account.SignIn", "Sign in", "Zaloguj się", "Anmelden"),
        ("Account.SignOut", "Sign out", "Wyloguj się", "Abmelden"),
        ("Account.Welcome", "Welcome back, {0}!", "Witaj ponownie, {0}!", "Willkommen zurück, {0}!"),
        ("Checkout.Title", "Checkout", "Podsumowanie zamówienia", "Kasse"),
        ("Checkout.Total", "Total", "Razem", "Gesamt"),
        ("Checkout.Shipping", "Shipping", "Dostawa", "Versand"),
        ("Checkout.FreeShipping", "Free shipping on orders over {0}", "Darmowa dostawa przy zamówieniu powyżej {0}", null),
        ("Checkout.Pay", "Pay now", "Zapłać", "Jetzt bezahlen"),
        ("Checkout.Pay.Tooltip", "You will be redirected to the payment provider.", "Przekierujemy Cię do operatora płatności.", null),
        ("Errors.NotFound", "We couldn't find this page.", "Nie znaleźliśmy tej strony.", "Diese Seite wurde nicht gefunden."),
        ("Errors.PaymentDeclined", "Your payment was declined. Try another card.", "Płatność została odrzucona. Spróbuj innej karty.", null),
        ("Errors.Generic", "Something went wrong. Please try again.", "Coś poszło nie tak. Spróbuj ponownie.", "Etwas ist schiefgelaufen. Bitte versuche es erneut."),
    ];

    private const string Anna = "anna";
    private const string Lukas = "lukas";

    // Each entry is one save: who, how long ago, and what changed (old value null = added).
    private static readonly (string User, TimeSpan Ago, (string Key, string Culture, string? Old, string New)[] Changes)[] History =
    [
        (Lukas, TimeSpan.FromDays(6), [("Common.Save", "de", null, "Speichern"), ("Common.Cancel", "de", null, "Abbrechen")]),
        (Anna, TimeSpan.FromDays(5), [("Checkout.Pay", "pl", null, "Zapłać teraz"), ("Checkout.Total", "pl", null, "Razem")]),
        (Lukas, TimeSpan.FromDays(4), [("Checkout.Pay", "de", null, "Jetzt bezahlen"), ("Account.SignIn", "de", null, "Einloggen")]),
        (Anna, TimeSpan.FromDays(2), [("Checkout.Pay", "pl", "Zapłać teraz", "Zapłać")]),
        (Lukas, TimeSpan.FromDays(1), [("Account.SignIn", "de", "Einloggen", "Anmelden")]),
        (Anna, TimeSpan.FromHours(20), [("Errors.NotFound", "pl", "Nie znaleziono", "Nie znaleźliśmy tej strony.")]),
        (Anna, TimeSpan.FromHours(3), [("Checkout.Pay", "en", "Pay", "Pay now")]),
    ];

    /// <summary>
    /// Seeds the instance once, right after the demo administrator was created. Nobody can add users
    /// in the demo, so any other user means it has been seeded already.
    /// </summary>
    public static async Task SeedDemoAsync(this WebApplication app, DemoOptions demo, bool apiEnabled)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserService>();
        var db = services.GetRequiredService<AppDbContext>();

        var existing = await users.ListAsync();
        if (existing.Count != 1)
            return;

        var admin = existing[0];
        var anna = await CreateUserAsync(users, Anna, "Anna Kowalska");
        var lukas = await CreateUserAsync(users, Lukas, "Lukas Becker");
        var former = await CreateUserAsync(users, "former-intern", "Former intern");
        await users.SetDisabledAsync(former.Id, true);

        var cultures = services.GetRequiredService<CultureWayOptions>().SupportedCultures;
        bool Supported(string culture) => cultures.Contains(culture, StringComparer.OrdinalIgnoreCase);

        var translations = Translations
            .SelectMany(t => new[] { ("en", t.En), ("pl", t.Pl), ("de", t.De) }
                .Where(v => v.Item2 is not null && Supported(v.Item1))
                .Select(v => new Translation(t.Key, v.Item1, v.Item2!)))
            .ToList();
        await services.GetRequiredService<IStore>().SetAsync(translations);

        // The write above was recorded as the system's. The demo shows the translators' story instead.
        await db.TranslationChanges.ExecuteDeleteAsync();
        var now = DateTimeOffset.UtcNow;
        var byName = new Dictionary<string, User> { [Anna] = anna, [Lukas] = lukas };
        foreach (var save in History)
        {
            var user = byName[save.User];
            var changeSetId = Guid.CreateVersion7();
            db.TranslationChanges.AddRange(save.Changes
                .Where(c => Supported(c.Culture))
                .Select(c => new TranslationChange
                {
                    ChangeSetId = changeSetId,
                    Key = c.Key,
                    Culture = c.Culture,
                    OldValue = c.Old,
                    NewValue = c.New,
                    UserId = user.Id,
                    UserName = user.DisplayName,
                    ChangedAt = now - save.Ago,
                }));
            // Saved one by one, so the ids follow the order of the changes, as the history page expects.
            await db.SaveChangesAsync();
        }

        if (apiEnabled)
            await services.GetRequiredService<ApiKeyService>().RegisterAsync("demo-app", demo.ApiKey, admin.Id);

        app.Logger.LogInformation("Seeded the demo with {Count} translations.", translations.Count);
    }

    private static async Task<User> CreateUserAsync(UserService users, string username, string displayName)
    {
        // Nobody signs in as them, so the password is random and never shown.
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        var (user, errors) = await users.CreateLocalAsync(username, displayName, password, UserRole.Editor);
        return user ?? throw new InvalidOperationException("Could not create a demo user: " + string.Join(" ", errors));
    }
}
