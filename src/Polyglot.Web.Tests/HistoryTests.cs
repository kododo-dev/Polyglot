using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kododo.CultureWay.Core.Model;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class HistoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";
    private const string EditorPassword = "editor-password-1";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    private async Task<WebApplicationFactory<Program>> CreateAppAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", AdminPassword);
            b.UseSetting("Polyglot:Cultures", "en,pl");
        });
        _factories.Add(factory);
        return factory;
    }

    private static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> factory, string username = "admin", string password = AdminPassword)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = await AntiforgeryAsync(client, "/login"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static async Task CreateEditorAsync(WebApplicationFactory<Program> factory, string username, string displayName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (user, errors) = await scope.ServiceProvider.GetRequiredService<UserService>()
            .CreateLocalAsync(username, displayName, EditorPassword, UserRole.Editor);
        Assert.True(user is not null, string.Join(" ", errors));
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
        => AntiforgeryPattern().Match(await client.GetStringAsync(url)).Groups[1].Value;

    // What the editor sends on save: every translation it holds, plus the keys to delete.
    // StringContent, because the editor's API needs a Content-Length.
    private static async Task SaveInEditorAsync(
        HttpClient client, (string Key, string Culture, string Value)[] translations, (string Key, string Culture)[]? delete = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            translations = translations.Select(t => new
            {
                key = t.Key, culture = t.Culture, value = t.Value, hasExternalDefault = false, externalDefaultValue = (string?)null,
            }),
            keysToDelete = (delete ?? []).Select(k => new { key = k.Key, culture = k.Culture }),
        });
        var response = await client.PostAsync(
            "/translations/api/UpdateTranslations", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<TranslationChange>> ChangesAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .TranslationChanges.OrderBy(c => c.Id).ToListAsync();
    }

    private static async Task<string?> StoredValueAsync(WebApplicationFactory<Program> factory, string key, string culture)
        => (await factory.Services.GetRequiredService<IStore>().GetAllAsync())
            .SingleOrDefault(t => t.Key == key && t.Culture == culture)?.Value;

    private static async Task<HttpResponseMessage> UndoAsync(HttpClient client, long id)
        => await client.PostAsync("/history?handler=Undo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = id.ToString(),
            ["__RequestVerificationToken"] = await AntiforgeryAsync(client, "/history"),
        }));

    [Fact]
    public async Task A_save_in_the_editor_records_only_the_values_that_changed()
    {
        var factory = await CreateAppAsync();
        await CreateEditorAsync(factory, "anna", "Anna Nowak");
        var client = await SignInAsync(factory, "anna", EditorPassword);

        await SaveInEditorAsync(client, [("Checkout.Pay", "en", "Pay"), ("Checkout.Pay", "pl", "Zapłać")]);
        // The editor sends everything again; only the Polish value differs.
        await SaveInEditorAsync(client, [("Checkout.Pay", "en", "Pay"), ("Checkout.Pay", "pl", "Zapłać teraz")]);

        var changes = await ChangesAsync(factory);
        Assert.Equal(3, changes.Count);

        Assert.All(changes.Take(2), c => Assert.Null(c.OldValue));
        Assert.Single(changes.Take(2).Select(c => c.ChangeSetId).Distinct());

        var edit = changes[2];
        Assert.Equal(("Checkout.Pay", "pl", "Zapłać", "Zapłać teraz"), (edit.Key, edit.Culture, edit.OldValue, edit.NewValue));
        Assert.Equal("Anna Nowak", edit.UserName);
        Assert.NotNull(edit.UserId);
        Assert.NotEqual(changes[0].ChangeSetId, edit.ChangeSetId);
    }

    [Fact]
    public async Task Deleting_a_key_records_the_removed_values_only()
    {
        var factory = await CreateAppAsync();
        var client = await SignInAsync(factory);
        await SaveInEditorAsync(client, [("Old.Key", "en", "Old")]);

        // The editor asks to delete the key in every language, including ones it never had.
        await SaveInEditorAsync(client, [], [("Old.Key", "en"), ("Old.Key", "pl")]);

        var removal = (await ChangesAsync(factory)).Last();
        Assert.Equal(("Old.Key", "en", "Old", (string?)null), (removal.Key, removal.Culture, removal.OldValue, removal.NewValue));
        Assert.Equal(2, (await ChangesAsync(factory)).Count);
    }

    [Fact]
    public async Task The_history_page_lists_changes_and_filters_them()
    {
        var factory = await CreateAppAsync();
        var client = await SignInAsync(factory);
        await SaveInEditorAsync(client, [("Checkout.Pay", "pl", "Zapłać"), ("Common.Cancel", "en", "Cancel")]);

        var all = await client.GetStringAsync("/history");
        Assert.Contains("Checkout.Pay", all);
        Assert.Contains("Common.Cancel", all);

        var byKey = await client.GetStringAsync("/history?key=checkout");
        Assert.Contains("Checkout.Pay", byKey);
        Assert.DoesNotContain("Common.Cancel", byKey);

        var byCulture = await client.GetStringAsync("/history?culture=en");
        Assert.DoesNotContain("Checkout.Pay", byCulture);
        Assert.Contains("Common.Cancel", byCulture);
    }

    [Fact]
    public async Task Undo_restores_the_old_value_and_is_recorded()
    {
        var factory = await CreateAppAsync();
        var client = await SignInAsync(factory);
        await SaveInEditorAsync(client, [("Checkout.Pay", "pl", "Zapłać")]);
        await SaveInEditorAsync(client, [("Checkout.Pay", "pl", "Płacę")]);
        var edit = (await ChangesAsync(factory)).Last();

        var response = await UndoAsync(client, edit.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Zapłać", await StoredValueAsync(factory, "Checkout.Pay", "pl"));
        var undo = (await ChangesAsync(factory)).Last();
        Assert.Equal(("Płacę", "Zapłać"), (undo.OldValue, undo.NewValue));
    }

    [Fact]
    public async Task Undoing_an_added_translation_removes_it()
    {
        var factory = await CreateAppAsync();
        var client = await SignInAsync(factory);
        await SaveInEditorAsync(client, [("New.Key", "en", "New")]);

        await UndoAsync(client, (await ChangesAsync(factory)).Single().Id);

        Assert.Null(await StoredValueAsync(factory, "New.Key", "en"));
    }

    [Fact]
    public async Task Undo_refuses_a_language_that_was_removed()
    {
        var factory = await CreateAppAsync();
        var client = await SignInAsync(factory);
        await SaveInEditorAsync(client, [("Checkout.Pay", "pl", "Zapłać")]);
        var added = (await ChangesAsync(factory)).Single();
        var removed = await client.PostAsync("/translations/api/DeleteCulture",
            new StringContent("""{"culture":"pl"}""", Encoding.UTF8, "application/json"));
        Assert.True(removed.IsSuccessStatusCode);

        var response = await UndoAsync(client, added.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no longer in Polyglot", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changes_outside_a_request_are_attributed_to_the_system()
    {
        var factory = await CreateAppAsync();

        await factory.Services.GetRequiredService<IStore>().SetAsync([new Translation("Seed.Key", "en", "Seed")]);

        var change = (await ChangesAsync(factory)).Single();
        Assert.Equal("System", change.UserName);
        Assert.Null(change.UserId);
    }

    [Fact]
    public async Task Editors_see_the_history_and_signed_out_visitors_do_not()
    {
        var factory = await CreateAppAsync();
        await CreateEditorAsync(factory, "anna", "Anna Nowak");

        var editor = await SignInAsync(factory, "anna", EditorPassword);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/history")).StatusCode);

        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await anonymous.GetAsync("/history");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login", page.Headers.Location?.AbsolutePath);
    }
}
