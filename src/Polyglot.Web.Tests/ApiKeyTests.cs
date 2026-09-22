using System.Text.RegularExpressions;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class ApiKeyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    [GeneratedRegex("^pg_[a-z0-9]{8}_[A-Za-z0-9]{43}$")]
    private static partial Regex TokenPattern();

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
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", "bootstrap-password-1");
        });
        _factories.Add(factory);
        return factory;
    }

    // Every call gets its own scope, the way a request would, so nothing is proven by an entity that
    // merely happens to be tracked by the same DbContext.
    private static async Task<(ApiKey Key, string Token)> IssueAsync(
        WebApplicationFactory<Program> factory, string name, DateTimeOffset? expiresAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (key, token, errors) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync(name, expiresAt: expiresAt);

        Assert.True(key is not null, string.Join(" ", errors));
        return (key, token);
    }

    private static async Task<ApiKey?> ValidateAsync(
        WebApplicationFactory<Program> factory, string token, DateTimeOffset? now = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        return now is null ? await keys.ValidateAsync(token) : await keys.ValidateAsync(token, now.Value);
    }

    private static async Task<ApiKey?> ReadAsync(WebApplicationFactory<Program> factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>().FindByIdAsync(id);
    }

    private static async Task<T> WithServiceAsync<T>(
        WebApplicationFactory<Program> factory, Func<ApiKeyService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApiKeyService>());
    }

    /// <summary>Postgres keeps microseconds, so compare instants that survive the round trip.</summary>
    private static DateTimeOffset WholeSecond(DateTimeOffset value)
        => new(value.UtcDateTime.AddTicks(-(value.UtcTicks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);

    [Fact]
    public async Task IssuedKey_HasTheDocumentedTokenShapeAndValidates()
    {
        var factory = await CreateAppAsync();

        var (key, token) = await IssueAsync(factory, "shop-frontend-prod");

        Assert.Matches(TokenPattern(), token);
        Assert.StartsWith($"pg_{key.KeyId}_", token);
        Assert.Equal(ApiKeyService.ReadScope, key.Scopes);
        Assert.Null(key.LastUsedAt);
        Assert.False(key.IsDisabled);

        Assert.Equal(key.Id, (await ValidateAsync(factory, token))?.Id);
    }

    [Fact]
    public async Task StoredKey_KeepsNoCopyOfTheSecret()
    {
        var factory = await CreateAppAsync();
        var (key, token) = await IssueAsync(factory, "shop-frontend-prod");
        var secret = token.Split('_')[2];

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = Assert.Single(await db.ApiKeys.ToListAsync());

        Assert.Equal(key.Id, stored.Id);
        Assert.Equal(32, stored.SecretHash.Length);
        Assert.DoesNotContain(secret, Convert.ToBase64String(stored.SecretHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("pg_short_secret")]
    [InlineData("pg_ABCDEFGH_0123456789012345678901234567890123456")]
    // The secret is letters and digits only, so that splitting a token on '_' is unambiguous.
    [InlineData("pg_abcdefgh_012345678901234567890123456789012345678901_")]
    [InlineData("pg_abcdefgh_012345678901234567890123456789012345678901-")]
    public async Task MalformedToken_IsRejected(string token)
    {
        var factory = await CreateAppAsync();

        Assert.False(ApiKeyService.TryParse(token, out _, out _));
        Assert.Null(await ValidateAsync(factory, token));
    }

    [Fact]
    public async Task TamperedSecret_IsRejected()
    {
        var factory = await CreateAppAsync();
        var (_, token) = await IssueAsync(factory, "shop-frontend-prod");

        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        Assert.Null(await ValidateAsync(factory, tampered));
    }

    [Fact]
    public async Task SecretUnderAnotherKeyId_IsRejected()
    {
        var factory = await CreateAppAsync();
        var (first, firstToken) = await IssueAsync(factory, "first");
        var (second, _) = await IssueAsync(factory, "second");

        var swapped = $"pg_{second.KeyId}_{firstToken.Split('_')[2]}";

        Assert.Null(await ValidateAsync(factory, swapped));
        Assert.Equal(first.Id, (await ValidateAsync(factory, firstToken))?.Id);
    }

    [Fact]
    public async Task DisabledKey_IsRejectedUntilItIsEnabledAgain()
    {
        var factory = await CreateAppAsync();
        var (key, token) = await IssueAsync(factory, "shop-frontend-prod");

        Assert.Empty(await WithServiceAsync(factory, s => s.SetDisabledAsync(key.Id, true)));
        Assert.Null(await ValidateAsync(factory, token));

        Assert.Empty(await WithServiceAsync(factory, s => s.SetDisabledAsync(key.Id, false)));
        Assert.NotNull(await ValidateAsync(factory, token));
    }

    [Fact]
    public async Task ExpiredKey_IsRejected()
    {
        var factory = await CreateAppAsync();
        var expiry = WholeSecond(DateTimeOffset.UtcNow.AddHours(1));
        var (_, token) = await IssueAsync(factory, "expiring", expiry);

        Assert.NotNull(await ValidateAsync(factory, token, expiry.AddMinutes(-1)));
        Assert.Null(await ValidateAsync(factory, token, expiry.AddMinutes(1)));
    }

    [Fact]
    public async Task ExpiryInThePast_IsRefused()
    {
        var factory = await CreateAppAsync();

        var (key, token, errors) = await WithServiceAsync(
            factory, s => s.CreateAsync("stale", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Null(key);
        Assert.Equal("", token);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task EmptyName_IsRefused()
    {
        var factory = await CreateAppAsync();

        var (key, _, errors) = await WithServiceAsync(factory, s => s.CreateAsync("   "));

        Assert.Null(key);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task LastUsed_IsWrittenAtMostOncePerMinute()
    {
        var factory = await CreateAppAsync();
        var (key, token) = await IssueAsync(factory, "shop-frontend-prod");

        var first = WholeSecond(DateTimeOffset.UtcNow);
        Assert.NotNull(await ValidateAsync(factory, token, first));
        Assert.Equal(first, (await ReadAsync(factory, key.Id))!.LastUsedAt);

        // Inside the window the timestamp stays put, so a polling client costs no write.
        Assert.NotNull(await ValidateAsync(factory, token, first.AddSeconds(30)));
        Assert.Equal(first, (await ReadAsync(factory, key.Id))!.LastUsedAt);

        var later = first.AddMinutes(2);
        Assert.NotNull(await ValidateAsync(factory, token, later));
        Assert.Equal(later, (await ReadAsync(factory, key.Id))!.LastUsedAt);
    }

    [Fact]
    public async Task DeletedKey_StopsWorking()
    {
        var factory = await CreateAppAsync();
        var (key, token) = await IssueAsync(factory, "shop-frontend-prod");

        Assert.Empty(await WithServiceAsync(factory, s => s.DeleteAsync(key.Id)));

        Assert.Null(await ValidateAsync(factory, token));
        Assert.Null(await ReadAsync(factory, key.Id));
        Assert.NotEmpty(await WithServiceAsync(factory, s => s.DeleteAsync(key.Id)));
    }

    [Fact]
    public async Task EveryIssuedToken_ParsesAndValidates()
    {
        var factory = await CreateAppAsync();

        // A token whose alphabet overlaps the separator only fails for some keys, so one sample is
        // not enough to trust the format.
        for (var i = 0; i < 20; i++)
        {
            var (key, token) = await IssueAsync(factory, $"app-{i}");

            Assert.Matches(TokenPattern(), token);
            Assert.True(ApiKeyService.TryParse(token, out var keyId, out _), token);
            Assert.Equal(key.KeyId, keyId);
            Assert.Equal(key.Id, (await ValidateAsync(factory, token))?.Id);
        }
    }

    [Fact]
    public async Task ListedKeys_AreOrderedAndHaveDistinctIdentifiers()
    {
        var factory = await CreateAppAsync();
        for (var i = 4; i >= 0; i--)
            await IssueAsync(factory, $"app-{i}");

        var listed = await WithServiceAsync(factory, s => s.ListAsync());

        Assert.Equal(["app-0", "app-1", "app-2", "app-3", "app-4"], listed.Select(k => k.Name));
        Assert.Equal(5, listed.Select(k => k.KeyId).Distinct().Count());
    }
}
