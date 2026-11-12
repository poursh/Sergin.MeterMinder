using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The real host's scheduling engine. The shared factory runs <c>Program.cs</c>, so <c>UseSerginTickerQ</c> on the
/// stand-in branch has already run without throwing by the time any service resolves here.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class HostEngineTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void RealHost_WithoutALicence_UsesTheStandIn()
    {
        IScheduledTaskEngine engine = factory.Services.GetRequiredService<IScheduledTaskEngine>();

        Assert.IsType<InProcessScheduledTaskEngine>(engine);
        Assert.False(engine.FiresTriggers);
    }
}
