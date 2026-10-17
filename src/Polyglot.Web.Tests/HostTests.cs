using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Kododo.Polyglot.Web.Tests;

public class HostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed record CultureResponse(string Code, bool IsDefault);

    private readonly WebApplicationFactory<Program> _factory;

    public HostTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Polyglot:Auth:Enabled", "false")
            .UseSetting("Polyglot:Cultures", "en, pl")
            .UseSetting("Polyglot:DefaultCulture", "pl"));
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Root_IsThePanel_WithOnlyTheEditor()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Without sign-in there are no users, keys or history: the panel has the editor only.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"mode\":\"panel\"", html);
        Assert.Contains("\"id\":\"translations\"", html);
        Assert.DoesNotContain("\"id\":\"overview\"", html);
    }

    [Fact]
    public async Task Editor_ServesSpa()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/translations/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WithoutSignIn_TheEditorIsOpen_OutsideDevelopment()
    {
        // Way.Shell answers 403 outside Development until the host decides on the editor's authorization.
        var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var page = await client.GetAsync("/translations/");
        var api = await client.PostAsJsonAsync("/translations/api/GetCultures", new { });

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task Cultures_AreReadFromConfiguration()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/translations/api/GetCultures", new { });
        var cultures = await response.Content.ReadFromJsonAsync<CultureResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(cultures);
        Assert.Equal(["pl", "en"], cultures.Select(c => c.Code));
        Assert.Equal("pl", cultures.Single(c => c.IsDefault).Code);
    }
}
