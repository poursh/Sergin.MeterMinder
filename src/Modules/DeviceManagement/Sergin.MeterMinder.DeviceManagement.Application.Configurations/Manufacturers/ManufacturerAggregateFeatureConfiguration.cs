using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;

internal sealed class ManufacturerAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Manufacturer>
{
    public void Configure(AggregateFeatureBuilder<Manufacturer> builder) => builder.Audited().SoftDeletable().Versioned();
}
