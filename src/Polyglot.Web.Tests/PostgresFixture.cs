using Npgsql;
using Testcontainers.PostgreSql;

namespace Kododo.Polyglot.Web.Tests;

/// <summary>One PostgreSQL container for the whole test run; each test gets its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");

        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
