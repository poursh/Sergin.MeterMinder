using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Activity;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// What the activity indicator shows for each run, built without a browser from the real host's scheduled command
/// catalogue and view registry: the command's view label, the target's stored label or a record count, the target's
/// detail link, and the run's state.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ActivityRowsTests(SerginWebApiFactory<Program> factory)
{
    private const string ReadProfile = "dm.devices.read-profile";

    [Fact]
    public void AOneDeviceReadProfile_HasTheReadProfileLabel_TheDeviceLabel_AndTheDeviceLink()
    {
        var device = Guid.CreateVersion7();

        ActivityRow row = Build(Item(ReadProfile, total: 1, unfinished: 1, targetId: device, label: "MTR-1"));

        Assert.Equal(DeviceUiTexts.ReadProfile, row.CommandLabel);
        Assert.Equal("MTR-1", row.Target);
        Assert.Equal($"/dm/devices/{device}", row.Href);
    }

    [Fact]
    public void AThreeTargetRun_CountsItsRecords_AndLinksNowhere()
    {
        ActivityRow row = Build(Item(ReadProfile, total: 3, unfinished: 3));

        Assert.Equal("3 records", row.Target);
        Assert.Null(row.Href);
        Assert.Equal((0, 3), (row.Done, row.Total));
    }

    [Fact]
    public void AOneTargetRunWithNoLabel_StartedBeforeLabelsWereStored_SaysOneRecord()
    {
        ActivityRow row = Build(Item(ReadProfile, total: 1, unfinished: 0, succeeded: 1, targetId: Guid.CreateVersion7(), label: null));

        Assert.Equal("1 record", row.Target);
    }

    [Theory]
    [InlineData(3, 1, 0, 2, "Running")]
    [InlineData(3, 3, 0, 0, "Succeeded")]
    [InlineData(3, 0, 3, 0, "Failed")]
    [InlineData(3, 2, 1, 0, "PartlyFailed")]
    public void TheState_FollowsTheCounts(int total, int succeeded, int failed, int unfinished, string expected)
    {
        ActivityRow row = Build(Item(ReadProfile, total, unfinished, succeeded, failed));

        Assert.Equal(expected, row.State.ToString());
        Assert.Equal(succeeded + failed, row.Done);
    }

    [Fact]
    public void AnUnknownCommand_HasNoLabel_AndKeepsItsName()
    {
        ActivityRow row = Build(Item("xx.gone.command", total: 1, unfinished: 1, targetId: Guid.CreateVersion7(), label: "X-1"));

        Assert.Null(row.CommandLabel);
        Assert.Equal("xx.gone.command", row.CommandName);
        Assert.Null(row.Href);
    }

    private ActivityRow Build(ActivityItem item)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Func<int, string> records = ActivityRows.RecordsText(
            scope.ServiceProvider.GetRequiredService<IStringLocalizer>(), scope.ServiceProvider.GetRequiredService<ICultureFormatter>());

        return Assert.Single(ActivityRows.Build(
            [item],
            factory.Services.GetRequiredService<ScheduledCommandCatalog>(),
            factory.Services.GetRequiredService<AggregateViewRegistry>(),
            records));
    }

    private static ActivityItem Item(
        string command, int total, int unfinished, int succeeded = 0, int failed = 0, Guid? targetId = null, string? label = null) =>
        new(
            new BackgroundRunRef("dm", Guid.CreateVersion7()),
            command,
            ScheduledRunTrigger.Background,
            unfinished > 0 ? ScheduledRunStatus.Running : ScheduledRunStatus.Completed,
            DateTime.UtcNow.AddMinutes(-1),
            unfinished > 0 ? null : DateTime.UtcNow,
            total,
            succeeded,
            failed,
            unfinished,
            total == 1 ? targetId : null,
            null,
            total == 1 ? label : null,
            null);
}
