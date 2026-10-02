namespace Kododo.Polyglot.Web.Api;

/// <summary>Delivery API settings, read from <c>Polyglot:Api</c> (env prefix <c>Polyglot__Api__</c>).</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Polyglot:Api";

    /// <summary>
    /// The read-only API consuming apps fetch translations from. Needs authentication to be enabled:
    /// without it there is no admin UI to issue or revoke a key with.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Requests allowed per key per minute. Requests without a usable key are counted per client address.</summary>
    public int RequestsPerMinute { get; set; } = 120;

    /// <summary>
    /// How long one read of every translation is reused before the store is read again. An edit reaches
    /// consuming apps within this, plus their own poll interval. <c>0</c> reads the store on every request.
    /// </summary>
    public int SnapshotCacheSeconds { get; set; } = 10;

    public void Validate()
    {
        if (RequestsPerMinute <= 0)
            throw new InvalidOperationException("Polyglot__Api__RequestsPerMinute must be greater than zero.");
        if (SnapshotCacheSeconds < 0)
            throw new InvalidOperationException("Polyglot__Api__SnapshotCacheSeconds must not be negative.");
    }
}
