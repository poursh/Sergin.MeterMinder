using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Devices;
using Sergin.SharedKernel.Infrastructure.Dispatching;
using Sergin.SharedKernel.Presentation.Grpc.Dispatching;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;

public static class DeviceManagementRemoteServicesExtensions
{
    // Must match DeviceManagementRemoteModule.Schema ("dm"). Duplicated, not shared, for the same
    // isolation reason documented there: this project must not reference .Infrastructure.Data.
    private const string Schema = "dm";

    public static IServiceCollection AddDeviceManagementRemoteServices(
        this IServiceCollection services, IConfigurationSection configuration)
    {
        string address = configuration.GetSection(Schema)["GrpcAddress"]
            ?? throw new InvalidOperationException(
                $"Missing 'GrpcAddress' under the '{Schema}' section — required when the DeviceManagement module is registered Remote.");

        // Build the channel once and capture it in the client factory's closure, rather than registering
        // GrpcChannel itself as a shared singleton service — a bare GrpcChannel registration would collide
        // across multiple remote modules, with whichever module registers last winning any
        // GetRequiredService<GrpcChannel>() call from an unrelated module's client.
        var channel = GrpcChannel.ForAddress(address);
        services.AddSingleton(_ => new DeviceService.DeviceServiceClient(channel));

        services.AddTransient<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();
        services.AddTransient<
            IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>,
            RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>>();

        return services;
    }
}
