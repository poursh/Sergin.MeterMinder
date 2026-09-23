using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels;

/// <summary>
/// A child entity, configured on its own: adding a model stamps the model's row, not the manufacturer's.
/// </summary>
internal sealed class DeviceModelAggregateConfiguration : IAggregateConfiguration<DeviceModel>
{
    public void Configure(AggregateFeatureBuilder<DeviceModel> builder) => builder.Audited();
}
