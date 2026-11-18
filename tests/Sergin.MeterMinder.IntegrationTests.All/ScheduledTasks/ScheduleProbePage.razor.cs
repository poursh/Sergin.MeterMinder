using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Actions;
using Sergin.SharedKernel.Presentation.Blazor.Navigation;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

public sealed partial class ScheduleProbePage
{
    public const string Schema = ScheduledTasksDbContext.Schema;

    private SerginScheduledTasksSection section = default!;
    private IReadOnlyList<SerginPageAction> actions = [];
    private string note = "scheduled";
    private int count;

    [Parameter]
    public Guid Id { get; set; }

    [SupplyParameterFromQuery(Name = "schedules")]
    private string? commandName { get; set; }

    // ?label=true names the filtered command by its label, the way a generated detail view does.
    [SupplyParameterFromQuery(Name = "label")]
    private bool labelled { get; set; }

    [Inject]
    private IScopeAuthorization ScopeAuthorization { get; set; } = default!;

    [Inject]
    private IDialogService Dialogs { get; set; } = default!;

    private TextDefinition? CommandLabel => labelled ? ScheduleProbeUiTexts.CommandTitle : null;

    private static IReadOnlyList<SerginBreadcrumb> Trail => [new(ScheduleProbeUiTexts.Title)];

    // The note field the dialog shows for the command's own argument; the page keeps what is typed.
    private RenderFragment ArgumentsForm => builder =>
    {
        builder.OpenComponent<ScheduleProbeNoteField>(0);
        builder.AddAttribute(1, nameof(ScheduleProbeNoteField.Value), note);
        builder.AddAttribute(2, nameof(ScheduleProbeNoteField.ValueChanged), EventCallback.Factory.Create<string>(this, value => note = value));
        builder.CloseComponent();
    };

    protected override async Task OnInitializedAsync()
    {
        // What a generated view builds for a schedulable command: the platform permission gates the action,
        // the command's own permission is the action's.
        bool mayManage = await ScopeAuthorization.CurrentUserHasAsync(ScheduledTaskPermissions.Manage);

        actions = mayManage
            ?
            [
                new SerginPageAction(
                    ScheduledTaskUiTexts.ScheduleAction,
                    Icons.Material.Outlined.Schedule,
                    SerginActionKind.Secondary,
                    OnClick: EventCallback.Factory.Create(this, ScheduleAsync),
                    Permission: ScheduleProbePermissions.Touch),
            ]
            : [];
    }

    private void OnLoaded() => count = section.CountOf("tst.probes.touch");

    private async Task ScheduleAsync()
    {
        IReadOnlyCollection<ScheduledTaskTarget> targets = [new ScheduledTaskTarget(Id, null)];

        bool saved = await Dialogs.ShowScheduleAsync(new SerginScheduleRequest(
            Schema,
            ScheduleProbeUiTexts.CommandTitle,
            () => (IBaseCommand)new TouchProbeCommand(new ScheduleProbeId(Id), new ProbeNote(note)),
            targets,
            [Id.ToString()],
            ArgumentsForm));

        if (saved)
        {
            await section.ReloadAsync();
        }
    }
}
