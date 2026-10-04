using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Set to <c>1</c> to rewrite the committed copy instead of failing when it is stale.</summary>
    private const string UpdateVariable = "POLYGLOT_UPDATE_OPENAPI";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    private async Task<HttpClient> CreateClientAsync(params (string Key, string Value)[] settings)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", "bootstrap-password-1");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });
        _factories.Add(factory);
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static string CommittedCopyPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Polyglot.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find the repository root.");
        return Path.Combine(dir.FullName, "docs", "openapi", "v1.json");
    }

    [Fact]
    public async Task CommittedCopy_MatchesTheServedDocument()
    {
        var client = await CreateClientAsync();
        var served = (await client.GetStringAsync("/openapi/v1.json")).ReplaceLineEndings("\n").TrimEnd() + "\n";
        var path = CommittedCopyPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, served);
            return;
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : "";
        Assert.True(
            committed == served,
            $"docs/openapi/v1.json is stale. Run the tests with {UpdateVariable}=1 to regenerate it, and commit the result.");
    }

    [Fact]
    public async Task Document_IsServedWithoutAKey_AsOpenApi30()
    {
        var client = await CreateClientAsync();

        var response = await client.GetAsync("/openapi/v1.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.0", document.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task Document_DescribesWhatGeneratedClientsNeed()
    {
        var client = await CreateClientAsync();
        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
        var paths = document.GetProperty("paths");

        // Only the delivery API; the editor's endpoints are not a public contract.
        Assert.Equal(
            ["/api/v1/cultures", "/api/v1/translations/{culture}"],
            paths.EnumerateObject().Select(p => p.Name).Order());

        var cultures = paths.GetProperty("/api/v1/cultures").GetProperty("get");
        var translations = paths.GetProperty("/api/v1/translations/{culture}").GetProperty("get");
        Assert.Equal("getCultures", cultures.GetProperty("operationId").GetString());
        Assert.Equal("getTranslations", translations.GetProperty("operationId").GetString());

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        Assert.True(translations.TryGetProperty("security", out _));

        var map = translations.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.Equal("string", map.GetProperty("additionalProperties").GetProperty("type").GetString());

        var ns = translations.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "namespace");
        Assert.Equal("array", ns.GetProperty("schema").GetProperty("type").GetString());

        foreach (var status in new[] { "304", "400", "401", "403", "404", "429" })
            Assert.True(translations.GetProperty("responses").TryGetProperty(status, out _), status);
    }

    [Fact]
    public async Task Document_IsAlsoServedAsYaml()
    {
        var client = await CreateClientAsync();

        var yaml = await client.GetStringAsync("/openapi/v1.yaml");

        Assert.StartsWith("openapi: 3.0", yaml);
    }

    [Fact]
    public async Task DisabledDocument_IsNotServed()
    {
        var client = await CreateClientAsync(("Polyglot:Api:OpenApi:Enabled", "false"));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
