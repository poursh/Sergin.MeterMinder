using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.GetList;

internal sealed class GetDeviceModelListQueryCommandConfiguration : ICommandConfiguration<GetDeviceModelListQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceModelListQueryCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Read)
            .Texts(DeviceModelTexts.Category);
}
