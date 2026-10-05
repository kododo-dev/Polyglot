using System.Net;
using System.Text.RegularExpressions;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class DemoTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    private async Task<WebApplicationFactory<Program>> CreateAppAsync(string? connectionString = null, bool demo = true)
    {
        connectionString ??= await postgres.CreateDatabaseAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Cultures", "en,pl,de");
            b.UseSetting("Polyglot:Demo:Enabled", demo.ToString());
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", "bootstrap-password-1");
        });
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
        => AntiforgeryPattern().Match(await client.GetStringAsync(url)).Groups[1].Value;

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string page, string url, Dictionary<string, string>? fields = null)
    {
        fields ??= [];
        fields["__RequestVerificationToken"] = await AntiforgeryAsync(client, page);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpClient> TryTheDemoAsync(WebApplicationFactory<Program> factory)
    {
        var client = NewClient(factory);
        var response = await PostFormAsync(client, "/login", "/login?handler=Demo");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<(int Users, int Translations, int Changes, int Keys)> CountsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var translations = await scope.ServiceProvider.GetRequiredService<IStore>().GetAllAsync();
        return (await db.Users.CountAsync(), translations.Count, await db.TranslationChanges.CountAsync(), await db.ApiKeys.CountAsync());
    }

    [Fact]
    public async Task Seeds_UsersTranslationsHistoryAndKey()
    {
        var factory = await CreateAppAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var users = await services.GetRequiredService<UserService>().ListAsync();
        Assert.Equal(["anna", "demo", "former-intern", "lukas"], users.Select(u => u.Username).Order());
        Assert.Equal(UserRole.Admin, users.Single(u => u.Username == "demo").Role);
        Assert.True(users.Single(u => u.Username == "former-intern").IsDisabled);

        // 16 keys in English and Polish, three of them missing in German.
        var translations = await services.GetRequiredService<IStore>().GetAllAsync();
        Assert.Equal(16 + 16 + 13, translations.Count);

        // The newest change of each translation in the history ends at the value the store holds.
        var changes = await services.GetRequiredService<AppDbContext>().TranslationChanges.OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(10, changes.Count);
        Assert.DoesNotContain(changes, c => c.UserName == "System");
        foreach (var last in changes.GroupBy(c => (c.Key, c.Culture)).Select(g => g.Last()))
            Assert.Equal(last.NewValue, translations.Single(t => t.Key == last.Key && t.Culture == last.Culture).Value);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, DemoOptions.DefaultApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task Seeds_OnlyOnce()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var first = await CountsAsync(await CreateAppAsync(connectionString));

        var restarted = await CountsAsync(await CreateAppAsync(connectionString));

        Assert.Equal(first, restarted);
    }

    [Fact]
    public async Task TryTheDemo_SignsInAndShowsTheKey()
    {
        var factory = await CreateAppAsync();
        var login = await NewClient(factory).GetStringAsync("/login");
        Assert.Contains("Try the demo", login);
        Assert.Contains("polyglot-demo", login);

        var client = await TryTheDemoAsync(factory);
        var overview = await client.GetStringAsync("/");

        Assert.Contains("demo-banner", overview);
        Assert.Contains(DemoOptions.DefaultApiKey, overview);
    }

    [Fact]
    public async Task UsersAndPassword_CannotBeChanged()
    {
        var factory = await CreateAppAsync();
        var client = await TryTheDemoAsync(factory);
        var before = await CountsAsync(factory);

        var create = await PostFormAsync(client, "/admin/users", "/admin/users?handler=Create", new()
        {
            ["Input.Username"] = "intruder",
            ["Input.Password"] = "intruder-password-1",
            ["Input.Role"] = "Admin",
        });
        var password = await PostFormAsync(client, "/account", "/account", new()
        {
            ["Input.CurrentPassword"] = "polyglot-demo",
            ["Input.NewPassword"] = "taken-over-password",
        });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.Equal(before, await CountsAsync(factory));
        await TryTheDemoAsync(factory); // the published password still works

        // The page shows the users without a single form that would change them.
        var usersPage = await client.GetStringAsync("/admin/users");
        Assert.Contains("anna", usersPage);
        Assert.DoesNotContain("?handler=", usersPage);
    }

    [Fact]
    public async Task DemoKey_CannotBeDisabledOrDeleted()
    {
        var factory = await CreateAppAsync();
        var client = await TryTheDemoAsync(factory);
        ApiKey demoKey;
        await using (var scope = factory.Services.CreateAsyncScope())
            demoKey = (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync()).Single();

        var delete = await PostFormAsync(client, "/admin/api-keys", "/admin/api-keys?handler=Delete",
            new() { ["id"] = demoKey.Id.ToString() });
        var disable = await PostFormAsync(client, "/admin/api-keys", "/admin/api-keys?handler=Disabled",
            new() { ["id"] = demoKey.Id.ToString(), ["disabled"] = "true" });
        var create = await PostFormAsync(client, "/admin/api-keys", "/admin/api-keys?handler=Create",
            new() { ["Input.Name"] = "my-app" });

        Assert.Contains("cannot be disabled or deleted", await delete.Content.ReadAsStringAsync());
        Assert.Contains("cannot be disabled or deleted", await disable.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync();
            Assert.Equal(2, keys.Count);
            Assert.False(keys.Single(k => k.Id == demoKey.Id).IsDisabled);
        }
    }

    [Fact]
    public async Task WithoutDemo_NothingChanges()
    {
        var factory = await CreateAppAsync(demo: false);
        var client = NewClient(factory);

        Assert.DoesNotContain("Try the demo", await client.GetStringAsync("/login"));
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(client, "/login", "/login?handler=Demo")).StatusCode);
        Assert.Equal((1, 0, 0, 0), await CountsAsync(factory));
    }

    [Fact]
    public void Validate_NeedsLocalSignInAndValidSettings()
    {
        var auth = new AuthOptions();
        new DemoOptions().Validate(new AuthOptions { Enabled = false });
        new DemoOptions { Enabled = true }.Validate(auth);

        Assert.Throws<InvalidOperationException>(() => new DemoOptions { Enabled = true }.Validate(new AuthOptions { Enabled = false }));
        Assert.Throws<InvalidOperationException>(() => new DemoOptions { Enabled = true }.Validate(new AuthOptions { Local = { Enabled = false } }));
        Assert.Throws<InvalidOperationException>(() => new DemoOptions { Enabled = true, Password = "short" }.Validate(auth));
        Assert.Throws<InvalidOperationException>(() => new DemoOptions { Enabled = true, ApiKey = "pg_demo" }.Validate(auth));
    }

    [Fact]
    public void Apply_MakesTheDemoUserTheFirstAdministrator()
    {
        var auth = new AuthOptions();

        new DemoOptions { Enabled = true, Username = "visitor", Password = "visitor-password" }.Apply(auth);

        Assert.Equal("visitor", auth.Bootstrap.Username);
        Assert.Equal("visitor-password", auth.Bootstrap.Password);
    }
}
