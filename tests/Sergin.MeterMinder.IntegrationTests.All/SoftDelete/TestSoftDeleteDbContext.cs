using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.IntegrationTests.All.SoftDelete;

/// <summary>
/// A test-only module with one soft-deletable aggregate covering every shape the interceptor walks: a child
/// entity in its own table (<see cref="Slot"/>, like DeviceModel), an owned collection in its own table
/// (<see cref="Crate.Tags"/>, like Role.Permissions), and a unique alternate key (<see cref="Crate.Code"/>).
/// Mapped into its own test_soft_delete schema through the real AddModuleDbContext, so the interceptors are
/// the host's own. The registry comes from an explicit type list, as in TestAuditDbContext.
/// </summary>
internal interface ITestSoftDeleteDbContext : IDbContext;

internal interface ITestSoftDeleteUnitOfWork : IUnitOfWork;

internal sealed class TestSoftDeleteDbContext(DbContextOptions<TestSoftDeleteDbContext> options)
    : SerginDbContext(options), ITestSoftDeleteDbContext, ITestSoftDeleteUnitOfWork
{
    public const string Schema = "test_soft_delete";

    public DbSet<Crate> Crates => Set<Crate>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(CrateAggregateFeatureConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Crate>(crate =>
        {
            crate.ToTable("crates");
            crate.HasKey(x => x.Id);
            crate.Property(x => x.Code);
            crate.HasIndex(x => x.Code).IsUnique();

            crate.HasMany(x => x.Slots)
                .WithOne()
                .HasForeignKey(slot => slot.CrateId)
                .OnDelete(DeleteBehavior.Cascade);
            crate.Navigation(x => x.Slots).HasField("slots").UsePropertyAccessMode(PropertyAccessMode.Field);

            crate.OwnsMany(x => x.Tags, tag =>
            {
                tag.ToTable("crate_tags");
                tag.WithOwner().HasForeignKey("crate_id");
                tag.Property(x => x.Value);
                tag.HasKey("crate_id", nameof(Tag.Value));
            });
            crate.Navigation(x => x.Tags).HasField("tags").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Slot>(slot =>
        {
            slot.ToTable("slots");
            slot.HasKey(x => x.Id);
            slot.Property(x => x.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class Crate : AggregateRoot<Guid>
{
    private readonly List<Slot> slots = [];
    private readonly List<Tag> tags = [];

    private Crate()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyCollection<Slot> Slots => slots;

    public IReadOnlyCollection<Tag> Tags => tags;

    public static Crate Create(string code, int slotCount = 0, params string[] tagValues)
    {
        Crate crate = new() { Id = Guid.CreateVersion7(), Code = code };

        for (int i = 0; i < slotCount; i++)
        {
            crate.slots.Add(Slot.Create(crate.Id, $"slot-{i}"));
        }

        crate.tags.AddRange(tagValues.Select(value => new Tag(value)));
        return crate;
    }

    public void RemoveSlot(Slot slot) => slots.Remove(slot);
}

internal sealed class Slot : Entity<Guid>
{
    private Slot()
    {
    }

    public Guid CrateId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    internal static Slot Create(Guid crateId, string name) =>
        new() { Id = Guid.CreateVersion7(), CrateId = crateId, Name = name };
}

internal sealed record Tag(string Value);

internal sealed class CrateAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Crate>
{
    public void Configure(AggregateFeatureBuilder<Crate> builder) => builder.Audited().SoftDeletable();
}
