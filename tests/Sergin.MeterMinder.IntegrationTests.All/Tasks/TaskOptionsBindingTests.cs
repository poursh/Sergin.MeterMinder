using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.Tasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// <c>Sergin:Tasks</c> binds the way AddSerginCore binds it: configured retry intervals replace the default
/// rather than being appended to it, and the default applies when none are configured.
/// </summary>
public sealed class TaskOptionsBindingTests
{
    [Fact]
    public void ConfiguredRetryIntervals_ReplaceTheDefault()
    {
        TaskOptions options = Bind(new Dictionary<string, string?>
        {
            ["Sergin:Tasks:RetryIntervalsSeconds:0"] = "10",
        });

        Assert.Equal([10], options.RetryIntervalsSeconds);
    }

    [Fact]
    public void NoRetryIntervals_KeepTheDefault()
    {
        TaskOptions options = Bind(new Dictionary<string, string?>
        {
            ["Sergin:Tasks:MaxAttempts"] = "5",
        });

        Assert.Equal([30, 120], options.RetryIntervalsSeconds);
        Assert.Equal(5, options.MaxAttempts);
    }

    [Fact]
    public void HeartbeatInterval_IsBoundAndValidated()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Bind([]).HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), Bind(new Dictionary<string, string?>
        {
            ["Sergin:Tasks:HeartbeatInterval"] = "00:00:02",
        }).HeartbeatInterval);

        TaskOptions zero = Bind(new Dictionary<string, string?>
        {
            ["Sergin:Tasks:HeartbeatInterval"] = "00:00:00",
        });

        Assert.False(zero.Validate(out string failure));
        Assert.Contains("Sergin:Tasks:HeartbeatInterval", failure, StringComparison.Ordinal);
    }

    private static TaskOptions Bind(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new();
        services.AddOptions<TaskOptions>()
            .Bind(configuration.GetSection("Sergin").GetSection(TaskOptions.SectionName))
            .WithDefaults();

        using ServiceProvider provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TaskOptions>>().Value;
    }
}
