using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Kododo.Polyglot.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminPassword = "bootstrap-password-1";

    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateApp(string connectionString)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Polyglot:Auth:Bootstrap:Password", AdminPassword);
        });
        _factories.Add(factory);
        return factory;
    }

    [Fact]
    public async Task Ready_WithTheDatabaseUp_ReturnsHealthy()
    {
        var client = CreateApp(await postgres.CreateDatabaseAsync()).CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_WithTheDatabaseGone_ReturnsUnhealthy_WhileLivenessStaysUp()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var client = CreateApp(connectionString).CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        await DropDatabaseAsync(connectionString);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    private static async Task DropDatabaseAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var name = builder.Database;
        builder.Database = "postgres";

        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
