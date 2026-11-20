using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The real host's task names, pinned: a renamed task type changes its derived name, and a saved schedule stores the
/// name. When this fails, accept the new name in <c>task-names.txt</c> (no saved schedule uses the old one) or keep the
/// old one with <c>Task(task =&gt; task.Name("&lt;old&gt;"))</c>.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskNamePinTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void TaskNames_MatchThePinnedList()
    {
        TaskCatalog catalog = factory.Services.GetRequiredService<TaskCatalog>();

        string[] actual = [.. catalog.Commands.Select(command => command.Name).Order(StringComparer.Ordinal)];
        string[] expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Tasks", "task-names.txt"));

        Assert.Equal(expected, actual);
    }
}
