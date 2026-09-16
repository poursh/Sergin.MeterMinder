using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class DeviceModelNameConverter : ValueConverter<DeviceModelName, string>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public DeviceModelNameConverter() : this(null)
    {
    }

    public DeviceModelNameConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new DeviceModelName(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
