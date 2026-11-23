using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Domain.Tasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

public sealed class RunOutcomeTests
{
    [Theory]
    [InlineData(TaskRunStatus.Skipped, 0, 0, 0, 0, RunOutcome.Skipped)]
    [InlineData(TaskRunStatus.Running, 3, 0, 0, 0, RunOutcome.Running)]
    [InlineData(TaskRunStatus.Completed, 0, 12, 0, 0, RunOutcome.Succeeded)]
    [InlineData(TaskRunStatus.Completed, 0, 11, 1, 0, RunOutcome.PartlyFailed)]
    [InlineData(TaskRunStatus.Completed, 0, 0, 2, 0, RunOutcome.Failed)]
    [InlineData(TaskRunStatus.Completed, 0, 25, 0, 15, RunOutcome.Cancelled)]
    [InlineData(TaskRunStatus.Completed, 0, 3, 1, 5, RunOutcome.PartlyFailed)]
    public void Of_FollowsTheRule(TaskRunStatus status, int unfinished, int succeeded, int failed, int cancelled, RunOutcome expected) =>
        Assert.Equal(expected, RunOutcomes.Of(status, unfinished, succeeded, failed, cancelled));

    [Fact]
    public void Of_StaleRunningWithNothingUnfinished_IsSucceeded() =>
        Assert.Equal(RunOutcome.Succeeded, RunOutcomes.Of(TaskRunStatus.Running, 0, 0, 0, 0));
}
