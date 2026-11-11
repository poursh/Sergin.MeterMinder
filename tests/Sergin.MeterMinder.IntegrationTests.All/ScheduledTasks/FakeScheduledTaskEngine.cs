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
    private readonly Dictionary<Guid, DateTime> createdAt = [];

    private readonly Dictionary<Guid, (ScheduledTrigger Trigger, bool Enabled)> triggers = [];

    /// <summary>A snapshot, taken under the lock: the rebuild and the recovery change the triggers from other threads.</summary>
    public IReadOnlyDictionary<Guid, (ScheduledTrigger Trigger, bool Enabled)> Triggers
    {
        get
        {
            lock (gate)
            {
                return new Dictionary<Guid, (ScheduledTrigger Trigger, bool Enabled)>(triggers);
            }
        }
    }

    public bool FailNextCall { get; set; }

    public bool FiresTriggers => true;

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
            triggers[id] = (trigger, true);
            createdAt[id] = DateTime.UtcNow;
        }

        return Task.FromResult(id);
    }

    /// <summary>Puts a trigger straight into the engine, as if it had been made at <paramref name="madeAtUtc"/>.</summary>
    public Guid AddTrigger(ScheduledTrigger trigger, DateTime madeAtUtc)
    {
        var id = Guid.CreateVersion7();

        lock (gate)
        {
            triggers[id] = (trigger, true);
            createdAt[id] = madeAtUtc;
        }

        return id;
    }

    public Task<IReadOnlyCollection<ScheduledEngineTrigger>> ListTriggersAsync(CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            IReadOnlyCollection<ScheduledEngineTrigger> all = [.. triggers.Select(pair => new ScheduledEngineTrigger(
                pair.Key, pair.Value.Trigger.Schema, pair.Value.Trigger.TaskId, createdAt.GetValueOrDefault(pair.Key, DateTime.UtcNow)))];

            return Task.FromResult(all);
        }
    }

    public Task RescheduleAsync(Guid engineTriggerId, ScheduledTrigger trigger, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            triggers[engineTriggerId] = (trigger, triggers.TryGetValue(engineTriggerId, out (ScheduledTrigger Trigger, bool Enabled) existing) && existing.Enabled);
        }

        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid engineTriggerId, bool enabled, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            if (triggers.TryGetValue(engineTriggerId, out (ScheduledTrigger Trigger, bool Enabled) existing))
            {
                triggers[engineTriggerId] = (existing.Trigger, enabled);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid engineTriggerId, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            triggers.Remove(engineTriggerId);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid engineTriggerId, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            return Task.FromResult(triggers.ContainsKey(engineTriggerId));
        }
    }

    public Task<bool?> GetEnabledAsync(Guid engineTriggerId, CancellationToken ct)
    {
        ThrowIfFailing();

        lock (gate)
        {
            return Task.FromResult(triggers.TryGetValue(engineTriggerId, out (ScheduledTrigger Trigger, bool Enabled) held) ? held.Enabled : (bool?)null);
        }
    }

    /// <summary>Drops every queued job, as an engine that lost them would.</summary>
    public void LoseQueuedItems()
    {
        lock (gate)
        {
            queue.Clear();
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

    /// <summary>Makes <see cref="NextOccurrences"/> answer nothing, as an engine that cannot say.</summary>
    public bool NoOccurrences { get; set; }

    public bool IsValidCron(string cron, string timeZoneId) =>
        !string.IsNullOrWhiteSpace(cron) && cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is 5 or 6;

    /// <summary>
    /// A deterministic stub: for a valid cron, one occurrence per day at 02:00 UTC after <paramref name="after"/> (whatever the
    /// expression says), so a UI test sees real-looking next runs without a cron parser.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> NextOccurrences(string cron, string timeZoneId, DateTimeOffset after, int count)
    {
        if (NoOccurrences || count <= 0 || !IsValidCron(cron, timeZoneId))
        {
            return [];
        }

        var first = new DateTimeOffset(after.UtcDateTime.Date.AddHours(2), TimeSpan.Zero);

        return [.. Enumerable.Range(0, count + 1).Select(day => first.AddDays(day)).Where(time => time > after).Take(count)];
    }

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
