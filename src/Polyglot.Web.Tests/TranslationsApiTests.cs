using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kododo.CultureWay.Core.Model;
using Kododo.CultureWay.Core.Store;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TranslationsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    // Cultures en, pl, de with pl as the default, so that de falls back to pl.
    private async Task<(HttpClient Client, IStore Store)> CreateClientAsync(
        Translation[] translations,
        (string Key, string Value)[]? settings = null,
        IReadOnlySource? readOnlySource = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", "bootstrap-password-1");
            b.UseSetting("Polyglot:Cultures", "en, pl, de");
            b.UseSetting("Polyglot:DefaultCulture", "pl");
            foreach (var (key, value) in settings ?? [])
                b.UseSetting(key, value);
            if (readOnlySource is not null)
                b.ConfigureTestServices(s => s.AddSingleton(readOnlySource));
        });
        _factories.Add(factory);

        var store = factory.Services.GetRequiredService<IStore>();
        if (translations.Length > 0)
            await store.SetAsync(translations);

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, token, _) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync("consumer");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, token);
        return (client, store);
    }

    private static async Task<Dictionary<string, string>> GetTranslationsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>())!;
    }

    private static readonly Translation[] Shop =
    [
        new("Checkout.Pay", "pl", "Zapłać"),
        new("Checkout.Summary.Total", "pl", "Razem"),
        new("CheckoutExtra.Note", "pl", "Uwaga"),
        new("Common.Cancel", "pl", "Anuluj"),
        new("Admin.Users.Title", "pl", "Użytkownicy"),
        new("Greeting", "pl", "Cześć"),
    ];

    [Fact]
    public async Task Culture_IsTheBareSortedMapOfFullKeys()
    {
        var (client, _) = await CreateClientAsync(Shop);

        var response = await client.GetAsync("/api/v1/translations/pl");
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["Admin.Users.Title", "Checkout.Pay", "Checkout.Summary.Total", "CheckoutExtra.Note", "Common.Cancel", "Greeting"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Zapłać", body.GetProperty("Checkout.Pay").GetString());
        Assert.Matches("^\"[0-9a-f]{16}\"$", response.Headers.ETag?.ToString());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task MissingKeys_FallBackAlongParentsThenTheDefaultCulture()
    {
        var (client, _) = await CreateClientAsync(
        [
            new("Greeting", "pl", "Cześć"),
            new("Farewell", "pl", "Pa"),
            new("Greeting", "de", "Hallo"),
            new("Greeting", "en", "Hello"),
            new("Farewell", "en", "Bye"),
        ]);

        var de = await GetTranslationsAsync(client, "/api/v1/translations/de");
        var deAt = await GetTranslationsAsync(client, "/api/v1/translations/de-AT");

        Assert.Equal(new Dictionary<string, string> { ["Farewell"] = "Pa", ["Greeting"] = "Hallo" }, de);
        Assert.Equal(de, deAt);
    }

    [Fact]
    public async Task RegionalCulture_OfTheDefaultCulture_IsServed()
    {
        var (client, _) = await CreateClientAsync([new("Greeting", "pl", "Cześć")]);

        Assert.Equal("Cześć", (await GetTranslationsAsync(client, "/api/v1/translations/pl-PL"))["Greeting"]);
    }

    [Fact]
    public async Task Namespaces_SelectTheirSubtreesAndTheUnion()
    {
        var (client, _) = await CreateClientAsync(Shop);

        var body = await GetTranslationsAsync(client, "/api/v1/translations/pl?namespace=Checkout&namespace=Common");

        // Not CheckoutExtra.Note: Checkout is a namespace, not a string prefix.
        Assert.Equal(["Checkout.Pay", "Checkout.Summary.Total", "Common.Cancel"], body.Keys);
    }

    [Fact]
    public async Task NamespaceWithoutKeys_IsAnEmptyMap()
    {
        var (client, _) = await CreateClientAsync(Shop);

        Assert.Empty(await GetTranslationsAsync(client, "/api/v1/translations/pl?namespace=Nowhere"));
    }

    [Theory]
    [InlineData("namespace=")]
    [InlineData("namespace=.Checkout")]
    [InlineData("namespace=Checkout.")]
    [InlineData("namespace=A..B")]
    [InlineData("namespace=Checkout&namespace=")]
    public async Task MalformedNamespace_IsABadRequest(string query)
    {
        var (client, _) = await CreateClientAsync(Shop);

        var response = await client.GetAsync($"/api/v1/translations/pl?{query}");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiProblems.InvalidRequest, problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnsupportedCulture_IsNotFound_RatherThanTheDefaultCulture()
    {
        var (client, _) = await CreateClientAsync(Shop);

        var response = await client.GetAsync("/api/v1/translations/fr");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiProblems.NotFound, problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MalformedCulture_IsABadRequest()
    {
        var (client, _) = await CreateClientAsync(Shop);

        var response = await client.GetAsync("/api/v1/translations/not%20a%20culture!");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblems.InvalidRequest, problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task KnownVersion_IsAnsweredWithAnEmptyNotModified()
    {
        var (client, _) = await CreateClientAsync(Shop);

        var first = await client.GetAsync("/api/v1/translations/pl?namespace=Checkout");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/translations/pl?namespace=Checkout");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Versions_ChangeOnlyForResponsesThatChange()
    {
        var (client, store) = await CreateClientAsync(
            [.. Shop, new("Greeting", "de", "Hallo")],
            [("Polyglot:Api:SnapshotCacheSeconds", "0")]);

        async Task<string> ETag(string url) => (await client.GetAsync(url)).Headers.ETag!.ToString();

        var de = await ETag("/api/v1/translations/de");
        var checkout = await ETag("/api/v1/translations/pl?namespace=Checkout");
        var admin = await ETag("/api/v1/translations/pl?namespace=Admin");

        await store.SetAsync([new Translation("Admin.Users.Title", "pl", "Konta")]);

        Assert.Equal(checkout, await ETag("/api/v1/translations/pl?namespace=Checkout"));
        Assert.NotEqual(admin, await ETag("/api/v1/translations/pl?namespace=Admin"));
        // de falls back to pl for Admin.Users.Title, so its response really changed.
        Assert.NotEqual(de, await ETag("/api/v1/translations/de"));
    }

    [Fact]
    public async Task Snapshot_IsReusedForItsLifetime()
    {
        var (client, store) = await CreateClientAsync(Shop, [("Polyglot:Api:SnapshotCacheSeconds", "3600")]);

        var before = await GetTranslationsAsync(client, "/api/v1/translations/pl");
        await store.SetAsync([new Translation("Checkout.Pay", "pl", "Płacę")]);
        var after = await GetTranslationsAsync(client, "/api/v1/translations/pl");

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ReadOnlyDefaults_AreServed_AndTheStoreWins()
    {
        var (client, _) = await CreateClientAsync(
            [new("Greeting", "pl", "Cześć")],
            readOnlySource: new Defaults(
            [
                new("Greeting", "pl", "Dzień dobry"),
                new("Farewell", "pl", "Do widzenia"),
            ]));

        var body = await GetTranslationsAsync(client, "/api/v1/translations/pl");

        Assert.Equal(new Dictionary<string, string> { ["Farewell"] = "Do widzenia", ["Greeting"] = "Cześć" }, body);
    }

    private sealed class Defaults(Translation[] translations) : IReadOnlySource
    {
        public Task<IReadOnlyList<Translation>> GetDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Translation>>(translations);
    }
}
