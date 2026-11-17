using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.ConfirmGuards;

// The refusals of confirms and deletes that ask for values: a delete whose extra value is not shown, so its confirm dialog
// cannot fill it, and a Confirm(...) on a command no view asks before sending. Their own namespace, so no host scans
// them; a test feeds a configuration to AggregateViewTestRegistry.Build by name. Test-only roots throughout.

// ---- Refusal: a delete whose extra value is NotShown ----

internal sealed record HiddenValueProbeId(Guid Value);

internal sealed class HiddenValueProbe : AggregateRoot<HiddenValueProbeId>;

public static class HiddenValueProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "HiddenValueProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Hidden value probe");

    public static FieldText Cascade { get; } = Category.Field(nameof(Cascade), "Cascade");

    public static TextDefinition Confirm { get; } = Category.Text("Confirm", "Sure?");
}

internal sealed class HiddenValueProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<HiddenValueProbe>
{
    public void Configure(AggregateFeatureBuilder<HiddenValueProbe> builder) =>
        builder.Texts(HiddenValueProbeTexts.Category).Named(HiddenValueProbeTexts.Singular);
}

internal sealed record DeleteHiddenValueProbeCommand(HiddenValueProbeId Id, bool Cascade)
    : ICommand, IDeleteCommand<HiddenValueProbe, HiddenValueProbeId>;

/// <summary>The extra value is NotShown, so the confirm dialog would send it at its default without asking.</summary>
internal sealed class DeleteHiddenValueProbeCommandConfiguration : ICommandConfiguration<DeleteHiddenValueProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<DeleteHiddenValueProbeCommand> builder) => builder.NotShown(command => command.Cascade);
}

internal sealed class HiddenValueProbeViewConfiguration : IAggregateViewConfiguration<HiddenValueProbe>
{
    public void Configure(AggregateViewBuilder<HiddenValueProbe> view)
    {
    }
}

/// <summary>The same delete, hidden: nothing asks for its value, so the refusal does not apply.</summary>
internal sealed class HiddenDeleteViewConfiguration : IAggregateViewConfiguration<HiddenValueProbe>
{
    public void Configure(AggregateViewBuilder<HiddenValueProbe> view) => view.Command<DeleteHiddenValueProbeCommand>().Hidden();
}

// ---- Refusal: Confirm(...) on a command no view asks before sending ----

/// <summary>An update links to its edit page, so a confirm on it would never be asked.</summary>
internal sealed class ConfirmOnUpdateViewConfiguration : IAggregateViewConfiguration<PlainProbe>
{
    public void Configure(AggregateViewBuilder<PlainProbe> view) =>
        view.Command<UpdatePlainProbeCommand>().Confirm(HiddenValueProbeTexts.Singular, HiddenValueProbeTexts.Confirm);
}

internal static class ConfirmGuardTypes
{
    public static Type[] AggregateConfigurations { get; } = [typeof(HiddenValueProbeAggregateFeatureConfiguration)];

    public static Type[] Requests { get; } = [typeof(DeleteHiddenValueProbeCommand), typeof(DeleteHiddenValueProbeCommandConfiguration)];

    public static Type[] Texts { get; } = [typeof(HiddenValueProbeTexts)];
}
