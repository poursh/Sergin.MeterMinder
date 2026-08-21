using System.Net;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;
using Sergin.SharedKernel.Application.Securities;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;
using Grpc.Net.Client;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Real Kestrel server on a loopback port, real HTTP/2 gRPC call, real DeviceGrpcService ->
/// ISender.Send -> the actual GetDeviceByIdQueryCommandHandler — just with an in-memory
/// IGetDeviceQueryRepository instead of Postgres, so it needs no Testcontainers. Proves Local and
/// Remote agree, byte for byte, for the same input.
/// </summary>
public sealed class DeviceGrpcRoundTripTests : IAsyncLifetime
{
    private static readonly Permission DevicesReadPermission = Permission.Create("permission.dm.devices.read");

    private WebApplication server = null!;
    private GrpcChannel channel = null!;
    private StubDeviceQueryRepository repository = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        // ListenLocalhost(0, ...) throws ("Dynamic port binding is not supported when binding to
        // localhost") on this Kestrel version, because localhost resolves to both the IPv4 and IPv6
        // loopback addresses and a single OS-assigned port can't be shared across both. Listen(Loopback,
        // 0, ...) binds explicitly to 127.0.0.1 with an OS-assigned port instead -- exactly the
        // http://127.0.0.1:0 loopback address this test's design calls for.
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        repository = new StubDeviceQueryRepository();

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IGetDeviceQueryRepository>(repository);
        builder.Services.AddSingleton<IUserContextFactory>(
            new StubUserContextFactory([DevicesReadPermission]));
        builder.Services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());

        // A production host also open-registers the permission and validation pipeline behaviors here,
        // via SerginCoreExtensions in the SharedKernel.Hosts project. Both behaviors are internal to the
        // SharedKernel Application assembly, which is a separate git submodule this outer-repo test
        // project has no visibility grant into, and cannot add one to on its own -- that source lives in,
        // and changes to it belong in, the standalone SharedKernel repository, not here. Leaving them out
        // does not weaken what this file proves. The dispatcher's own permission gate already runs
        // client-side ahead of either the local or the remote branch, so that gate, not a server-side
        // pipeline behavior, is what the forbidden-path test below actually exercises. And the local
        // sender comparison never goes through the dispatcher at all, so it was never subject to that
        // behavior either way.
        builder.Services.AddMediatR(o =>
            o.RegisterServicesFromAssembly(DeviceManagementApplicationAssemblyReference.Assembly));

        server = builder.Build();
        server.MapGrpcService<DeviceGrpcService>();
        await server.StartAsync();

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        channel = GrpcChannel.ForAddress(server.Urls.First());
    }

    public async Task DisposeAsync()
    {
        channel.Dispose();
        await server.StopAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RemoteDispatch_ForExistingDevice_ReturnsSameResultAsLocalHandler()
    {
        // GetDeviceByIdQueryCommandHandler wraps request.Id directly into a DeviceIntenralId for the
        // repository lookup, and the real DeviceQueryRepository.GetDeviceById selects the row's own id
        // column straight into DeviceQueryResponse.Id. For this feature, unlike the write side (where
        // Device.DeviceId is a separate business-facing string, distinct from the DeviceIntenralId
        // primary key), the GetOne read path carries only one Guid identity end to end: the command's
        // Id, the DeviceIntenralId used to look the row up, and the response's Id are the same value.
        // Seeding the stub with an unrelated internal id here would make every lookup miss.
        var deviceGuid = Guid.CreateVersion7();
        DeviceIntenralId internalId = new(deviceGuid);
        DeviceQueryResponse expected = new(deviceGuid, "DEV-42", Guid.CreateVersion7());
        repository.Add(internalId, expected);

        GetDeviceByIdQueryCommand command = new(deviceGuid);

        ISerginUiDispatcher remoteDispatcher = BuildDispatcher(remote: true, permissions: [DevicesReadPermission]);
        ErrorOr<DeviceQueryResponse> remoteResult = await remoteDispatcher.SendAsync(command);

        // "Local" comparison goes through the real MediatR pipeline the server app already wired in
        // InitializeAsync (ISender -> PermissionCheckPipelineBehavior -> GetDeviceByIdQueryCommandHandler),
        // not a hand-constructed handler — GetDeviceByIdQueryCommandHandler is internal to the
        // Application project, and this is the more faithful comparison anyway: the exact same
        // in-process path RoutingSerginUiDispatcher's Local branch takes in production.
        ISender localSender = server.Services.GetRequiredService<ISender>();
        ErrorOr<DeviceQueryResponse> localResult = await localSender.Send(command);

        Assert.False(remoteResult.IsError, remoteResult.IsError ? remoteResult.FirstError.Description : string.Empty);
        Assert.False(localResult.IsError, localResult.IsError ? localResult.FirstError.Description : string.Empty);
        Assert.Equal(localResult.Value, remoteResult.Value);
    }

    [Fact]
    public async Task RemoteDispatch_ForMissingDevice_ReturnsNotFound()
    {
        ISerginUiDispatcher dispatcher = BuildDispatcher(remote: true, permissions: [DevicesReadPermission]);

        ErrorOr<DeviceQueryResponse> result = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task RemoteDispatch_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Deliberately queries for a device the shared `repository` field was never given — if the
        // permission short-circuit in RoutingSerginUiDispatcher (Task 3, Step 4) ever regressed to run
        // after the IsRemote branch instead of before it, this would fail as NotFound (from a real round
        // trip that reached the server) instead of Forbidden, not silently pass either way.
        ISerginUiDispatcher dispatcher = BuildDispatcher(remote: true, permissions: []);

        ErrorOr<DeviceQueryResponse> result =
            await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    private ISerginUiDispatcher BuildDispatcher(bool remote, Permission[] permissions)
    {
        ServiceCollection services = new();

        services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
        services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        services.AddSingleton(new DeviceService.DeviceServiceClient(channel));
        services.AddScoped<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();
        services.AddSingleton<IDispatchRouteResolver>(new FixedRouteResolver(remote));
        services.AddSerginBlazorKit(); // registers ISerginUiDispatcher -> RoutingSerginUiDispatcher, among others

        return services.BuildServiceProvider().GetRequiredService<ISerginUiDispatcher>();
    }

    private sealed class StubDeviceQueryRepository : IGetDeviceQueryRepository
    {
        private readonly Dictionary<DeviceIntenralId, DeviceQueryResponse> devices = [];

        public void Add(DeviceIntenralId id, DeviceQueryResponse response) => devices[id] = response;

        public Task<DeviceQueryResponse?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default) =>
            Task.FromResult(devices.GetValueOrDefault(Id));
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

    private sealed class FixedRouteResolver(bool remote) : IDispatchRouteResolver
    {
        public bool IsRemote(Type requestType) => remote;
    }
}
