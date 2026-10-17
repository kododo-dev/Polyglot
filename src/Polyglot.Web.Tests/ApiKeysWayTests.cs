using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Kododo.Polyglot.Web.Tests.WayClient;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ApiKeysWayTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";
    private const string Way = "api-keys";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

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

    private static Task<HttpClient> SignInAsAdminAsync(WebApplicationFactory<Program> factory)
        => SignInAsync(factory, "admin", AdminPassword);

    private static async Task<string> CreateKeyAsync(HttpClient admin, string name)
        => (await GetAsync(admin, Way, "CreateKey", new { name })).GetProperty("token").GetString()!;

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
    public async Task CreatedKey_IsReturnedOnce_AndWorks()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsAdminAsync(factory);

        var created = await PostAsync(admin, Way, "CreateKey", new { name = "shop-frontend-prod" });
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString());
        Assert.Matches(@"^pg_[a-z0-9]{8}_[A-Za-z0-9]{43}$", token);
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(factory, token));

        // Afterwards the list shows the public identifier only.
        var list = await GetAsync(admin, Way, "ApiKeys");
        var key = await SingleKeyAsync(factory);
        Assert.DoesNotContain(token, list.GetRawText());
        var listed = Assert.Single(list.GetProperty("keys").EnumerateArray());
        Assert.Equal($"pg_{key.KeyId}_…", listed.GetProperty("prefix").GetString());
        Assert.Equal("shop-frontend-prod", listed.GetProperty("name").GetString());
        Assert.Equal("active", listed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreatedKey_RecordsItsIssuerAndExpiry()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsAdminAsync(factory);
        var validThrough = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        await GetAsync(admin, Way, "CreateKey", new { name = "expiring", validThrough = validThrough.ToString("yyyy-MM-dd") });

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
        var admin = await SignInAsAdminAsync(factory);

        var errors = await ErrorsAsync(await PostAsync(admin, Way, "CreateKey", new { name, validThrough }));

        Assert.NotEmpty(errors);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync());
    }

    [Fact]
    public async Task DisablingAndDeleting_RevokeTheKey()
    {
        var factory = await CreateAppAsync();
        var admin = await SignInAsAdminAsync(factory);
        var token = await CreateKeyAsync(admin, "consumer");
        var id = (await SingleKeyAsync(factory)).Id;

        var disabled = await PostAsync(admin, Way, "SetKeyDisabled", new { id, disabled = true });
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync(factory, token));

        await PostAsync(admin, Way, "SetKeyDisabled", new { id, disabled = false });
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(factory, token));

        var deleted = await PostAsync(admin, Way, "DeleteKey", new { id });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
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

        var page = await editor.GetAsync($"/{Way}/");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/forbidden", page.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(editor, Way, "ApiKeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(editor, Way, "CreateKey", new { name = "sneaky" })).StatusCode);
    }

    [Fact]
    public async Task ApiKey_CannotUseThePage()
    {
        var factory = await CreateAppAsync();
        var token = await CreateKeyAsync(await SignInAsAdminAsync(factory), "consumer");

        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, token);
        var page = await client.GetAsync($"/{Way}/");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login", page.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, Way, "ApiKeys")).StatusCode);
    }
}
