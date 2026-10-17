using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kododo.Polyglot.Web.Tests;

/// <summary>Signs in through the sign-in page and calls the API of Polyglot's Ways, as the shell's client does.</summary>
internal static partial class WayClient
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    public static HttpClient NewClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public static async Task<string> AntiforgeryAsync(HttpClient client, string url)
        => AntiforgeryPattern().Match(await client.GetStringAsync(url)).Groups[1].Value;

    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username, string password)
    {
        var client = NewClient(factory);
        var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = await AntiforgeryAsync(client, "/login"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    /// <summary><c>POST /{way}/api/{request}</c> with a JSON body.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string way, string request, object? body = null)
        => client.PostAsJsonAsync($"/{way}/api/{request}", body ?? new { });

    public static async Task<JsonElement> GetAsync(HttpClient client, string way, string request, object? body = null)
    {
        var response = await PostAsync(client, way, request, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The messages of a 400 the module shows as they are.</summary>
    public static async Task<string[]> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. problem.GetProperty("errors").EnumerateObject().SelectMany(e => e.Value.EnumerateArray()).Select(m => m.GetString()!)];
    }
}
