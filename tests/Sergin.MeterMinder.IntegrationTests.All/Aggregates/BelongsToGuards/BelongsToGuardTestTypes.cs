using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Configuration;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.BelongsToGuards;

// The refusals of parents and list routes: BelongsTo naming a parent with no detail route, beside ListUnder, or reading
// another response than the GetOne query's; a form's Parent(...) naming no constructor parameter or one that wraps no
// Guid; and a list route on a root with neither a list query nor a tree query. Their own namespace, so no host scans
// them; a test feeds a configuration to AggregateViewTestRegistry.Build by name. Test-only roots throughout.

// ---- Refusal: a list route on a root with neither an IListOf nor an ITreeQuery ----

/// <summary>The edit probe has a GetOne query and nothing to list or read as a tree.</summary>
internal sealed class ListRouteWithoutListOrTreeViewConfiguration : IAggregateViewConfiguration<EditProbe>
{
    public void Configure(AggregateViewBuilder<EditProbe> view) =>
        view.Routes("/tst/edit-probes", id => $"/tst/edit-probes/{id}");
}

// ---- Refusals: BelongsTo ----

/// <summary>Belongs to the edit probe, whose view configuration declares no routes, so no detail page to link to.</summary>
internal sealed class BelongsToUnroutedParentViewConfiguration : IAggregateViewConfiguration<ChildProbe>
{
    public void Configure(AggregateViewBuilder<ChildProbe> view)
    {
        view.BelongsTo<EditProbe, ChildProbeResponse>(child => child.OwnerId, child => child.OwnerName);
        view.Form<CreateChildProbeCommand>(form =>
        {
            form.Parent(command => command.OwnerId);
            form.Field(command => command.Tags).Component<ChildProbeTagsInput>();
        });
    }
}

/// <summary>A root listed under a parent declares a second parent rule.</summary>
internal sealed class BelongsToWithListUnderViewConfiguration : IAggregateViewConfiguration<ViewProbeNote>
{
    public void Configure(AggregateViewBuilder<ViewProbeNote> view)
    {
        view.ListUnder<ViewProbe>(probe => $"/tst/view-probes/{probe}/notes");
        view.BelongsTo<ViewProbe, ViewProbeNoteListItem>(note => note.Id, note => note.Text);
    }
}

/// <summary>Reads the parent from the parent's response, not from the child's GetOne response.</summary>
internal sealed class BelongsToWrongResponseViewConfiguration : IAggregateViewConfiguration<ChildProbe>
{
    public void Configure(AggregateViewBuilder<ChildProbe> view)
    {
        view.BelongsTo<ParentProbe, ParentProbeResponse>(parent => parent.ParentId, parent => parent.Name);
        view.Form<CreateChildProbeCommand>(form =>
        {
            form.Parent(command => command.OwnerId);
            form.Field(command => command.Tags).Component<ChildProbeTagsInput>();
        });
    }
}

// ---- Refusals: a form's Parent(...) ----

/// <summary>Names the string Name as the parent, which wraps no Guid.</summary>
internal sealed class ParentOnStringViewConfiguration : IAggregateViewConfiguration<ChildProbe>
{
    public void Configure(AggregateViewBuilder<ChildProbe> view) =>
        view.Form<CreateChildProbeCommand>(form =>
        {
            form.Parent(command => command.Name);
            form.Field(command => command.Tags).Component<ChildProbeTagsInput>();
        });
}

internal sealed record ExtraParentProbeId(Guid Value);

internal sealed class ExtraParentProbe : AggregateRoot<ExtraParentProbeId>;

public static class ExtraParentProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "ExtraParentProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Extra parent probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class ExtraParentProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<ExtraParentProbe>
{
    public void Configure(AggregateFeatureBuilder<ExtraParentProbe> builder) =>
        builder.Texts(ExtraParentProbeTexts.Category).Named(ExtraParentProbeTexts.Singular);
}

/// <summary>Carries a Guid property its constructor does not take: a form could not fill it.</summary>
internal sealed record CreateExtraParentProbeCommand(string Name) : ICommand, ICreateCommand<ExtraParentProbe, ExtraParentProbeId>
{
    public Guid Extra { get; init; }
}

/// <summary>Names a property that is no constructor parameter as the parent.</summary>
internal sealed class ParentOnUnknownViewConfiguration : IAggregateViewConfiguration<ExtraParentProbe>
{
    public void Configure(AggregateViewBuilder<ExtraParentProbe> view) =>
        view.Form<CreateExtraParentProbeCommand>(form => form.Parent(command => command.Extra));
}

internal static class BelongsToGuardTypes
{
    public static Type[] AggregateConfigurations { get; } = [typeof(ExtraParentProbeAggregateFeatureConfiguration)];

    public static Type[] Requests { get; } = [typeof(CreateExtraParentProbeCommand)];

    public static Type[] Texts { get; } = [typeof(ExtraParentProbeTexts)];
}
