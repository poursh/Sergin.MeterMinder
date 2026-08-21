using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Devices.Converters;

internal sealed class DeviceIdConverter : ValueConverter<DeviceId, string>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public DeviceIdConverter() : this(null)
    {
    }

    public DeviceIdConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new DeviceId(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
