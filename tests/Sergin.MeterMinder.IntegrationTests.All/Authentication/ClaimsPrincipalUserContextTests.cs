using System.Security.Claims;
using Sergin.SharedKernel.Application.Securities.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authentication;

/// <summary>
/// The user context under Keycloak is built entirely from claims stamped at sign-in, which is what keeps
/// resolving it off the database on every send. It carries identity only: permissions are answered by scope.
/// </summary>
public sealed class ClaimsPrincipalUserContextTests
{
    [Fact]
    public void Create_MapsStampedClaims_IntoIdentity()
    {
        var userId = Guid.CreateVersion7();

        ClaimsPrincipal principal = Authenticated(
            new Claim(SerginClaimTypes.UserId, userId.ToString()),
            new Claim("preferred_username", "dev"),
            new Claim("email", "dev@sergin.local"),
            new Claim("given_name", "Development"),
            new Claim("family_name", "User"));

        var context = ClaimsPrincipalUserContext.Create(principal);

        Assert.Equal(userId, context.Id.Value);
        Assert.Equal("dev", context.UserName);
        Assert.Equal("dev@sergin.local", context.Email);
        Assert.Equal("Development", context.FirstName);
        Assert.Equal("User", context.LastName);
    }

    /// <summary>
    /// Services are resolved during the OIDC callback, before sign-in completes, and a Blazor circuit
    /// scope has no HttpContext at all. Throwing in either case would turn an authorization question
    /// into a failed login or a broken page, so an unauthenticated principal degrades to anonymous.
    /// </summary>
    [Fact]
    public void Create_WithNoAuthenticatedPrincipal_IsAnonymousRatherThanThrowing()
    {
        Assert.Same(ClaimsPrincipalUserContext.Anonymous, ClaimsPrincipalUserContext.Create(null));
        Assert.Same(ClaimsPrincipalUserContext.Anonymous, ClaimsPrincipalUserContext.Create(new ClaimsPrincipal()));
        Assert.Equal(Guid.Empty, ClaimsPrincipalUserContext.Create(null).Id.Value);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
