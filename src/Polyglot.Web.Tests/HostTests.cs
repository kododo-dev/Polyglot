using System.Net;
using System.Net.Http.Json;
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
    public async Task Root_RedirectsToEditor()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/translations/", response.Headers.Location?.OriginalString);
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
