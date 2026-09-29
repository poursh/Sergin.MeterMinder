using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices;

internal sealed class DeviceAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) => builder.Audited().SoftDeletable().Versioned();
}
