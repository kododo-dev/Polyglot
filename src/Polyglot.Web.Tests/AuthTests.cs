using System.Net;
using System.Net.Http.Json;
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

        var token = await GetTokenAsync(client, "/account");
        await client.PostAsync("/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallEditorApiAsync(client)).StatusCode);
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
        var users = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Equal("/forbidden", users.Headers.Location?.AbsolutePath);
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

        var token = await GetTokenAsync(admin, "/admin/users");
        var create = await admin.PostAsync("/admin/users?handler=Create", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.Username"] = "newbie",
                ["Input.Password"] = "newbie-password-1",
                ["Input.Role"] = nameof(UserRole.Editor),
                ["__RequestVerificationToken"] = token,
            }));

        var newbie = NewClient(factory);
        await LoginAsync(newbie, "newbie", "newbie-password-1");

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CallEditorApiAsync(newbie)).StatusCode);
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

        async Task<HttpResponseMessage> ChangeAsync(string current, string next)
        {
            var token = await GetTokenAsync(client, "/account");
            return await client.PostAsync("/account", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.CurrentPassword"] = current,
                ["Input.NewPassword"] = next,
                ["__RequestVerificationToken"] = token,
            }));
        }

        var wrong = await ChangeAsync("wrong-current", "a-brand-new-password");
        Assert.Contains("current password is incorrect", await wrong.Content.ReadAsStringAsync());

        var ok = await ChangeAsync(AdminPassword, "a-brand-new-password");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

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
