using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class ManufacturerNameConverter : ValueConverter<ManufacturerName, string>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public ManufacturerNameConverter() : this(null)
    {
    }

    public ManufacturerNameConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x.Value,
                convertFromProviderExpression: x => new ManufacturerName(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
