using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Dispatching;

public sealed class RoutingSerginUiDispatcherTests
{
    private static readonly DeviceQueryResponse StubResponse = new(Guid.NewGuid(), "DEV-1", Guid.NewGuid());

    [Fact]
    public async Task SendAsync_WithoutRequiredPermission_ReturnsForbidden()
    {
        ErrorOr<DeviceQueryResponse> result = await SendAsync(permissions: []);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task SendAsync_WithRequiredPermission_ReachesTheHandler()
    {
        ErrorOr<DeviceQueryResponse> result =
            await SendAsync(permissions: [Permission.Create("permission.dm.devices.read")]);

        Assert.False(result.IsError);
        Assert.Equal(StubResponse, result.Value);
    }

    private static Task<ErrorOr<DeviceQueryResponse>> SendAsync(Permission[] permissions)
    {
        ServiceCollection services = new();

        services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
        services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        services.AddScoped<ISender>(_ => new StubSender(StubResponse));
        services.AddSingleton<IDispatchRouteResolver, AlwaysLocalRouteResolver>();
        services.AddSerginBlazorKit(); // registers ISerginUiDispatcher -> RoutingSerginUiDispatcher, among others

        using ServiceProvider provider = services.BuildServiceProvider();

        ISerginUiDispatcher dispatcher = provider.GetRequiredService<ISerginUiDispatcher>();

        return dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));
    }

    private sealed class StubUserContextFactory(Permission[] permissions) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => new StubUserContext(permissions);
    }

    private sealed class StubUserContext(Permission[] permissions) : IUserContext
    {
        public UserId Id { get; } = new(Guid.NewGuid());
        public string UserName => "stub";
        public string FirstName => "Stub";
        public string LastName => "User";
        public string Email => "stub@sergin.local";
        public HashSet<Permission> Permissions { get; } = [.. permissions];
    }

    private sealed class StubSender(DeviceQueryResponse response) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)(ErrorOr<DeviceQueryResponse>)response);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class AlwaysLocalRouteResolver : IDispatchRouteResolver
    {
        public bool IsRemote(Type requestType) => false;
    }
}
