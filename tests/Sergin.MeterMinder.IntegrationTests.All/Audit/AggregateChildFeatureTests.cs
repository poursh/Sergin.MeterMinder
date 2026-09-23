using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// How a root's features reach its child entities: by default every entity reached through the root's
/// navigations takes them, down to grandchildren and owned collections; the walk stops at another aggregate
/// root; an owned type sharing its owner's table gets no columns of its own; ExceptChild leaves out only the
/// named type; and the two misdeclarations the convention can see — excepting a type that is not a child,
/// and one entity reached from two roots with different features — fail the model build naming the types.
/// No database is needed: UseNpgsql("Host=unused") only lets each context build its .Model, and EF caches a
/// model per context type, hence one context class per scenario.
/// </summary>
public sealed class AggregateChildFeatureTests
{
    [Fact]
    public void ChildrenAndGrandchildren_TakeTheRootsFeatures()
    {
        using DefaultChildContext context = new(Options<DefaultChildContext>());

        Assert.True(IsAudited(context, typeof(Order)));
        Assert.True(IsAudited(context, typeof(OrderLine)));
        Assert.True(IsAudited(context, typeof(LineNote)));
        Assert.True(IsAudited(context, typeof(OrderTag)));
    }

    [Fact]
    public void AnotherAggregateRoot_ReachedByANavigation_IsNotAudited()
    {
        using DefaultChildContext context = new(Options<DefaultChildContext>());

        Assert.False(IsAudited(context, typeof(Shipment)));
    }

    [Fact]
    public void OwnedTypeInItsOwnersTable_GetsNoColumnsOfItsOwn()
    {
        using DefaultChildContext context = new(Options<DefaultChildContext>());

        IEntityType address = Assert.Single(
            context.Model.GetEntityTypes(), entityType => entityType.ClrType == typeof(OrderAddress));

        Assert.Null(address.FindProperty(AuditColumns.CreatedAtUtc));
    }

    [Fact]
    public void ExceptChild_LeavesOutOnlyThatType()
    {
        using ExceptLineContext context = new(Options<ExceptLineContext>());

        Assert.True(IsAudited(context, typeof(Order)));
        Assert.False(IsAudited(context, typeof(OrderLine)));
        Assert.Null(context.Model.FindEntityType(typeof(OrderLine))!.FindProperty(AuditColumns.CreatedAtUtc));
        Assert.True(IsAudited(context, typeof(LineNote)));
    }

    [Fact]
    public void ExceptChild_OfATypeThatIsNotAChild_FailsTheModelBuild()
    {
        using StrayExceptionContext context = new(Options<StrayExceptionContext>());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains(typeof(Stranger).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Order).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptChild_OfAnAggregateRoot_IsRefusedByTheRegistry()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(OrderExceptingShipment)]));

        Assert.Contains(typeof(Shipment).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("aggregate root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildReachedFromTwoRootsWithDifferentFeatures_FailsTheModelBuild()
    {
        using ConflictingRootsContext context = new(Options<ConflictingRootsContext>());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains(nameof(OrderLine), error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Order).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Warehouse).FullName!, error.Message, StringComparison.Ordinal);
    }

    private static bool IsAudited(DbContext context, Type type) =>
        AuditColumns.IsAudited(Assert.IsAssignableFrom<IEntityType>(context.Model.FindEntityType(type)));

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=unused").Options;
}

internal abstract class ChildFeatureTestContext(DbContextOptions options) : SerginDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.HasKey(x => x.Id);
            order.HasMany(x => x.Lines).WithOne();
            order.HasMany(x => x.Shipments).WithOne();
            order.OwnsOne(x => x.Address);
            order.OwnsMany(x => x.Tags, tag => tag.ToTable("order_tags"));
        });

        modelBuilder.Entity<OrderLine>(line =>
        {
            line.ToTable("order_lines");
            line.HasKey(x => x.Id);
            line.HasMany(x => x.Notes).WithOne();
        });

        modelBuilder.Entity<LineNote>(note =>
        {
            note.ToTable("line_notes");
            note.HasKey(x => x.Id);
        });

        modelBuilder.Entity<Shipment>(shipment =>
        {
            shipment.ToTable("shipments");
            shipment.HasKey(x => x.Id);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class DefaultChildContext(DbContextOptions<DefaultChildContext> options) : ChildFeatureTestContext(options)
{
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(OrderAudited)]);
}

internal sealed class ExceptLineContext(DbContextOptions<ExceptLineContext> options) : ChildFeatureTestContext(options)
{
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(OrderExceptingLines)]);
}

internal sealed class StrayExceptionContext(DbContextOptions<StrayExceptionContext> options) : ChildFeatureTestContext(options)
{
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(OrderExceptingStranger)]);
}

internal sealed class ConflictingRootsContext(DbContextOptions<ConflictingRootsContext> options) : ChildFeatureTestContext(options)
{
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(OrderAudited), typeof(WarehouseWithNoFeatures)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Warehouse>(warehouse =>
        {
            warehouse.ToTable("warehouses");
            warehouse.HasKey(x => x.Id);
            warehouse.HasMany(x => x.Lines).WithOne();
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class Order : AggregateRoot<Guid>
{
    public ICollection<OrderLine> Lines { get; } = [];

    public ICollection<Shipment> Shipments { get; } = [];

    public OrderAddress Address { get; set; } = new();

    public ICollection<OrderTag> Tags { get; } = [];
}

internal sealed class OrderLine : Entity<Guid>
{
    public ICollection<LineNote> Notes { get; } = [];
}

internal sealed class LineNote : Entity<Guid>;

internal sealed class OrderAddress
{
    public string Street { get; set; } = string.Empty;
}

internal sealed class OrderTag
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class Shipment : AggregateRoot<Guid>;

internal sealed class Warehouse : AggregateRoot<Guid>
{
    public ICollection<OrderLine> Lines { get; } = [];
}

internal sealed class Stranger : Entity<Guid>;

internal sealed class OrderAudited : IAggregateFeatureConfiguration<Order>
{
    public void Configure(AggregateFeatureBuilder<Order> builder) => builder.Audited();
}

internal sealed class OrderExceptingLines : IAggregateFeatureConfiguration<Order>
{
    public void Configure(AggregateFeatureBuilder<Order> builder) =>
        builder.Audited(audit => audit.ExceptChild<OrderLine>());
}

internal sealed class OrderExceptingStranger : IAggregateFeatureConfiguration<Order>
{
    public void Configure(AggregateFeatureBuilder<Order> builder) =>
        builder.Audited(audit => audit.ExceptChild<Stranger>());
}

internal sealed class OrderExceptingShipment : IAggregateFeatureConfiguration<Order>
{
    public void Configure(AggregateFeatureBuilder<Order> builder) =>
        builder.Audited(audit => audit.ExceptChild<Shipment>());
}

internal sealed class WarehouseWithNoFeatures : IAggregateFeatureConfiguration<Warehouse>
{
    public void Configure(AggregateFeatureBuilder<Warehouse> builder)
    {
    }
}
