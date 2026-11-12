using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Navigation;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

public sealed partial class ScheduleProbePage
{
    public const string Schema = ScheduledTasksDbContext.Schema;

    private SerginScheduledTasksSection section = default!;
    private IReadOnlyList<SerginPageAction> actions = [];
    private string note = "scheduled";

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private IScopeAuthorization ScopeAuthorization { get; set; } = default!;

    [Inject]
    private IDialogService Dialogs { get; set; } = default!;

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
        SerginPageAction? schedule = await SerginScheduleAction.PageAsync(
            ScopeAuthorization, EventCallback.Factory.Create(this, ScheduleAsync), ScheduleProbePermissions.Touch);

        actions = schedule is null ? [] : [schedule];
    }

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
