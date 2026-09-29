using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// A test-only module with one versioned aggregate: <see cref="Pallet"/> with a child entity in its own table
/// (<see cref="Bay"/>, like DeviceModel). Audited and soft-deletable too, so the row-version interceptors run
/// alongside the real audit and soft-delete ones. <see cref="Pallet.Nudge"/> raises an event whose handler
/// renames another pallet, for the "bumped but not checked" case. Mapped into its own test_versioned schema
/// through the real AddModuleDbContext.
/// </summary>
internal interface ITestVersionedDbContext : IDbContext;

internal interface ITestVersionedUnitOfWork : IUnitOfWork;

internal sealed class TestVersionedDbContext(DbContextOptions<TestVersionedDbContext> options)
    : SerginDbContext(options), ITestVersionedDbContext, ITestVersionedUnitOfWork
{
    public const string Schema = "test_versioned";

    public DbSet<Pallet> Pallets => Set<Pallet>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(PalletAggregateFeatureConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Pallet>(pallet =>
        {
            pallet.ToTable("pallets");
            pallet.HasKey(x => x.Id);
            pallet.Property(x => x.Code);

            pallet.HasMany(x => x.Bays)
                .WithOne()
                .HasForeignKey(bay => bay.PalletId)
                .OnDelete(DeleteBehavior.Cascade);
            pallet.Navigation(x => x.Bays).HasField("bays").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Bay>(bay =>
        {
            bay.ToTable("bays");
            bay.HasKey(x => x.Id);
            bay.Property(x => x.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class Pallet : AggregateRoot<Guid>
{
    private readonly List<Bay> bays = [];

    private Pallet()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyCollection<Bay> Bays => bays;

    public static Pallet Create(string code) => new() { Id = Guid.CreateVersion7(), Code = code };

    public void Rename(string code) => Code = code;

    public void AddBay(string name) => bays.Add(Bay.Create(Id, name));

    public void Nudge(Guid neighbourId) =>
        Raise(new PalletNudged(Guid.CreateVersion7(), DateTime.UtcNow, neighbourId));
}

internal sealed class Bay : Entity<Guid>
{
    private Bay()
    {
    }

    public Guid PalletId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    internal static Bay Create(Guid palletId, string name) =>
        new() { Id = Guid.CreateVersion7(), PalletId = palletId, Name = name };
}

internal sealed record PalletNudged(Guid Id, DateTime OccurredOnUtc, Guid NeighbourId) : IDomainEvent;

internal sealed class PalletAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Pallet>
{
    public void Configure(AggregateFeatureBuilder<Pallet> builder) => builder.Audited().SoftDeletable().Versioned();
}
