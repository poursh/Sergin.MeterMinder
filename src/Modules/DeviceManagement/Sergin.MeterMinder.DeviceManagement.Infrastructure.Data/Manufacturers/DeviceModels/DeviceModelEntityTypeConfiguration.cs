using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.Converters;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.DeviceModels.Converters;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Manufacturers.DeviceModels;

/// <summary>
/// The entity's own shape. Its relationship to the owner is configured from the owner's side, in
/// <see cref="ManufacturerEntityTypeConfiguration"/>, which also explains why this is a regular entity type and
/// not an owned one.
/// </summary>
internal sealed class DeviceModelEntityTypeConfiguration : IEntityTypeConfiguration<DeviceModel>
{
    public void Configure(EntityTypeBuilder<DeviceModel> builder)
    {
        builder.HasKey(model => model.Id);

        builder.Property(model => model.Id)
            .HasConversion<DeviceModelInternalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(model => model.ManufacturerId)
            .HasConversion<ManufacturerIdConverter>();

        builder.Property(model => model.Name)
            .HasConversion<DeviceModelNameConverter>()
            .IsRequired();

        // Manufacturer.AddModel refuses a duplicate name; this is the guarantee under a race.
        builder.HasIndex(model => new { model.ManufacturerId, model.Name }).IsUnique();
    }
}
