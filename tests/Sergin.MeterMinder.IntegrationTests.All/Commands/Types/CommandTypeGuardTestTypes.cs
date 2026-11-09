using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

// Records the shape guard refuses (or accepts), one per rule.

internal sealed record TwoEffectsCommand(TestTypedRootId Id)
    : IDeleteCommand<TestTypedRoot, TestTypedRootId>, ITransitionCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record SelectableCreateCommand(string Name)
    : ICreateCommand<TestTypedRoot, TestTypedRootId>, ISelectable, ICommand<TestCreatedResponse>;

internal sealed record SelectableAddChildCommand(TestTypedRootId RootId, string Name)
    : IAddChildCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ISelectable, ICommand<TestCreatedResponse>;

internal sealed record SetTwoPropertiesCommand(TestTypedRootId Id, string Name, string Other)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record SetNoPropertyCommand(TestTypedRootId Id)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId>, ICommand;

/// <summary>Two non-id properties, one marked NotShown: exactly one shown, so it passes.</summary>
internal sealed record SetPropertyWithHiddenCommand(TestTypedRootId Id, string Name, string Extra)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed class SetPropertyWithHiddenConfiguration : ICommandConfiguration<SetPropertyWithHiddenCommand>
{
    public void Configure(CommandConfigurationBuilder<SetPropertyWithHiddenCommand> builder) => builder.NotShown(x => x.Extra);
}

internal sealed record TransitionWithPropertyCommand(TestTypedRootId Id, string Reason)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record ReverseOfDeleteCommand(TestTypedRootId Id)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId>, IReverseOf<TestDeleteCommand>, ICommand;

internal sealed record OtherRootTransitionCommand(TestTypedRootId Id)
    : ITransitionCommand<TestOtherRoot, TestTypedRootId>, ICommand;

internal sealed record ReverseOfOtherRootCommand(TestTypedRootId Id)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId>, IReverseOf<OtherRootTransitionCommand>, ICommand;

internal sealed class SoftDeletableTypedRootConfiguration : IAggregateFeatureConfiguration<TestTypedRoot>
{
    public void Configure(AggregateFeatureBuilder<TestTypedRoot> builder) => builder.Audited().SoftDeletable();
}

// Field-text auto-hide: target ids need no FieldText, a parent id still does.

public static class AutoHideTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("DeviceManagement", "FieldTextAutoHideTests");

    public static FieldText Name { get; } = Category.Field(nameof(AutoHideChildCommand.Name), "Name");
}

internal sealed record AutoHideChildCommand(TestTypedRootId RootId, TestTypedChildId Id, string Name)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand;

internal sealed class AutoHideChildConfiguration : ICommandConfiguration<AutoHideChildCommand>
{
    public void Configure(CommandConfigurationBuilder<AutoHideChildCommand> builder) => builder.Texts(AutoHideTexts.Category);
}

internal sealed record AutoHideUnderParentCommand(string Name, TestTypedRootId ParentId)
    : ICreateCommand<TestTypedRoot, TestTypedRootId>, IUnderParent<TestTypedRoot, TestTypedRootId>, ICommand<TestCreatedResponse>;

internal sealed class AutoHideUnderParentConfiguration : ICommandConfiguration<AutoHideUnderParentCommand>
{
    public void Configure(CommandConfigurationBuilder<AutoHideUnderParentCommand> builder) => builder.Texts(AutoHideTexts.Category);
}

/// <summary>An untyped record with a property called Id: not a target member, so it is still shown.</summary>
internal sealed record AutoHideUntypedCommand(Guid Id, string Name) : ICommand;

internal sealed class AutoHideUntypedConfiguration : ICommandConfiguration<AutoHideUntypedCommand>
{
    public void Configure(CommandConfigurationBuilder<AutoHideUntypedCommand> builder) => builder.Texts(AutoHideTexts.Category);
}

// The mapping guard: a tiny model with a root, a child, a grandchild reached through the child, and an entity the
// model maps but no navigation of the root reaches.

internal sealed class MappingRoot : AggregateRoot<Guid>
{
    private readonly List<MappingChild> children = [];

    private MappingRoot()
    {
    }

    public IReadOnlyCollection<MappingChild> Children => children;

    // Referenced only so Sonar sees the private constructor used; the test reads the model, never a row.
    public static MappingRoot Create() => new() { Id = Guid.CreateVersion7() };
}

internal sealed class MappingChild : Entity<Guid>
{
    private readonly List<MappingGrandChild> grandChildren = [];

    private MappingChild()
    {
    }

    public IReadOnlyCollection<MappingGrandChild> GrandChildren => grandChildren;

    public static MappingChild Create() => new() { Id = Guid.CreateVersion7() };
}

internal sealed class MappingGrandChild : Entity<Guid>
{
    private MappingGrandChild()
    {
    }

    public static MappingGrandChild Create() => new() { Id = Guid.CreateVersion7() };
}

internal sealed class MappingLoose : Entity<Guid>
{
    private MappingLoose()
    {
    }

    public static MappingLoose Create() => new() { Id = Guid.CreateVersion7() };
}

internal sealed class MappingTestDbContext(DbContextOptions<MappingTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MappingRoot>(root =>
        {
            root.HasKey(x => x.Id);
            root.Ignore(x => x.DomainEvents);
            root.HasMany(x => x.Children).WithOne();
            root.Navigation(x => x.Children).HasField("children");
        });

        modelBuilder.Entity<MappingChild>(child =>
        {
            child.HasKey(x => x.Id);
            child.HasMany(x => x.GrandChildren).WithOne();
            child.Navigation(x => x.GrandChildren).HasField("grandChildren");
        });

        modelBuilder.Entity<MappingGrandChild>().HasKey(x => x.Id);
        modelBuilder.Entity<MappingLoose>().HasKey(x => x.Id);
    }
}

internal sealed record MappedDeleteCommand(Guid Id) : IDeleteCommand<MappingRoot, Guid>, ICommand<Deleted>;

internal sealed record MappedChildRemoveCommand(Guid RootId, Guid Id)
    : IRemoveChildCommand<MappingRoot, Guid, MappingChild, Guid>, ICommand<Deleted>;

internal sealed record MappedGrandChildRemoveCommand(Guid RootId, Guid Id)
    : IRemoveChildCommand<MappingRoot, Guid, MappingGrandChild, Guid>, ICommand<Deleted>;

internal sealed record UnreachableChildRemoveCommand(Guid RootId, Guid Id)
    : IRemoveChildCommand<MappingRoot, Guid, MappingLoose, Guid>, ICommand<Deleted>;
