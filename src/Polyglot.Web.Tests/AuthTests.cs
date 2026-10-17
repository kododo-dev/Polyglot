using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class AuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    private async Task<WebApplicationFactory<Program>> CreateAppAsync(params (string Key, string Value)[] settings)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", AdminPassword);
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string username, string password, string? returnUrl = null)
    {
        var token = await GetTokenAsync(client, "/login");
        var url = returnUrl is null ? "/login" : $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private static Task<HttpResponseMessage> CallEditorApiAsync(HttpClient client)
        => client.PostAsJsonAsync("/translations/api/GetCultures", new { });

    private static async Task<User> CreateUserAsync(
        WebApplicationFactory<Program> factory, string username, string password, UserRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var (user, errors) = await users.CreateLocalAsync(username, null, password, role);
        Assert.True(user is not null, string.Join(" ", errors));
        return user;
    }

    [Fact]
    public async Task Editor_RequiresSignIn()
    {
        var client = NewClient(await CreateAppAsync());

        var page = await client.GetAsync("/translations/");
        var api = await CallEditorApiAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login", page.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task BootstrapAdmin_CanSignInAndUseEditor()
    {
        var client = NewClient(await CreateAppAsync());

        var login = await LoginAsync(client, "admin", AdminPassword);
        var api = await CallEditorApiAsync(client);
        var home = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task UnderPathBase_CanSignInAndUseEditor()
    {
        // Routing added implicitly runs before the path base is removed and matches the full path: the pages still
        // loaded, but every POST under the prefix, the sign-in and the editor's API, got 405.
        var client = NewClient(await CreateAppAsync(("Polyglot:PathBase", "/polyglot")));

        var token = await GetTokenAsync(client, "/polyglot/login");
        var login = await client.PostAsync("/polyglot/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin",
            ["Password"] = AdminPassword,
            ["__RequestVerificationToken"] = token,
        }));
        var api = await client.PostAsJsonAsync("/polyglot/translations/api/GetCultures", new { });

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/polyglot/", login.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        // The panel at the instance's address, with and without the trailing slash (without it the path is empty).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/polyglot/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/polyglot")).StatusCode);
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        var client = NewClient(await CreateAppAsync());

        var login = await LoginAsync(client, "admin", "not-the-password");
        var api = await CallEditorApiAsync(client);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Invalid username or password.", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        var client = NewClient(await CreateAppAsync());
        await LoginAsync(client, "admin", AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        await client.PostAsync("/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallEditorApiAsync(client)).StatusCode);
        var panel = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, panel.StatusCode);
        Assert.Equal("/login", panel.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task SignOut_FromTheEditor_NeedsNoAntiforgeryToken()
    {
        var client = NewClient(await CreateAppAsync());
        await LoginAsync(client, "admin", AdminPassword);

        var logout = await client.PostAsync("/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallEditorApiAsync(client)).StatusCode);
    }

    private static async Task<JsonElement> EditorSettingsAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/translations/api/GetEditorSettings", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Panel_ForAdmin_ListsEveryPage_AndAllowsLanguageChanges()
    {
        var client = NewClient(await CreateAppAsync());
        await LoginAsync(client, "admin", AdminPassword);

        var shell = await ShellSettingsAsync(client);
        var settings = await EditorSettingsAsync(client);

        Assert.Equal("Polyglot", shell.GetProperty("title").GetString());
        Assert.Equal("panel", shell.GetProperty("mode").GetString());
        Assert.Equal(
            ["overview", "translations", "history", "users", "api-keys", "account"],
            shell.GetProperty("ways").EnumerateArray().Select(w => w.GetProperty("id").GetString()));
        // The account page opens from the user's name, not from the menu.
        Assert.Equal(
            ["account"],
            shell.GetProperty("ways").EnumerateArray().Where(w => w.GetProperty("hidden").GetBoolean()).Select(w => w.GetProperty("id").GetString()));
        Assert.Empty(shell.GetProperty("links").EnumerateArray());
        Assert.Equal("/#/account/", shell.GetProperty("user").GetProperty("accountUrl").GetString());
        Assert.Equal("/logout", shell.GetProperty("user").GetProperty("signOutUrl").GetString());
        Assert.True(settings.GetProperty("canManageCultures").GetBoolean());
    }

    /// <summary>What the panel's page hands to Way.Shell: the title, the pages the user may open and the user.</summary>
    private static async Task<JsonElement> ShellSettingsAsync(HttpClient client, string url = "/")
    {
        var html = await client.GetStringAsync(url);
        var json = Regex.Match(
            html, """<script type="application/json" id="way-shell-settings">(.*?)</script>""",
            RegexOptions.Singleline);
        Assert.True(json.Success, "The page has no shell settings.");
        return JsonDocument.Parse(json.Groups[1].Value).RootElement;
    }

    [Fact]
    public async Task EditorRole_CannotChangeLanguages()
    {
        var factory = await CreateAppAsync();
        await CreateUserAsync(factory, "erin", "erin-password-1", UserRole.Editor);
        var client = NewClient(factory);
        await LoginAsync(client, "erin", "erin-password-1");

        var shell = await ShellSettingsAsync(client);
        var settings = await EditorSettingsAsync(client);
        var add = await client.PostAsJsonAsync("/translations/api/AddCulture", new { culture = "fr" });

        // The editor, its history and the account page: no overview or admin pages.
        Assert.Equal(
            ["translations", "history", "account"],
            shell.GetProperty("ways").EnumerateArray().Select(w => w.GetProperty("id").GetString()));
        Assert.Equal("erin", shell.GetProperty("user").GetProperty("name").GetString());
        Assert.False(settings.GetProperty("canManageCultures").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
    }

    [Fact]
    public async Task LoginReturnUrl_MustBeLocal()
    {
        var client = NewClient(await CreateAppAsync());

        var login = await LoginAsync(client, "admin", AdminPassword, returnUrl: "https://evil.example/");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Editor_CanUseEditorButNotUserAdministration()
    {
        var factory = await CreateAppAsync();
        await CreateUserAsync(factory, "erin", "erin-password-1", UserRole.Editor);
        var client = NewClient(factory);

        await LoginAsync(client, "erin", "erin-password-1");
        var api = await CallEditorApiAsync(client);
        var users = await client.GetAsync("/users/");
        var usersApi = await WayClient.PostAsync(client, "users", "Users");

        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Equal("/forbidden", users.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Forbidden, usersApi.StatusCode);
    }

    [Fact]
    public async Task Panel_OpensTheEditorFirst_ForEditors()
    {
        var factory = await CreateAppAsync();
        await CreateUserAsync(factory, "erin", "erin-password-1", UserRole.Editor);
        var client = NewClient(factory);
        await LoginAsync(client, "erin", "erin-password-1");

        var shell = await ShellSettingsAsync(client);

        // The panel opens the first page of its menu; editors have one place to work, so no overview comes first.
        var first = shell.GetProperty("ways").EnumerateArray().First(w => !w.GetProperty("hidden").GetBoolean());
        Assert.Equal("translations", first.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("SetRole")]
    [InlineData("SetDisabled")]
    public async Task Admin_CannotChangeTheirOwnRoleOrDisableThemselves(string request)
    {
        var factory = await CreateAppAsync();
        // A second admin, so the last-admin rule is not what stops the change.
        await CreateUserAsync(factory, "ada", "ada-password-1", UserRole.Admin);
        var client = NewClient(factory);
        await LoginAsync(client, "admin", AdminPassword);
        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
            adminId = (await scope.ServiceProvider.GetRequiredService<UserService>().ListAsync())
                .Single(u => u.Username == "admin").Id;

        var response = await WayClient.PostAsync(client, "users", request, new { id = adminId, role = "Editor", disabled = true });

        Assert.Contains("Ask another administrator", Assert.Single(await WayClient.ErrorsAsync(response)));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<UserService>().FindByIdAsync(adminId);
            Assert.Equal(UserRole.Admin, admin!.Role);
            Assert.False(admin.IsDisabled);
        }
    }

    [Fact]
    public async Task Stylesheet_IsServedBeforeSignIn()
    {
        var client = NewClient(await CreateAppAsync());

        var css = await client.GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RoleChange_TakesEffectOnTheNextRequest()
    {
        var factory = await CreateAppAsync();
        var erin = await CreateUserAsync(factory, "erin", "erin-password-1", UserRole.Editor);
        var client = NewClient(factory);
        await LoginAsync(client, "erin", "erin-password-1");
        Assert.Equal(HttpStatusCode.OK, (await CallEditorApiAsync(client)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<UserService>().SetRoleAsync(erin.Id, UserRole.None);

        Assert.Equal(HttpStatusCode.Forbidden, (await CallEditorApiAsync(client)).StatusCode);
    }

    [Fact]
    public async Task DisabledUser_LosesAccessImmediately()
    {
        var factory = await CreateAppAsync();
        var erin = await CreateUserAsync(factory, "erin", "erin-password-1", UserRole.Editor);
        var client = NewClient(factory);
        await LoginAsync(client, "erin", "erin-password-1");
        Assert.Equal(HttpStatusCode.OK, (await CallEditorApiAsync(client)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<UserService>().SetDisabledAsync(erin.Id, true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallEditorApiAsync(client)).StatusCode);

        var relogin = await LoginAsync(client, "erin", "erin-password-1");
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateUsersThroughThePage()
    {
        var factory = await CreateAppAsync();
        var admin = NewClient(factory);
        await LoginAsync(admin, "admin", AdminPassword);

        var create = await WayClient.PostAsync(admin, "users", "CreateUser",
            new { username = "newbie", password = "newbie-password-1", role = nameof(UserRole.Editor) });

        var newbie = NewClient(factory);
        await LoginAsync(newbie, "newbie", "newbie-password-1");

        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CallEditorApiAsync(newbie)).StatusCode);
        var users = await WayClient.GetAsync(admin, "users", "Users");
        var listed = users.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("username").GetString() == "newbie");
        Assert.Equal("Editor", listed.GetProperty("role").GetString());
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDemotedOrDisabled()
    {
        var factory = await CreateAppAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var admin = (await users.ListAsync()).Single();

        Assert.NotEmpty(await users.SetRoleAsync(admin.Id, UserRole.Editor));
        Assert.NotEmpty(await users.SetDisabledAsync(admin.Id, true));

        await CreateUserAsync(factory, "second", "second-password-1", UserRole.Admin);
        Assert.Empty(await users.SetRoleAsync(admin.Id, UserRole.Editor));
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        var client = NewClient(await CreateAppAsync(("Polyglot:Auth:LoginAttemptsPerMinute", "2")));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await LoginAsync(client, "admin", "wrong-password")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task PasswordChange_RequiresCurrentPassword()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory);
        await LoginAsync(client, "admin", AdminPassword);

        Task<HttpResponseMessage> ChangeAsync(string current, string next)
            => WayClient.PostAsync(client, "account", "ChangePassword", new { currentPassword = current, newPassword = next });

        var wrong = await ChangeAsync("wrong-current", "a-brand-new-password");
        Assert.Contains("current password is incorrect", Assert.Single(await WayClient.ErrorsAsync(wrong)));

        var ok = await ChangeAsync(AdminPassword, "a-brand-new-password");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var fresh = NewClient(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(fresh, "admin", "a-brand-new-password")).StatusCode);
    }

    [Fact]
    public async Task ExternalUsers_AreProvisionedOnceAndKeepTheirRole()
    {
        var factory = await CreateAppAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var oidc = new OidcOptions { DefaultRole = UserRole.Editor };

        var first = await users.ProvisionExternalAsync("https://idp", "sub-1", "admin", "Ann", [], oidc);
        await users.SetRoleAsync(first.Id, UserRole.Admin);
        var again = await users.ProvisionExternalAsync("https://idp", "sub-1", "admin", "Ann", [], oidc);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(UserRole.Admin, again.Role);
        // "admin" is taken by the local bootstrap user, so the SSO user gets a distinct username.
        Assert.NotEqual("admin", first.Username);
        Assert.StartsWith("admin-", first.Username);
    }
}
