using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DeliveryApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

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
            b.UseSetting("Polyglot:Cultures", "en, pl, de");
            b.UseSetting("Polyglot:DefaultCulture", "pl");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });
        _factories.Add(factory);
        return factory;
    }

    private static async Task<string> IssueKeyAsync(WebApplicationFactory<Program> factory, string name = "consumer")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (key, token, errors) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(name);
        Assert.True(key is not null, string.Join(" ", errors));
        return token;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory, string? token = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null)
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, token);
        return client;
    }

    [Fact]
    public async Task ValidKey_GetsTheCulturesWithAnETag()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory, await IssueKeyAsync(factory));

        var response = await client.GetAsync("/api/v1/cultures");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pl", body.GetProperty("defaultCulture").GetString());
        Assert.Equal(["de", "en", "pl"], body.GetProperty("cultures").EnumerateArray().Select(c => c.GetString()));

        Assert.False(body.TryGetProperty("version", out _));
        Assert.Matches("^\"[0-9a-f]{16}\"$", response.Headers.ETag?.ToString());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task KnownVersion_IsAnsweredWithAnEmptyNotModified()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory, await IssueKeyAsync(factory));

        var first = await client.GetAsync("/api/v1/cultures");
        var etag = first.Headers.ETag!;

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cultures");
        request.Headers.IfNoneMatch.Add(etag);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(etag.ToString(), second.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task StaleVersion_IsAnsweredWithTheCurrentPayload()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory, await IssueKeyAsync(factory));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cultures");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0123456789abcdef\""));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingKey_IsUnauthorizedAsProblemJson()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory);

        var response = await client.GetAsync("/api/v1/cultures");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiProblems.Unauthorized, problem.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("pg_abcdefgh_0123456789012345678901234567890123456")]
    public async Task UnusableKey_IsUnauthorized(string token)
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory, token);

        var response = await client.GetAsync("/api/v1/cultures");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisabledKey_LosesAccessImmediately()
    {
        var factory = await CreateAppAsync();
        var token = await IssueKeyAsync(factory);
        var client = NewClient(factory, token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cultures")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            var key = (await keys.ListAsync()).Single();
            Assert.Empty(await keys.SetDisabledAsync(key.Id, true));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task SignedInUser_CannotUseTheDeliveryApi()
    {
        var factory = await CreateAppAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var token = await AntiforgeryToken(client, "/login");
        var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin",
            ["Password"] = AdminPassword,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // The cookie opens the editor, but the delivery API only accepts its own scheme.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/translations/api/GetCultures", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task ApiKey_CannotUseTheEditorOrTheAdminPages()
    {
        var factory = await CreateAppAsync();
        var client = NewClient(factory, await IssueKeyAsync(factory));

        var editor = await client.PostAsJsonAsync("/translations/api/GetCultures", new { });
        var admin = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, editor.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.Equal("/login", admin.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task RequestsOverTheLimit_AreRejectedWithRetryAfter()
    {
        var factory = await CreateAppAsync(("Polyglot:Api:RequestsPerMinute", "2"));
        var client = NewClient(factory, await IssueKeyAsync(factory));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cultures")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cultures")).StatusCode);

        var rejected = await client.GetAsync("/api/v1/cultures");
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(ApiProblems.RateLimited, problem.GetProperty("type").GetString());
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task TheLimitIsPerKey_NotPerInstance()
    {
        var factory = await CreateAppAsync(("Polyglot:Api:RequestsPerMinute", "1"));
        var first = NewClient(factory, await IssueKeyAsync(factory, "first"));
        var second = NewClient(factory, await IssueKeyAsync(factory, "second"));

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/v1/cultures")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("/api/v1/cultures")).StatusCode);

        // The second key still has its own budget.
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task InvalidKeys_AreLimitedToo()
    {
        var factory = await CreateAppAsync(("Polyglot:Api:RequestsPerMinute", "1"));
        var client = NewClient(factory, "pg_abcdefgh_0123456789012345678901234567890123456");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cultures")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task DisabledApi_IsNotMapped()
    {
        var factory = await CreateAppAsync(("Polyglot:Api:Enabled", "false"));
        var client = NewClient(factory, await IssueKeyAsync(factory));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/cultures")).StatusCode);
    }

    [Fact]
    public async Task WithoutAuthentication_TheApiIsNotMapped()
    {
        var factory = await CreateAppAsync(("Polyglot:Auth:Enabled", "false"));
        var client = NewClient(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/cultures")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task CultureAddedInTheEditor_ShowsUpAndChangesTheVersion()
    {
        var factory = await CreateAppAsync();
        var apiClient = NewClient(factory, await IssueKeyAsync(factory));

        var before = await apiClient.GetAsync("/api/v1/cultures");

        var editor = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var token = await AntiforgeryToken(editor, "/login");
        await editor.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin",
            ["Password"] = AdminPassword,
            ["__RequestVerificationToken"] = token,
        }));
        // StringContent rather than PostAsJsonAsync: the editor's API rejects a request whose
        // Content-Length is unknown, and JsonContent streams chunked.
        var added = await editor.PostAsync(
            "/translations/api/AddCulture",
            new StringContent("""{"culture":"fr"}""", Encoding.UTF8, "application/json"));
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());

        var after = await apiClient.GetAsync("/api/v1/cultures");
        var afterBody = await after.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(["de", "en", "fr", "pl"], afterBody.GetProperty("cultures").EnumerateArray().Select(c => c.GetString()));
        Assert.NotEqual(before.Headers.ETag?.ToString(), after.Headers.ETag?.ToString());
    }

    private static async Task<string> AntiforgeryToken(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = System.Text.RegularExpressions.Regex.Match(
            html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Groups[1].Value;
    }
}
