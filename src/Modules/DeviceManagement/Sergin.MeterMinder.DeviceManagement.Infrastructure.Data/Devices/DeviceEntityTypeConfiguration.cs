using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Devices.Converters;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Devices;

internal sealed class DeviceEntityTypeConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion<DeviceInternalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(d => d.DeviceId)
            .HasConversion<DeviceIdConverter>();

        builder.Property(d => d.ManufacturerId)
            .HasConversion<ManufacturerIdConverter>();

        builder.HasOne<Manufacturer>()
            .WithMany()
            .HasForeignKey(d => d.ManufacturerId)
            .IsRequired();
    }
}
