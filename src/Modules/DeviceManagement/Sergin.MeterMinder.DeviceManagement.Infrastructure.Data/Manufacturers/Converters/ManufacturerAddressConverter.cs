using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

internal sealed class ManufacturerAddressConverter : ValueConverter<ManufacturerAddress?, string?>
{
    private static readonly ConverterMappingHints defaultHints = new();

    public ManufacturerAddressConverter() : this(null)
    {
    }

    public ManufacturerAddressConverter(ConverterMappingHints? mappingHints)
        : base(
                convertToProviderExpression: x => x == null ? null : x.Value,
                convertFromProviderExpression: x => x == null ? null : new ManufacturerAddress(x),
                mappingHints: defaultHints.With(mappingHints))
    {
    }
}
