using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web.Auth;

public static class OidcRoleMapper
{
    /// <summary>
    /// With a group mapping configured the identity provider is authoritative and the role is recomputed at
    /// every sign-in (no matching group means <see cref="UserRole.None"/>). Without one, a new user gets the
    /// default role and afterwards keeps whatever an administrator sets.
    /// </summary>
    public static UserRole Resolve(OidcOptions options, IReadOnlyCollection<string> groups, UserRole? existingRole)
    {
        if (!options.HasGroupMapping)
            return existingRole ?? options.DefaultRole;

        if (Matches(options.AdminGroup, groups))
            return UserRole.Admin;

        return Matches(options.EditorGroup, groups) ? UserRole.Editor : UserRole.None;
    }

    private static bool Matches(string? group, IReadOnlyCollection<string> groups)
        => !string.IsNullOrWhiteSpace(group)
           && groups.Contains(group.Trim(), StringComparer.OrdinalIgnoreCase);
}
