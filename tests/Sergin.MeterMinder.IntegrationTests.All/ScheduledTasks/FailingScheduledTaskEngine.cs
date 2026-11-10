using Sergin.SharedKernel.Application.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// An engine that is down: every call that would reach it throws, while it still answers that any cron is valid, so a
/// request gets past validation to the engine call and the test sees what a failing engine does to the save.
/// </summary>
internal sealed class FailingScheduledTaskEngine : IScheduledTaskEngine
{
    public int Calls { get; private set; }

    public Task<Guid> ScheduleAsync(ScheduledTrigger trigger, CancellationToken ct) => Fail<Guid>();

    public Task RescheduleAsync(Guid engineTriggerId, ScheduledTrigger trigger, CancellationToken ct) => Fail<bool>();

    public Task SetEnabledAsync(Guid engineTriggerId, bool enabled, CancellationToken ct) => Fail<bool>();

    public Task RemoveAsync(Guid engineTriggerId, CancellationToken ct) => Fail<bool>();

    public Task<bool> ExistsAsync(Guid engineTriggerId, CancellationToken ct) => Fail<bool>();

    public Task<bool?> GetEnabledAsync(Guid engineTriggerId, CancellationToken ct) => Fail<bool?>();

    public Task<IReadOnlyCollection<ScheduledEngineTrigger>> ListTriggersAsync(CancellationToken ct) => Fail<IReadOnlyCollection<ScheduledEngineTrigger>>();

    public Task<Guid> EnqueueRunItemAsync(ScheduledRunItemReference item, CancellationToken ct) => Fail<Guid>();

    public bool IsValidCron(string cron, string timeZoneId) => true;

    private Task<T> Fail<T>()
    {
        Calls++;
        throw new InvalidOperationException("The job engine is unavailable.");
    }
}
