using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>
/// A module's subheader on the role pages is its localized name when a resource exists and its schema when not,
/// never the raw resource key the localizer echoes back.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PermissionModuleLabelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Localizer_WithNoResource_FallsBackToTheSchema()
    {
        IStringLocalizer localizer = factory.Services.GetRequiredService<IStringLocalizer>();

        Assert.Equal("dm", OrDefault(localizer, "Permissions.dm", "dm"));
    }

    [Fact]
    public void Localizer_WithAResource_ShowsTheLocalizedText()
    {
        Assert.Equal("Devices", OrDefault(new FoundLocalizer(), "Permissions.dm", "dm"));
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

    private static string OrDefault(IStringLocalizer localizer, string key, string fallback) =>
        localizer[key] is { ResourceNotFound: false } text ? text.Value : fallback;

    private sealed class FoundLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, "Devices", resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
