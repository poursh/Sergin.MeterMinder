using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// <c>Sergin:ScheduledTasks</c> binds the way AddSerginCore binds it: configured retry intervals replace the default
/// rather than being appended to it, and the default applies when none are configured.
/// </summary>
public sealed class ScheduledTaskOptionsBindingTests
{
    [Fact]
    public void ConfiguredRetryIntervals_ReplaceTheDefault()
    {
        ScheduledTaskOptions options = Bind(new Dictionary<string, string?>
        {
            ["Sergin:ScheduledTasks:RetryIntervalsSeconds:0"] = "10",
        });

        Assert.Equal([10], options.RetryIntervalsSeconds);
    }

    [Fact]
    public void NoRetryIntervals_KeepTheDefault()
    {
        ScheduledTaskOptions options = Bind(new Dictionary<string, string?>
        {
            ["Sergin:ScheduledTasks:MaxAttempts"] = "5",
        });

        Assert.Equal([30, 120], options.RetryIntervalsSeconds);
        Assert.Equal(5, options.MaxAttempts);
    }

    private static ScheduledTaskOptions Bind(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new();
        services.AddOptions<ScheduledTaskOptions>()
            .Bind(configuration.GetSection("Sergin").GetSection(ScheduledTaskOptions.SectionName))
            .WithDefaults();

        using ServiceProvider provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<ScheduledTaskOptions>>().Value;
    }
}
