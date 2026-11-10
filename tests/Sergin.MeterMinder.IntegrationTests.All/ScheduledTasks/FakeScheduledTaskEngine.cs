using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// An in-memory engine for the scheduled-task tests: triggers are a dictionary, run items a queue that
/// <see cref="DrainAsync"/> runs through the real <see cref="ScheduledRunItemExecutor"/>, one fresh scope per attempt,
/// re-queuing an item whenever the executor asks for a retry. <see cref="FailNextCall"/> makes the next engine call
/// throw, so a test can prove a failing engine fails the request.
/// </summary>
internal sealed class FakeScheduledTaskEngine : IScheduledTaskEngine
{
    // A drain that keeps re-queuing past this is a test bug (an executor that never stops asking), not a slow test.
    private const int MaxDrainSteps = 1000;

    private readonly Lock gate = new();
    private readonly Queue<ScheduledRunItemReference> queue = new();

    public Dictionary<Guid, (ScheduledTrigger Trigger, bool Enabled)> Triggers { get; } = [];

    public bool FailNextCall { get; set; }

    public IReadOnlyCollection<ScheduledRunItemReference> QueuedItems
    {
        get
        {
            lock (gate)
            {
                return [.. queue];
            }
        }
    }

    public Task<Guid> ScheduleAsync(ScheduledTrigger trigger, CancellationToken ct)
    {
        ThrowIfFailing();
        var id = Guid.CreateVersion7();

        lock (gate)
        {
            Triggers[id] = (trigger, true);
        }

        return Task.FromResult(id);
    }

    public Task RescheduleAsync(Guid engineTriggerId, ScheduledTrigger trigger, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            Triggers[engineTriggerId] = (trigger, Triggers.TryGetValue(engineTriggerId, out (ScheduledTrigger Trigger, bool Enabled) existing) && existing.Enabled);
        }

        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid engineTriggerId, bool enabled, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            if (Triggers.TryGetValue(engineTriggerId, out (ScheduledTrigger Trigger, bool Enabled) existing))
            {
                Triggers[engineTriggerId] = (existing.Trigger, enabled);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid engineTriggerId, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            Triggers.Remove(engineTriggerId);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid engineTriggerId, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            return Task.FromResult(Triggers.ContainsKey(engineTriggerId));
        }
    }

    public Task<Guid> EnqueueRunItemAsync(ScheduledRunItemReference item, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            queue.Enqueue(item);
        }

        return Task.FromResult(Guid.CreateVersion7());
    }

    public bool IsValidCron(string cron, string timeZoneId) =>
        !string.IsNullOrWhiteSpace(cron) && cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is 5 or 6;

    /// <summary>Runs every queued item, and every retry it asks for, until the queue is empty.</summary>
    public async Task DrainAsync(IServiceProvider services)
    {
        for (int step = 0; step < MaxDrainSteps; step++)
        {
            ScheduledRunItemReference? next;

            lock (gate)
            {
                if (!queue.TryDequeue(out next))
                {
                    return;
                }
            }

            using IServiceScope scope = services.CreateScope();
            ScheduledRunItemExecutor executor = scope.ServiceProvider.GetRequiredService<ScheduledRunItemExecutor>();

            if (await executor.ExecuteAsync(next.Schema, next.RunItemId, CancellationToken.None))
            {
                lock (gate)
                {
                    queue.Enqueue(next);
                }
            }
        }

        throw new InvalidOperationException($"The fake engine drained {MaxDrainSteps} items without emptying its queue.");
    }

    private void ThrowIfFailing()
    {
        if (FailNextCall)
        {
            FailNextCall = false;
            throw new InvalidOperationException("The fake engine was told to fail this call.");
        }
    }
}
