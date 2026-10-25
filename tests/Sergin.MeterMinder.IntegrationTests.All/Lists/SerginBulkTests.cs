using ErrorOr;
using MudBlazor;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.IntegrationTests.All.Lists;

/// <summary>
/// The loop behind a list's bulk actions, without a database: every item is sent in order, a refusal does not stop
/// the others, a stop rule ends the run and counts the rest as skipped, and identical errors are reported once.
/// </summary>
public sealed class SerginBulkTests
{
    private static readonly Error InUse = Error.Validation("DeviceModelId", "'Device Model Id' is in use.");
    private static readonly Error Stale = Error.Custom(412, "General.VersionStale", "stale");

    [Fact]
    public async Task RunAsync_SendsEveryItemInOrder_AndCollectsTheRefusals()
    {
        List<int> sent = [];

        SerginBulkOutcome outcome = await SerginBulk.RunAsync<int>(
            [1, 2, 3, 4],
            item =>
            {
                sent.Add(item);
                return Task.FromResult<ErrorOr<Success>>(item % 2 == 0 ? InUse : Result.Success);
            });

        Assert.Equal([1, 2, 3, 4], sent);
        Assert.Equal(2, outcome.Succeeded);
        Assert.Equal([InUse, InUse], outcome.Errors);
        Assert.Equal(0, outcome.Skipped);
    }

    [Fact]
    public async Task RunAsync_StopsAtAnErrorTheStopRuleNames_AndCountsTheRestAsSkipped()
    {
        List<int> sent = [];

        SerginBulkOutcome outcome = await SerginBulk.RunAsync<int>(
            [1, 2, 3, 4],
            item =>
            {
                sent.Add(item);
                return Task.FromResult<ErrorOr<Success>>(item == 2 ? Stale : Result.Success);
            },
            error => error.NumericType == 412);

        Assert.Equal([1, 2], sent);
        Assert.Equal(1, outcome.Succeeded);
        Assert.Equal([Stale], outcome.Errors);
        Assert.Equal(2, outcome.Skipped);
    }

    [Fact]
    public void Report_ShowsTheCount_EachDistinctErrorOnce_AndTheSkipped()
    {
        RecordingPresenter presenter = new();
        using SnackbarService snackbar = new(new NavigationManagerStub(), TimeProvider.System);

        new SerginBulkOutcome(3, [InUse, InUse, Stale], 2).Report(presenter, snackbar, "deleted");

        Assert.Equal([InUse, Stale], presenter.Notified);
        Assert.Equal(["3 deleted", "2 not sent"], snackbar.ShownSnackbars.Select(shown => shown.Message).ToList());
    }

    [Fact]
    public void Report_ShowsNoSuccess_WhenNothingSucceeded()
    {
        RecordingPresenter presenter = new();
        using SnackbarService snackbar = new(new NavigationManagerStub(), TimeProvider.System);

        new SerginBulkOutcome(0, [InUse], 0).Report(presenter, snackbar, "deleted");

        Assert.Equal([InUse], presenter.Notified);
        Assert.Empty(snackbar.ShownSnackbars);
    }

    private sealed class RecordingPresenter : IUiErrorPresenter
    {
        public List<Error> Notified { get; } = [];

        public SerginProblem Present(Error error) => throw new NotSupportedException();

        public void Notify(Error error) => Notified.Add(error);

        public void Notify(IReadOnlyList<Error> errors) => Notified.AddRange(errors);
    }

    private sealed class NavigationManagerStub : Microsoft.AspNetCore.Components.NavigationManager
    {
        public NavigationManagerStub() => Initialize("http://localhost/", "http://localhost/");
    }
}
