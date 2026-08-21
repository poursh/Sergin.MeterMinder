using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers;

internal sealed class ManufacturerEntityTypeConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasConversion<ManufacturerIdConverter>()
            .ValueGeneratedNever();

        builder.Property(m => m.Name)
            .HasConversion<ManufacturerNameConverter>()
            .IsRequired();

        builder.Property(m => m.Address)
            .HasConversion<ManufacturerAddressConverter>()
            .IsRequired(false);
    }
}
