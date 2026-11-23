using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Infrastructure.Tasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

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
        ITaskEngine engine = factory.Services.GetRequiredService<ITaskEngine>();

        Assert.IsType<InProcessTaskEngine>(engine);
        Assert.False(engine.FiresTriggers);
    }
}
