using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// A declared text for a test's own nav entries and labels, whose English default is what the test reads. Never in
/// the catalog, so the localizer shows the default.
/// </summary>
internal static class TestText
{
    private static readonly TextKey key = TextKey.Create("test.text");

    public static TextDefinition Of(string value) => new(key, value);
}
