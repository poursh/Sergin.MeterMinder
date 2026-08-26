using System.Security.Claims;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authentication;

/// <summary>
/// The user context under Keycloak is built entirely from claims stamped at sign-in, which is what keeps
/// authorization off the database on every send. These cover the two things that has to get right.
/// </summary>
public sealed class ClaimsPrincipalUserContextTests
{
    [Fact]
    public void Create_MapsStampedClaims_IntoIdentityAndPermissions()
    {
        var userId = Guid.CreateVersion7();

        ClaimsPrincipal principal = Authenticated(
            new Claim(SerginClaimTypes.UserId, userId.ToString()),
            new Claim(SerginClaimTypes.Permission, "permission.dm.devices.read"),
            new Claim(SerginClaimTypes.Permission, "permission.ua.users.read"),
            new Claim("preferred_username", "dev"),
            new Claim("email", "dev@sergin.local"),
            new Claim("given_name", "Development"),
            new Claim("family_name", "User"));

        IUserContext context = ClaimsPrincipalUserContext.Create(principal);

        Assert.Equal(userId, context.Id.Value);
        Assert.Equal("dev", context.UserName);
        Assert.Equal("dev@sergin.local", context.Email);
        Assert.Equal("Development", context.FirstName);
        Assert.Equal("User", context.LastName);
        Assert.True(context.HasPermission(Permission.Create("permission.dm.devices.read")));
        Assert.True(context.HasPermission(Permission.Create("permission.ua.users.read")));
        Assert.False(context.HasPermission(Permission.Create("permission.dm.manufacturers.read")));
    }

    /// <summary>
    /// Services are resolved during the OIDC callback, before sign-in completes, and a Blazor circuit
    /// scope has no HttpContext at all. Throwing in either case would turn an authorization question
    /// into a failed login or a broken page, so an unauthenticated principal degrades to no rights.
    /// </summary>
    [Fact]
    public void Create_WithNoAuthenticatedPrincipal_IsAnonymousRatherThanThrowing()
    {
        Assert.Empty(ClaimsPrincipalUserContext.Create(null).Permissions);
        Assert.Empty(ClaimsPrincipalUserContext.Create(new ClaimsPrincipal()).Permissions);
        Assert.Equal(Guid.Empty, ClaimsPrincipalUserContext.Create(null).Id.Value);
    }

    /// <summary>
    /// A stamped value that no longer parses means the permission format outgrew the cookie. Dropping it
    /// narrows the rights the caller holds, which is the safe direction; keeping it would grant
    /// something nothing can name.
    /// </summary>
    [Fact]
    public void Create_WithAnUnparseablePermissionClaim_DropsThatClaimOnly()
    {
        ClaimsPrincipal principal = Authenticated(
            new Claim(SerginClaimTypes.Permission, "NOT A PERMISSION"),
            new Claim(SerginClaimTypes.Permission, "permission.dm.devices.read"));

        IUserContext context = ClaimsPrincipalUserContext.Create(principal);

        Assert.Single(context.Permissions);
        Assert.True(context.HasPermission(Permission.Create("permission.dm.devices.read")));
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
