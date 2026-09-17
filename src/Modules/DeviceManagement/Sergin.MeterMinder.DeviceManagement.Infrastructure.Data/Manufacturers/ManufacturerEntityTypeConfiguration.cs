using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
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

        // Not OwnsMany, though Role.Permissions and User.Roles are: Device holds a foreign key to a model, and
        // EF refuses an owned type on the principal side of a non-ownership relationship
        // (CoreStrings.PrincipalOwnedType). DeviceModel is a regular entity type instead, and the aggregate
        // boundary is held by the repository layer — no DbSet, no repository of its own, written only through
        // Manufacturer.Models. Cascade: a model has no life outside its manufacturer.
        builder.HasMany(m => m.Models)
            .WithOne()
            .HasForeignKey(model => model.ManufacturerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Models)
            .HasField("models")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
