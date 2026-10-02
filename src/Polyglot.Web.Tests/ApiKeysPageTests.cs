using System.Net;
using System.Text.RegularExpressions;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class ApiKeysPageTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    [GeneratedRegex(@"pg_[a-z0-9]{8}_[A-Za-z0-9]{43}")]
    private static partial Regex ApiTokenPattern();

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
        });
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
        => AntiforgeryPattern().Match(await client.GetStringAsync(url)).Groups[1].Value;

    private static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> factory, string username = "admin", string password = AdminPassword)
    {
        var client = NewClient(factory);
        var token = await AntiforgeryAsync(client, "/login");
        var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string handler, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await AntiforgeryAsync(client, "/admin/api-keys");
        return await client.PostAsync($"/admin/api-keys?handler={handler}", new FormUrlEncodedContent(fields));
    }

    private static async Task<ApiKey> SingleKeyAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync()).Single();
    }

    private static async Task<HttpStatusCode> CallApiAsync(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, token);
        return (await client.GetAsync("/api/v1/cultures")).StatusCode;
    }

    [Fact]
    public async Task CreatedKey_IsShownOnce_AndWorks()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsync(factory);

        var created = await PostAsync(admin, "Create", new() { ["Input.Name"] = "shop-frontend-prod" });
        var html = await created.Content.ReadAsStringAsync();
        var token = ApiTokenPattern().Match(html).Value;

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString());
        Assert.NotEmpty(token);
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(factory, token));

        // Afterwards the list shows the public identifier only.
        var list = await admin.GetStringAsync("/admin/api-keys");
        var key = await SingleKeyAsync(factory);
        Assert.DoesNotMatch(ApiTokenPattern(), list);
        Assert.Contains($"pg_{key.KeyId}_", list);
        Assert.Contains("shop-frontend-prod", list);
    }

    [Fact]
    public async Task CreatedKey_RecordsItsIssuerAndExpiry()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsync(factory);
        var validThrough = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        await PostAsync(admin, "Create", new()
        {
            ["Input.Name"] = "expiring",
            ["Input.ValidThrough"] = validThrough.ToString("yyyy-MM-dd"),
        });

        var key = await SingleKeyAsync(factory);
        Assert.NotNull(key.CreatedByUserId);
        // Valid through the whole chosen day.
        Assert.Equal(
            new DateTimeOffset(validThrough.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            key.ExpiresAt);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("expired", "2020-01-01")]
    public async Task InvalidInput_IsReported_AndCreatesNothing(string name, string? validThrough)
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsync(factory);

        var fields = new Dictionary<string, string> { ["Input.Name"] = name };
        if (validThrough is not null)
            fields["Input.ValidThrough"] = validThrough;
        var response = await PostAsync(admin, "Create", fields);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"error\"", html);
        Assert.DoesNotMatch(ApiTokenPattern(), html);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync());
    }

    [Fact]
    public async Task DisablingAndDeleting_RevokeTheKey()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsync(factory);
        var created = await PostAsync(admin, "Create", new() { ["Input.Name"] = "consumer" });
        var token = ApiTokenPattern().Match(await created.Content.ReadAsStringAsync()).Value;
        var id = (await SingleKeyAsync(factory)).Id.ToString();

        var disabled = await PostAsync(admin, "Disabled", new() { ["id"] = id, ["disabled"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync(factory, token));

        await PostAsync(admin, "Disabled", new() { ["id"] = id, ["disabled"] = "false" });
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(factory, token));

        var deleted = await PostAsync(admin, "Delete", new() { ["id"] = id });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync(factory, token));
    }

    [Fact]
    public async Task Editor_CannotManageKeys()
    {
        var factory = await CreateAppAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<UserService>()
                .CreateLocalAsync("erin", null, "erin-password-1", UserRole.Editor);
        var editor = await SignInAsync(factory, "erin", "erin-password-1");

        var page = await editor.GetAsync("/admin/api-keys");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/forbidden", page.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task ApiKey_CannotOpenThePage()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsync(factory);
        var created = await PostAsync(admin, "Create", new() { ["Input.Name"] = "consumer" });
        var token = ApiTokenPattern().Match(await created.Content.ReadAsStringAsync()).Value;

        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, token);
        var page = await client.GetAsync("/admin/api-keys");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login", page.Headers.Location?.AbsolutePath);
    }
}
