using Kododo.CultureWay;
using Kododo.CultureWay.PostgreSQL;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web;
using Kododo.Polyglot.Web.Auth;

var builder = WebApplication.CreateBuilder(args);

var polyglot = builder.Configuration.GetSection(PolyglotOptions.SectionName).Get<PolyglotOptions>()
               ?? new PolyglotOptions();
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
           ?? new AuthOptions();
var connectionString = builder.Configuration.GetConnectionString("Default");
var cultures = polyglot.GetCultures();
var editorPath = polyglot.GetEditorPath();

auth.Validate(connectionString);

builder.Services.AddSingleton(polyglot);
builder.Services.AddSingleton(auth);
builder.Services.AddHealthChecks();

builder.Services.AddCultureWay(x =>
{
    x.Options.SupportedCultures = cultures;
    x.Options.DefaultCulture = polyglot.DefaultCulture.Trim().Length > 0 ? polyglot.DefaultCulture.Trim() : cultures[0];
    x.AddEditor();
    if (!string.IsNullOrWhiteSpace(connectionString))
        x.UsePostgreSQL(connectionString);
});

if (auth.Enabled)
    builder.Services.AddPolyglotAuth(connectionString!, auth, editorPath);

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

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
}

app.MapHealthChecks("/health");

if (auth.Enabled)
    app.MapRazorPages();
else
    app.MapGet("/", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}{editorPath}/"));

var editor = app.UseCultureWay(editorPath);
if (auth.Enabled)
    editor.RequireAuthorization(PolyglotPolicies.Editor);

app.Run();

public partial class Program;
