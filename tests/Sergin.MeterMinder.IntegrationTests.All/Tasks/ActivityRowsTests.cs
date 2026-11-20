using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Activity;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Tasks;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// What the activity indicator shows for each run, built without a browser from the real host's scheduled command
/// catalogue and view registry: the command's view label, the target's stored label or a record count, the target's
/// detail link, and the run's state.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ActivityRowsTests(SerginWebApiFactory<Program> factory)
{
    private const string ReadProfile = "dm.device.read-device-profile";

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

    // A run's succeeded, failed and cancelled counts are written only when it completes, so a running run carries none:
    // its bar counts the items that no longer wait.
    [Theory]
    [InlineData(3, 0, 0, 2, 0, "Running", 1)]
    [InlineData(3, 0, 0, 3, 0, "Running", 0)]
    [InlineData(3, 3, 0, 0, 0, "Succeeded", 3)]
    [InlineData(3, 0, 3, 0, 0, "Failed", 3)]
    [InlineData(3, 2, 1, 0, 0, "PartlyFailed", 3)]
    [InlineData(3, 0, 0, 0, 3, "Cancelled", 0)]
    [InlineData(1, 0, 0, 0, 1, "Cancelled", 0)]
    [InlineData(3, 2, 0, 0, 1, "Cancelled", 2)]
    [InlineData(3, 0, 1, 0, 2, "Failed", 1)]
    [InlineData(3, 1, 1, 0, 1, "PartlyFailed", 2)]
    public void TheState_FollowsTheCounts(
        int total, int succeeded, int failed, int unfinished, int cancelled, string expected, int expectedDone)
    {
        ActivityRow row = Build(Item(ReadProfile, total, unfinished, succeeded, failed, cancelled: cancelled));

        Assert.Equal(expected, row.State.ToString());
        Assert.Equal(expectedDone, row.Done);
        Assert.Equal(cancelled, row.Cancelled);
    }

    [Fact]
    public void AOneTargetFailure_CarriesItsStoredErrorDescription_AsTheRunsDialogShowsIt()
    {
        ActivityRow row = Build(Item(
            ReadProfile, total: 1, unfinished: 0, failed: 1, targetId: Guid.CreateVersion7(), label: "MTR-1",
            errorCode: "Tasks.TimedOut", errorDescription: "Timed out after 00:10:00 without progress."));

        Assert.Equal(ActivityState.Failed, row.State);
        Assert.Equal("Tasks.TimedOut", row.FirstErrorCode);
        Assert.Equal("Timed out after 00:10:00 without progress.", row.FirstErrorDescription);
    }

    [Fact]
    public void ModuleCaption_IsTheModulesCaption()
    {
        // Device Management is a group of one, so it declares no caption: its group's title names it.
        ActivityRow device = Build(Item(ReadProfile, total: 1, unfinished: 1, targetId: Guid.CreateVersion7(), label: "MTR-1"));
        ActivityRow unknown = Build(Item(ReadProfile, total: 1, unfinished: 1, schema: "xx"));

        Assert.Equal(DeviceManagementUiTexts.MenuTitle, device.ModuleCaption);
        Assert.Null(unknown.ModuleCaption);
    }

    [Fact]
    public void Progress_IsCarried_ForOneRecord()
    {
        var device = Guid.CreateVersion7();

        ActivityRow row = Build(Item(ReadProfile, total: 1, unfinished: 1, targetId: device, label: "MTR-1", progress: 40));

        Assert.Equal(40, row.Progress);
        Assert.Equal(device, row.TargetId);
    }

    [Fact]
    public void AnUnknownCommand_HasNoLabel_AndKeepsItsName()
    {
        ActivityRow row = Build(Item("xx.gone.command", total: 1, unfinished: 1, targetId: Guid.CreateVersion7(), label: "X-1"));

        Assert.Null(row.CommandLabel);
        Assert.Equal("xx.gone.command", row.CommandName);
        Assert.Null(row.Href);
    }

    [Fact]
    public void ACommandAViewNames_IsShownByThatViewCommandsIcon()
    {
        string? icon = ActivityRows.CommandIcon(
            ReadProfile,
            factory.Services.GetRequiredService<TaskCatalog>(),
            factory.Services.GetRequiredService<AggregateViewRegistry>());

        Assert.Equal(MudBlazor.Icons.Material.Outlined.Sensors, icon);
    }

    [Fact]
    public void ACommandTheCatalogueDoesNotKnow_HasNoIcon() =>
        Assert.Null(ActivityRows.CommandIcon(
            "xx.gone.command",
            factory.Services.GetRequiredService<TaskCatalog>(),
            factory.Services.GetRequiredService<AggregateViewRegistry>()));

    private ActivityRow Build(ActivityItem item)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Func<int, string> records = ActivityRows.RecordsText(
            scope.ServiceProvider.GetRequiredService<IStringLocalizer>(), scope.ServiceProvider.GetRequiredService<ICultureFormatter>());

        return Assert.Single(ActivityRows.Build(
            [item],
            factory.Services.GetRequiredService<TaskCatalog>(),
            factory.Services.GetRequiredService<AggregateViewRegistry>(),
            records,
            ActivityRows.ModuleCaption(factory.Services.GetRequiredService<SerginUiModuleCatalog>())));
    }

    private static ActivityItem Item(
        string command,
        int total,
        int unfinished,
        int succeeded = 0,
        int failed = 0,
        Guid? targetId = null,
        string? label = null,
        string? errorCode = null,
        string? errorDescription = null,
        int cancelled = 0,
        int? progress = null,
        string schema = "dm") =>
        new(
            new TaskRunRef(schema, Guid.CreateVersion7()),
            command,
            TaskRunTrigger.Manual,
            unfinished > 0 ? TaskRunStatus.Running : TaskRunStatus.Completed,
            DateTime.UtcNow.AddMinutes(-1),
            unfinished > 0 ? null : DateTime.UtcNow,
            total,
            succeeded,
            failed,
            unfinished,
            total == 1 ? targetId : null,
            null,
            total == 1 ? label : null,
            total == 1 ? errorCode : null,
            total == 1 ? errorDescription : null,
            Cancelled: cancelled,
            Progress: total == 1 ? progress : null);
}
