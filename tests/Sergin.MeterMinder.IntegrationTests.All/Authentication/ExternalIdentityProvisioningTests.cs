using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.UserAccess.Application.Configurations.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authentication;

/// <summary>
/// The seam SharedKernel calls inside the OIDC callback. Keycloak proves the identity; this is where
/// Sergin turns it into a local user, whose grants every check then answers from.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ExternalIdentityProvisioningTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task Resolve_ForAnUnseenSubject_CreatesTheUserAndGrantsTheDefaultRole()
    {
        ExternalIdentity identity = IdentityFor(Guid.CreateVersion7().ToString());

        ExternalIdentityResult result = await Resolve(identity);

        Assert.NotEqual(Guid.Empty, result.UserId);

        // The migration seeds "viewer" and provisioning assigns it, so a brand-new user can actually
        // see something. Without that they would sign in successfully to an empty application.
        Assert.True(await HoldsAtRootAsync(result.UserId, DevicePermissions.Read.Permission.Value));
        Assert.True(await HoldsAtRootAsync(result.UserId, UserPermissions.Read.Permission.Value));
    }

    /// <summary>
    /// Every sign-in calls this, not only the first, so a second call must find the same user rather
    /// than accumulate a row per login.
    /// </summary>
    [Fact]
    public async Task Resolve_ForAKnownSubject_ReturnsTheSameUser()
    {
        ExternalIdentity identity = IdentityFor(Guid.CreateVersion7().ToString());

        ExternalIdentityResult first = await Resolve(identity);
        ExternalIdentityResult second = await Resolve(identity with { FirstName = "Renamed" });

        Assert.Equal(first.UserId, second.UserId);
    }

    private static ExternalIdentity IdentityFor(string subject)
        => new(subject, $"external-{subject[..8]}", $"{subject[..8]}@sergin.local", "Test", "User");

    private async Task<bool> HoldsAtRootAsync(Guid userId, string permission)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IScopedPermissionChecker>()
            .HasPermissionAsync(userId, Permission.Create(permission), AuthorizationScope.RootId);
    }

    private async Task<ExternalIdentityResult> Resolve(ExternalIdentity identity)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        IExternalIdentityResolver resolver =
            scope.ServiceProvider.GetRequiredService<IExternalIdentityResolver>();

        return await resolver.ResolveAsync(identity, CancellationToken.None);
    }
}
