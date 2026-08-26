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
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;
using Grpc.Net.Client;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Real Kestrel server on a loopback port, real HTTP/2 gRPC call, real DeviceGrpcService ->
/// ISender.Send -> the actual GetDeviceByIdQueryCommandHandler — just with an in-memory
/// IGetDeviceQueryRepository instead of Postgres, so it needs no Testcontainers. Proves Local and
/// Remote agree, byte for byte, for the same input. Both sides are a plain ISender: "Local" is the
/// server app's own bespoke MediatR registration (InitializeAsync, below); "Remote" is
/// BuildRemoteSender's bespoke registration, whose GetDeviceByIdQueryCommand handler is
/// RemoteForwardingHandler&lt;GetDeviceByIdQueryCommand, DeviceQueryResponse&gt; wrapping
/// GetDeviceByIdGrpcInvoker's real gRPC call into the Kestrel server started here. There is no
/// dispatcher and no Local/Remote routing decision left anywhere in this file — MediatR dispatches
/// by request type on both sides, exactly as it does in production.
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

        // Deliberately no PermissionCheckPipelineBehavior/ValidationPipelineBehavior registered on this
        // "Local" comparison side, even though Task 5's InternalsVisibleTo grant would now let this test
        // project reference them directly (see BuildRemoteSender, below, which does). This side exists
        // purely to prove Local and Remote agree on the handler's own output for the same input; it never
        // enforced permissions before the redesign and doesn't need to now. The forbidden-path test below
        // is exercised entirely by BuildRemoteSender's own real PermissionCheckPipelineBehavior — this
        // server-side registration plays no part in it.
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

        ISender remoteSender = BuildRemoteSender([DevicesReadPermission]);
        ErrorOr<DeviceQueryResponse> remoteResult = await remoteSender.Send(command);

        // "Local" comparison goes through the server app's own bespoke MediatR setup wired in
        // InitializeAsync: ISender -> GetDeviceByIdQueryCommandHandler, with no pipeline behaviors
        // registered (see the comment there) — not a hand-constructed handler, since
        // GetDeviceByIdQueryCommandHandler is internal to the Application project.
        ISender localSender = server.Services.GetRequiredService<ISender>();
        ErrorOr<DeviceQueryResponse> localResult = await localSender.Send(command);

        Assert.False(remoteResult.IsError, remoteResult.IsError ? remoteResult.FirstError.Description : string.Empty);
        Assert.False(localResult.IsError, localResult.IsError ? localResult.FirstError.Description : string.Empty);
        Assert.Equal(localResult.Value, remoteResult.Value);
    }

    [Fact]
    public async Task RemoteDispatch_ForMissingDevice_ReturnsNotFound()
    {
        ISender sender = BuildRemoteSender([DevicesReadPermission]);

        ErrorOr<DeviceQueryResponse> result = await sender.Send(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task RemoteDispatch_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Deliberately queries for a device the shared `repository` field was never given — if the real
        // PermissionCheckPipelineBehavior (registered below in BuildRemoteSender, reachable only via
        // Task 5's InternalsVisibleTo grant into the SharedKernel Application assembly) ever stopped
        // running ahead of RemoteForwardingHandler's gRPC call, this would fail as NotFound (from a real
        // round trip that reached the server) instead of Forbidden, not silently pass either way.
        ISender sender = BuildRemoteSender([]);

        ErrorOr<DeviceQueryResponse> result =
            await sender.Send(new GetDeviceByIdQueryCommand(Guid.NewGuid()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    private ISender BuildRemoteSender(Permission[] permissions)
    {
        ServiceCollection services = new();

        services.AddSingleton<IUserContextFactory>(new StubUserContextFactory(permissions));
        services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());
        services.AddSingleton(new DeviceService.DeviceServiceClient(channel));
        services.AddScoped<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();

        // AddMediatR throws ("No assemblies found to scan") if given no assembly at all, even though the
        // one handler this test needs is registered explicitly below — so it's pointed at
        // GetDeviceByIdQueryCommand's own assembly (.Application.Contracts) purely to satisfy that
        // requirement. That assembly holds only request/response records, never handlers (see root
        // CLAUDE.md's Application.Contracts split), so the scan itself finds nothing and can't shadow the
        // explicit RemoteForwardingHandler registration below.
        services.AddMediatR(o =>
        {
            o.RegisterServicesFromAssemblyContaining<GetDeviceByIdQueryCommand>();
            o.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>));
        });

        // Not discovered by AddMediatR's assembly scan (generic, lives outside any scanned assembly) —
        // registered explicitly, same as production's AddDeviceManagementRemoteServices (Task 11).
        services.AddTransient<
            IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>,
            RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>>();

        return services.BuildServiceProvider().GetRequiredService<ISender>();
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
}
