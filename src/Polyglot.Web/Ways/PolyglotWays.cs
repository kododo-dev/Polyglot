using System.Security.Claims;
using Kododo.CultureWay;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.Demo;
using Kododo.Polyglot.Web.History;
using Kododo.Way.Shell;
using Microsoft.AspNetCore.Mvc;

namespace Kododo.Polyglot.Web.Ways;

/// <summary>
/// Polyglot's own pages as Ways of the shell's panel, next to the CultureWay editor: one React module (<c>UI/</c>)
/// mounted once per page, each with its own authorization and API. The panel lists the pages the user may open.
/// </summary>
public static class PolyglotWays
{
    public const string Overview = "overview";
    public const string History = "history";
    public const string Users = "users";
    public const string ApiKeys = "api-keys";
    public const string Account = "account";

    private const string ResourceRoot = "UI/dist";

    /// <summary>The overview, before the editor in the panel's menu.</summary>
    public static void MapOverviewWay(this IEndpointRouteBuilder app)
    {
        var group = app.MapWay(Module(Overview, "Overview", "home"), $"/{Overview}")
            .RequireAuthorization(PolyglotPolicies.Admin);
        group.MapPost("/api/Overview", GetOverview);
    }

    /// <summary>History, users, API keys and the account page, after the editor in the panel's menu.</summary>
    public static void MapAdminWays(this IEndpointRouteBuilder app)
    {
        var history = app.MapWay(Module(History, "History", "history"), $"/{History}")
            .RequireAuthorization(PolyglotPolicies.Editor);
        history.MapPost("/api/History", GetHistory);
        history.MapPost("/api/Undo", Undo);

        var users = app.MapWay(Module(Users, "Users", "people"), $"/{Users}")
            .RequireAuthorization(PolyglotPolicies.Admin);
        users.MapPost("/api/Users", GetUsers);
        // Users and passwords stay as they are in the demo, for the next visitor.
        var userChanges = users.MapGroup("/api").AddEndpointFilter(DemoReadOnly);
        userChanges.MapPost("/CreateUser", CreateUser);
        userChanges.MapPost("/SetRole", SetRole);
        userChanges.MapPost("/SetDisabled", SetDisabled);
        userChanges.MapPost("/ResetPassword", ResetPassword);

        var keys = app.MapWay(Module(ApiKeys, "API keys", "key"), $"/{ApiKeys}")
            .RequireAuthorization(PolyglotPolicies.Admin);
        keys.MapPost("/api/ApiKeys", GetApiKeys);
        keys.MapPost("/api/CreateKey", CreateKey);
        keys.MapPost("/api/SetKeyDisabled", SetKeyDisabled);
        keys.MapPost("/api/DeleteKey", DeleteKey);

        // Not in the menu: the user's name at the bottom of it links here.
        var account = app.MapWay(Module(Account, "Account", "people", hidden: true), $"/{Account}")
            .RequireAuthorization();
        account.MapPost("/api/Account", GetAccount);
        account.MapPost("/api/ChangePassword", ChangePassword).AddEndpointFilter(DemoReadOnly);
    }

    private static WayModule Module(string id, string name, string icon, bool hidden = false) =>
        new(id, name, typeof(PolyglotWays).Assembly, ResourceRoot) { Icon = icon, IsHidden = hidden };

    // Overview

    public sealed record OverviewDto(
        IReadOnlyList<string> Cultures,
        string DefaultCulture,
        int UserCount,
        int DisabledUserCount,
        int KeyCount,
        int ActiveKeyCount,
        bool ApiEnabled,
        string? ApiReferenceUrl,
        string? OpenApiUrl,
        DemoKeyDto? DemoKey);

    /// <summary>The demo's public key and the calls to try with it.</summary>
    public sealed record DemoKeyDto(string Token, string BaseUrl, string Culture, string? ApiReferenceUrl);

    private static async Task<OverviewDto> GetOverview(
        HttpContext ctx, CultureWayOptions cultureWay, ApiOptions api, DemoOptions demo, UserService users, [FromServices] ApiKeyService keys)
    {
        var allUsers = await users.ListAsync(ctx.RequestAborted);
        var allKeys = await keys.ListAsync(ctx.RequestAborted);
        var now = DateTimeOffset.UtcNow;
        return new OverviewDto(
            cultureWay.SupportedCultures,
            cultureWay.DefaultCulture,
            allUsers.Count,
            allUsers.Count(u => u.IsDisabled),
            allKeys.Count,
            allKeys.Count(k => k.IsActive(now)),
            api.Enabled,
            ApiReferenceUrl(ctx, api),
            api.Enabled && api.OpenApi.Enabled ? $"{ctx.Request.PathBase}/openapi/v1.json" : null,
            DemoKey(ctx, cultureWay, api, demo));
    }

    // History

    public sealed record HistoryQuery(string? Key, string? Culture, Guid? UserId, DateOnly? From, DateOnly? To, long? Before);

    public sealed record ChangeDto(
        long Id, string Key, string Culture, string? OldValue, string? NewValue, string UserName, DateTimeOffset ChangedAt);

    public sealed record HistoryUserDto(Guid Id, string Name);

    public sealed record HistoryDto(
        IReadOnlyList<ChangeDto> Changes, long? OlderThan, IReadOnlyList<string> Cultures, IReadOnlyList<HistoryUserDto> Users);

    public sealed record IdRequest(long Id);

    private static async Task<HistoryDto> GetHistory(HistoryQuery query, TranslationHistory history, HttpContext ctx)
    {
        var page = await history.ListAsync(
            new HistoryFilter(query.Key, query.Culture, query.UserId, query.From, query.To), query.Before, ctx.RequestAborted);
        var (cultures, users) = await history.FilterOptionsAsync(ctx.RequestAborted);
        return new HistoryDto(
            [.. page.Changes.Select(c => new ChangeDto(c.Id, c.Key, c.Culture, c.OldValue, c.NewValue, c.UserName, c.ChangedAt))],
            page.OlderThan,
            cultures,
            [.. users.Select(u => new HistoryUserDto(u.Id, u.Name))]);
    }

    private static async Task<IResult> Undo(IdRequest request, TranslationHistory history, HttpContext ctx) =>
        Outcome(await history.UndoAsync(request.Id, ctx.RequestAborted));

    // Users

    public sealed record UserDto(
        Guid Id, string Username, string DisplayName, UserRole Role, bool IsDisabled, bool HasPassword, bool IsExternal);

    public sealed record UsersDto(IReadOnlyList<UserDto> Users, Guid? CurrentUserId, bool ReadOnly, int MinPasswordLength);

    public sealed record CreateUserRequest(string? Username, string? DisplayName, string? Password, UserRole Role);

    public sealed record SetRoleRequest(Guid Id, UserRole Role);

    public sealed record SetDisabledRequest(Guid Id, bool Disabled);

    public sealed record ResetPasswordRequest(Guid Id, string? NewPassword);

    private static async Task<UsersDto> GetUsers(HttpContext ctx, UserService users, DemoOptions demo)
    {
        var all = await users.ListAsync(ctx.RequestAborted);
        return new UsersDto(
            [.. all.Select(u => new UserDto(
                u.Id, u.Username, u.DisplayName, u.Role, u.IsDisabled, u.PasswordHash is not null, u.ExternalSubject is not null))],
            CurrentUserId(ctx),
            demo.Enabled,
            UserService.MinPasswordLength);
    }

    private static async Task<IResult> CreateUser(CreateUserRequest request, UserService users, HttpContext ctx)
    {
        var (_, errors) = await users.CreateLocalAsync(
            request.Username ?? "", request.DisplayName, request.Password ?? "",
            request.Role == UserRole.Admin ? UserRole.Admin : UserRole.Editor, ctx.RequestAborted);
        return Outcome(errors);
    }

    // Admins cannot lock themselves out: their own role and account stay as they are.
    private static async Task<IResult> SetRole(SetRoleRequest request, UserService users, HttpContext ctx) =>
        request.Id == CurrentUserId(ctx)
            ? Outcome(["You cannot change your own role. Ask another administrator."])
            : Outcome(await users.SetRoleAsync(request.Id, request.Role, ctx.RequestAborted));

    private static async Task<IResult> SetDisabled(SetDisabledRequest request, UserService users, HttpContext ctx) =>
        request.Id == CurrentUserId(ctx)
            ? Outcome(["You cannot disable your own account. Ask another administrator."])
            : Outcome(await users.SetDisabledAsync(request.Id, request.Disabled, ctx.RequestAborted));

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, UserService users, HttpContext ctx) =>
        Outcome(await users.SetPasswordAsync(request.Id, request.NewPassword ?? "", ctx.RequestAborted));

    // API keys

    public sealed record ApiKeyDto(
        Guid Id, string Name, string Prefix, string Status, bool IsDemo,
        DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt);

    public sealed record ApiKeysDto(
        IReadOnlyList<ApiKeyDto> Keys, bool ApiEnabled, string BaseUrl, string DefaultCulture, int MaxNameLength,
        string? ApiReferenceUrl, DemoKeyDto? DemoKey);

    /// <param name="ValidThrough">The last day the key works, in UTC; null for a key that does not expire.</param>
    public sealed record CreateKeyRequest(string? Name, DateOnly? ValidThrough);

    /// <summary>The token just issued: in this response only, never stored.</summary>
    public sealed record CreatedKeyDto(string Name, string Token);

    public sealed record KeyRequest(Guid Id);

    public sealed record SetKeyDisabledRequest(Guid Id, bool Disabled);

    private static async Task<ApiKeysDto> GetApiKeys(
        HttpContext ctx, [FromServices] ApiKeyService keys, CultureWayOptions cultureWay, ApiOptions api, DemoOptions demo)
    {
        var now = DateTimeOffset.UtcNow;
        var all = await keys.ListAsync(ctx.RequestAborted);
        return new ApiKeysDto(
            [.. all.Select(k => new ApiKeyDto(
                k.Id, k.Name, $"{ApiKeyService.TokenPrefix}{k.KeyId}_…",
                k.IsDisabled ? "disabled" : k.IsActive(now) ? "active" : "expired",
                demo.IsDemoKey(k), k.CreatedAt, k.LastUsedAt, k.ExpiresAt))],
            api.Enabled,
            BaseUrl(ctx),
            cultureWay.DefaultCulture,
            ApiKeyService.MaxNameLength,
            ApiReferenceUrl(ctx, api),
            DemoKey(ctx, cultureWay, api, demo));
    }

    private static async Task<IResult> CreateKey(CreateKeyRequest request, [FromServices] ApiKeyService keys, HttpContext ctx)
    {
        // Through the end of the chosen day, so "valid through 31 Dec" still works on 31 Dec.
        DateTimeOffset? expiresAt = request.ValidThrough is { } day
            ? new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        var (key, token, errors) = await keys.CreateAsync(request.Name ?? "", CurrentUserId(ctx), expiresAt, ctx.RequestAborted);
        if (key is null)
            return Outcome(errors);

        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new CreatedKeyDto(key.Name, token));
    }

    private static async Task<IResult> SetKeyDisabled(SetKeyDisabledRequest request, [FromServices] ApiKeyService keys, DemoOptions demo, HttpContext ctx) =>
        Outcome(await DemoKeyErrorsAsync(request.Id, keys, demo, ctx)
                ?? await keys.SetDisabledAsync(request.Id, request.Disabled, ctx.RequestAborted));

    private static async Task<IResult> DeleteKey(KeyRequest request, [FromServices] ApiKeyService keys, DemoOptions demo, HttpContext ctx) =>
        Outcome(await DemoKeyErrorsAsync(request.Id, keys, demo, ctx)
                ?? await keys.DeleteAsync(request.Id, ctx.RequestAborted));

    // The key the demo shows has to keep working for the next visitor.
    private static async Task<string[]?> DemoKeyErrorsAsync(Guid id, [FromServices] ApiKeyService keys, DemoOptions demo, HttpContext ctx) =>
        demo.Enabled && await keys.FindByIdAsync(id, ctx.RequestAborted) is { } key && demo.IsDemoKey(key)
            ? ["The demo's own key cannot be disabled or deleted. Create a key of your own to try that."]
            : null;

    // Account

    public sealed record AccountDto(
        string Username, string DisplayName, UserRole Role, bool HasPassword, bool IsDemo, int MinPasswordLength);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    private static async Task<IResult> GetAccount(HttpContext ctx, UserService users, DemoOptions demo) =>
        CurrentUserId(ctx) is { } id && await users.FindByIdAsync(id, ctx.RequestAborted) is { } user
            ? Results.Ok(new AccountDto(
                user.Username, user.DisplayName, user.Role, user.PasswordHash is not null,
                // Everyone shares the demo account, so its password stays the published one.
                demo.Enabled, UserService.MinPasswordLength))
            : Results.Forbid();

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, UserService users, HttpContext ctx) =>
        CurrentUserId(ctx) is { } id
            ? Outcome(await users.ChangePasswordAsync(id, request.CurrentPassword ?? "", request.NewPassword ?? "", ctx.RequestAborted))
            : Results.Forbid();

    // Shared

    /// <summary>In the demo, refuses what would spoil it for the next visitor; the module hides those controls.</summary>
    private static async ValueTask<object?> DemoReadOnly(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<DemoOptions>().Enabled
            ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Turned off in the demo")
            : await next(context);

    /// <summary>204, or the errors as a 400 the module shows as they are.</summary>
    private static IResult Outcome(string[] errors) =>
        errors.Length == 0 ? Results.NoContent() : Results.ValidationProblem(new Dictionary<string, string[]> { [""] = errors });

    private static Guid? CurrentUserId(HttpContext ctx) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    private static string? ApiReferenceUrl(HttpContext ctx, ApiOptions api) =>
        api.Enabled && api.OpenApi.Enabled ? $"{ctx.Request.PathBase}/api-reference/" : null;

    private static DemoKeyDto? DemoKey(HttpContext ctx, CultureWayOptions cultureWay, ApiOptions api, DemoOptions demo)
    {
        if (!demo.Enabled || !api.Enabled)
            return null;
        // A language other than the default shows the fallback at work.
        var culture = cultureWay.SupportedCultures.FirstOrDefault(c => c != cultureWay.DefaultCulture) ?? cultureWay.DefaultCulture;
        return new DemoKeyDto(demo.ApiKey, BaseUrl(ctx), culture, ApiReferenceUrl(ctx, api));
    }
}
