namespace Kododo.Polyglot.Web.Data;

/// <summary>
/// A key that authenticates a machine caller of the delivery API. The token it was issued from is
/// never stored: <see cref="KeyId"/> identifies the key, <see cref="SecretHash"/> verifies it.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }

    /// <summary>The public part of the token, safe to display and log.</summary>
    public string KeyId { get; set; } = "";

    /// <summary>SHA-256 of the token's secret part.</summary>
    public byte[] SecretHash { get; set; } = [];

    /// <summary>What the key is for, e.g. <c>shop-frontend-prod</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Comma-separated permissions; <c>translations:read</c> is the only one so far.</summary>
    public string Scopes { get; set; } = "";

    /// <summary>The administrator who issued the key. Not a foreign key, so deleting them keeps the trail.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null for a key that does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Approximate: written at most once a minute, so a poll does not cost a write per request.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public bool IsDisabled { get; set; }

    public bool IsActive(DateTimeOffset now) => !IsDisabled && (ExpiresAt is null || ExpiresAt > now);
}
