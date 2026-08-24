using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Grpc;

/// <remarks>
/// Public, not internal: no production host references this yet — same "live-but-unhosted" posture as
/// DeviceGrpcService/GetDeviceByIdGrpcInvoker in this same project. A future gateway host would reference
/// this class (and this project) instead of the DeviceManagementModule composition root, specifically to
/// avoid pulling in .Application/.Infrastructure it has no need to run locally.
/// </remarks>
public sealed class DeviceManagementRemoteModule : ISerginRemoteModule
{
    // Must match DeviceManagementDbContext.Schema ("dm"). Duplicated, not shared, because this project
    // must not reference .Infrastructure.Data — that's the isolation property this class exists for.
    public string Schema => "dm";

    public Assembly ContractsAssembly => DeviceManagementApplicationContractsAssemblyReference.Assembly;

    public void AddRemoteServices(IServiceCollection services, IConfigurationSection configuration)
        => services.AddDeviceManagementRemoteServices(configuration);
}
