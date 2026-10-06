using Kododo.CultureWay;
using Kododo.CultureWay.PostgreSQL;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Demo;
using Kododo.Polyglot.Web.History;

var builder = WebApplication.CreateBuilder(args);

var polyglot = builder.Configuration.GetSection(PolyglotOptions.SectionName).Get<PolyglotOptions>()
               ?? new PolyglotOptions();
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
           ?? new AuthOptions();
var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()
          ?? new ApiOptions();
var demo = builder.Configuration.GetSection(DemoOptions.SectionName).Get<DemoOptions>()
           ?? new DemoOptions();
var connectionString = builder.Configuration.GetConnectionString("Default");
var cultures = polyglot.GetCultures();
var editorPath = polyglot.GetEditorPath();

demo.Apply(auth);
auth.Validate(connectionString);
api.Validate();
demo.Validate(auth);

// The API needs keys, which need the admin UI to issue and revoke them.
var apiEnabled = api.Enabled && auth.Enabled;

builder.Services.AddSingleton(polyglot);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(api);
builder.Services.AddSingleton(demo);
builder.Services.AddHealthChecks();

builder.Services.AddCultureWay(x =>
{
    x.Options.SupportedCultures = cultures;
    x.Options.DefaultCulture = polyglot.DefaultCulture.Trim().Length > 0 ? polyglot.DefaultCulture.Trim() : cultures[0];
    x.AddEditor(editor => EditorIntegration.Configure(editor, polyglot, auth.Enabled));
    if (!string.IsNullOrWhiteSpace(connectionString))
        x.UsePostgreSQL(connectionString);
});

if (auth.Enabled)
{
    builder.Services.AddPolyglotAuth(connectionString!, auth, editorPath);
    // The history needs the database, and a user to attribute changes to.
    builder.Services.AddPolyglotHistory();
}

if (apiEnabled)
    builder.Services.AddPolyglotApi(api);

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(polyglot.PathBase))
    app.UsePathBase("/" + polyglot.PathBase.Trim('/'));

if (string.IsNullOrWhiteSpace(connectionString))
    app.Logger.LogWarning(
        "No ConnectionStrings:Default configured. Translations are kept in memory and will be lost on restart.");

if (auth.Enabled)
    await app.InitializeAuthAsync(auth);
else
    app.Logger.LogWarning(
        "Authentication is disabled (Polyglot:Auth:Enabled=false). Anyone who can reach this instance can edit translations.");

await app.InitializeCultureWayAsync();

if (demo.Enabled)
    await app.SeedDemoAsync(demo, apiEnabled);

if (api.Enabled && !auth.Enabled)
    app.Logger.LogWarning(
        "The delivery API is not mapped: it needs authentication (Polyglot:Auth:Enabled=true), " +
        "without which there is no way to issue or revoke an API key.");

if (auth.Enabled)
{
    // Ahead of authentication, so that requests carrying an invalid key are limited too.
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health");

if (auth.Enabled)
{
    // The pages' stylesheet, script and icon; open to everyone, since the sign-in page needs them.
    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();
}
else
    app.MapGet("/", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}{editorPath}/"));

if (apiEnabled)
{
    app.MapPolyglotApi();
    if (api.OpenApi.Enabled)
    {
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        app.MapOpenApi("/openapi/{documentName}.yaml").AllowAnonymous();
        app.MapPolyglotApiReference(demo);
    }
}

var editor = app.UseCultureWay(editorPath);
if (auth.Enabled)
    editor.RequireAuthorization(PolyglotPolicies.Editor);

app.Run();

public partial class Program;
