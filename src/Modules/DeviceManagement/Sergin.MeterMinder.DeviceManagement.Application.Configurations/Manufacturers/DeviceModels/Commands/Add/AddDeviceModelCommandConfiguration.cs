using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Add;

// Guarded by the manufacturer's version: adding a model changes the Manufacturer aggregate.
internal sealed class AddDeviceModelCommandConfiguration : ICommandConfiguration<AddDeviceModelCommand>
{
    public void Configure(CommandConfigurationBuilder<AddDeviceModelCommand> builder) =>
        builder.RequireExpectedVersion();
}
