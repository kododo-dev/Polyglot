using System.Net;
using System.Text.RegularExpressions;
using Kododo.CultureWay.Core.Store;
using Kododo.CultureWay.UI;
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
        var panel = await client.GetStringAsync("/");
        var overview = await WayClient.GetAsync(client, "overview", "Overview");

        // The notice is the shell's, above every page.
        Assert.Contains(new DemoOptions().Notice, panel);
        Assert.Equal(DemoOptions.DefaultApiKey, overview.GetProperty("demoKey").GetProperty("token").GetString());
        Assert.Contains($"\"value\":\"{DemoOptions.DefaultApiKey}\"", await NewClient(factory).GetStringAsync("/api-reference/"));
    }

    [Fact]
    public async Task UsersAndPassword_CannotBeChanged()
    {
        var factory = await CreateAppAsync();
        var client = await TryTheDemoAsync(factory);
        var before = await CountsAsync(factory);
        var anna = (await WayClient.GetAsync(client, "users", "Users")).GetProperty("users").EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == "anna").GetProperty("id").GetGuid();

        var refused = new[]
        {
            await WayClient.PostAsync(client, "users", "CreateUser", new { username = "intruder", password = "intruder-password-1", role = "Admin" }),
            await WayClient.PostAsync(client, "users", "SetRole", new { id = anna, role = "None" }),
            await WayClient.PostAsync(client, "users", "SetDisabled", new { id = anna, disabled = true }),
            await WayClient.PostAsync(client, "users", "ResetPassword", new { id = anna, newPassword = "taken-over-password" }),
            await WayClient.PostAsync(client, "account", "ChangePassword", new { currentPassword = "polyglot-demo", newPassword = "taken-over-password" }),
        };

        Assert.All(refused, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(before, await CountsAsync(factory));
        await TryTheDemoAsync(factory); // the published password still works

        // The pages say so, and the users can still be looked at.
        var users = await WayClient.GetAsync(client, "users", "Users");
        Assert.True(users.GetProperty("readOnly").GetBoolean());
        Assert.Contains(users.GetProperty("users").EnumerateArray(), u => u.GetProperty("username").GetString() == "anna");
        Assert.True((await WayClient.GetAsync(client, "account", "Account")).GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public async Task DemoKey_CannotBeDisabledOrDeleted()
    {
        var factory = await CreateAppAsync();
        var client = await TryTheDemoAsync(factory);
        ApiKey demoKey;
        await using (var scope = factory.Services.CreateAsyncScope())
            demoKey = (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync()).Single();

        var delete = await WayClient.PostAsync(client, "api-keys", "DeleteKey", new { id = demoKey.Id });
        var disable = await WayClient.PostAsync(client, "api-keys", "SetKeyDisabled", new { id = demoKey.Id, disabled = true });
        var create = await WayClient.PostAsync(client, "api-keys", "CreateKey", new { name = "my-app" });

        Assert.Contains("cannot be disabled or deleted", Assert.Single(await WayClient.ErrorsAsync(delete)));
        Assert.Contains("cannot be disabled or deleted", Assert.Single(await WayClient.ErrorsAsync(disable)));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var listed = (await WayClient.GetAsync(client, "api-keys", "ApiKeys")).GetProperty("keys").EnumerateArray().ToList();
        Assert.True(listed.Single(k => k.GetProperty("id").GetGuid() == demoKey.Id).GetProperty("isDemo").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync();
            Assert.Equal(2, keys.Count);
            Assert.False(keys.Single(k => k.Id == demoKey.Id).IsDisabled);
        }
    }

    [Fact]
    public async Task Import_IsLimitedToSmallFiles()
    {
        var demo = await TryTheDemoAsync(await CreateAppAsync());
        var regular = await CreateAppAsync(demo: false);

        Assert.Equal(EditorIntegration.DemoMaxImportBytes, await MaxImportBytesAsync(demo));
        Assert.Equal(new EditorOptions().MaxImportBytes, regular.Services.GetRequiredService<EditorOptions>().MaxImportBytes);

        static async Task<long> MaxImportBytesAsync(HttpClient client)
        {
            var response = await client.PostAsync("/translations/api/GetEditorSettings",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            using var settings = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return settings.RootElement.GetProperty("maxImportBytes").GetInt64();
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
