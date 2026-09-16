using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class DeviceModelInternalIdConverter : ValueConverter<DeviceModelInternalId, Guid>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public DeviceModelInternalIdConverter() : this(null)
    {
    }

    public DeviceModelInternalIdConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new DeviceModelInternalId(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
