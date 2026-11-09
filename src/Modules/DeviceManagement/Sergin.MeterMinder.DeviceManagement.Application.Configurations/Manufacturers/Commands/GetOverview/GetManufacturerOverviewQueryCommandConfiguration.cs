using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.GetOverview;

internal sealed class GetManufacturerOverviewQueryCommandConfiguration : ICommandConfiguration<GetManufacturerOverviewQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerOverviewQueryCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Read);
}
