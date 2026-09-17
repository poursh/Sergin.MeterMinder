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

        // IDeviceRepository declares DeviceId an alternate key; the validator's check is advisory, this is the guarantee.
        builder.HasIndex(d => d.DeviceId).IsUnique();

        builder.Property(d => d.DeviceModelId)
            .HasConversion<DeviceModelInternalIdConverter>();

        // Restrict, not the default cascade: a reference across an aggregate boundary must never delete the
        // referrer.
        builder.HasOne<DeviceModel>()
            .WithMany()
            .HasForeignKey(d => d.DeviceModelId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
