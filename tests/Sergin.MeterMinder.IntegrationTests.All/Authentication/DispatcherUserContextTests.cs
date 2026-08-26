using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Authentication;

/// <summary>
/// Regression cover for the one thing real authentication breaks that nothing else would catch.
/// </summary>
/// <remarks>
/// ScopedSerginDispatcher opens each send in a scope taken from the <em>root</em> provider, which has no
/// HttpContext and no circuit authentication state. Under Keycloak a user context built inside that
/// scope would be anonymous and every permission check would fail for a signed-in user, so the
/// dispatcher carries the calling context in through UserContextAccessor. This asserts the carried
/// context is the one that decides: seed a caller with no rights and the send must be refused, even
/// though the configured development user the host falls back to does hold the permission.
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DispatcherUserContextTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task Send_UsesTheSeededUserContext_NotOneRebuiltInTheChildScope()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current =
            ClaimsPrincipalUserContext.Create(null);

        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ListQueryResponse<GetDeviceListItem>> result =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(10, 1)));

        Assert.True(result.IsError, "An anonymous caller must not reach a slice requiring a permission.");
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    /// <summary>
    /// The other direction: with nothing seeded the factory still supplies the context, so a host running
    /// on its configured development user keeps working exactly as before.
    /// </summary>
    [Fact]
    public async Task Send_WithNothingSeeded_FallsBackToTheUserContextFactory()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ListQueryResponse<GetDeviceListItem>> result =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(10, 1)));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }
}
