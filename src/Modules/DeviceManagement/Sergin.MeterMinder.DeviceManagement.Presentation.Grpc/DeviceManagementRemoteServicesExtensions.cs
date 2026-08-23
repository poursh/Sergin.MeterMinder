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
    public static IServiceCollection AddDeviceManagementRemoteServices(
        this IServiceCollection services, IConfigurationSection configuration)
    {
        string address = configuration["GrpcAddress"]
            ?? throw new InvalidOperationException(
                "Missing 'GrpcAddress' under the 'dm' section — required when the DeviceManagement module is registered Remote.");

        services.AddSingleton(_ => GrpcChannel.ForAddress(address));
        services.AddSingleton(p => new DeviceService.DeviceServiceClient(p.GetRequiredService<GrpcChannel>()));

        services.AddTransient<IRemoteInvoker<GetDeviceByIdQueryCommand, DeviceQueryResponse>, GetDeviceByIdGrpcInvoker>();
        services.AddTransient<
            IRequestHandler<GetDeviceByIdQueryCommand, ErrorOr<DeviceQueryResponse>>,
            RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>>();

        return services;
    }
}
