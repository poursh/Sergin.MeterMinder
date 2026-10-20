using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using System.Globalization;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>
/// A module's subheader on the role pages is its localized name when a resource exists and its schema when not,
/// never the raw resource key the default localizer echoes back.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PermissionModuleLabelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void DefaultLocalizer_WithNoResource_FallsBackToTheSchema()
    {
        ILocalizer localizer = factory.Services.GetRequiredService<ILocalizer>();

        Assert.Equal("dm", localizer.LocalizedOrDefault("Permissions.dm", "dm"));
    }

    [Fact]
    public void Localizer_WithAResource_ShowsTheLocalizedText()
    {
        Assert.Equal("Devices", new FoundLocalizer().LocalizedOrDefault("Permissions.dm", "dm"));
    }

    [Theory]
    [InlineData("/ua/roles/new?scopeId=01920000-0000-7000-8000-0000000000b1")]
    [InlineData("/ua/roles/01920000-0000-7000-8000-0000000000a2")]
    public async Task RolePages_ShowNoRawLocalizerKey(string path)
    {
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.DoesNotContain("Permissions.", html, StringComparison.Ordinal);
    }

    private sealed class FoundLocalizer : ILocalizer
    {
        public LocalizedString this[string name] => new(name, "Devices", resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
    }
}
