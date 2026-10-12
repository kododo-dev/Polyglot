using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Kododo.Polyglot.Web.Data;

/// <summary>Reports the instance as unhealthy when PostgreSQL cannot be reached.</summary>
public sealed class DatabaseHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database cannot be reached.", ex);
        }
    }
}
