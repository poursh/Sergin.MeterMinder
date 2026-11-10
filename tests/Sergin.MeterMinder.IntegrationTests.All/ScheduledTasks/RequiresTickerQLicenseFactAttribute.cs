namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// Skips a test that needs the TickerQ engine to run, unless the licence file named by the
/// <c>SERGIN_TICKERQ_LICENSE</c> environment variable exists. The licence is never committed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresTickerQLicenseFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "SERGIN_TICKERQ_LICENSE";

    public RequiresTickerQLicenseFactAttribute()
    {
        if (LicensePath is null)
        {
            Skip = $"No TickerQ licence: set {EnvironmentVariable} to a licence file path.";
        }
    }

    /// <summary>The licence file path, or <see langword="null"/> when none is available.</summary>
    public static string? LicensePath
    {
        get
        {
            string? path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
    }
}
