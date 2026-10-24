using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.GetAudit;

internal sealed class GetManufacturerAuditQueryCommandConfiguration : ICommandConfiguration<GetManufacturerAuditQueryCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerAuditQueryCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Read);
}
