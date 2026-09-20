using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web.Tests;

public class AuthOptionsTests
{
    private const string Db = "Host=localhost";

    [Fact]
    public void Validate_RequiresDatabaseWhenEnabled()
        => Assert.Throws<InvalidOperationException>(() => new AuthOptions().Validate(null));

    [Fact]
    public void Validate_AllowsNoDatabaseWhenDisabled()
        => new AuthOptions { Enabled = false }.Validate(null);

    [Fact]
    public void Validate_LocalOffRequiresOidc()
    {
        var options = new AuthOptions { Local = { Enabled = false } };

        Assert.Throws<InvalidOperationException>(() => options.Validate(Db));
    }

    [Fact]
    public void Validate_LocalOffRequiresAdminGroup()
    {
        var options = new AuthOptions
        {
            Local = { Enabled = false },
            Oidc = { Authority = "https://idp", ClientId = "polyglot" },
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate(Db));

        options.Oidc.AdminGroup = "polyglot-admins";
        options.Validate(Db);
    }
}

public class OidcRoleMapperTests
{
    private static readonly OidcOptions Mapped = new() { AdminGroup = "admins", EditorGroup = "editors" };

    [Fact]
    public void WithGroupMapping_AdminGroupWins()
        => Assert.Equal(UserRole.Admin, OidcRoleMapper.Resolve(Mapped, ["editors", "Admins"], null));

    [Fact]
    public void WithGroupMapping_EditorGroup()
        => Assert.Equal(UserRole.Editor, OidcRoleMapper.Resolve(Mapped, ["editors"], null));

    [Fact]
    public void WithGroupMapping_NoMatchingGroupMeansNoAccess_EvenForExistingAdmins()
        => Assert.Equal(UserRole.None, OidcRoleMapper.Resolve(Mapped, ["other"], UserRole.Admin));

    [Fact]
    public void WithoutMapping_NewUsersGetTheDefaultRole()
        => Assert.Equal(UserRole.None, OidcRoleMapper.Resolve(new OidcOptions { DefaultRole = UserRole.None }, [], null));

    [Fact]
    public void WithoutMapping_ExistingRoleIsKept()
        => Assert.Equal(UserRole.Admin, OidcRoleMapper.Resolve(new OidcOptions(), ["anything"], UserRole.Admin));
}
