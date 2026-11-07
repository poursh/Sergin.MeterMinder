using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Settings module (spec decision 20) is composed into the host: its UI module is in the shell's catalog under
/// schema <c>st</c>, in the shared Administration drawer group, and its texts are declared under the
/// <c>settings</c> key segment.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SettingsModuleTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void SettingsModule_IsComposed()
    {
        SerginUiModuleCatalog modules = factory.Services.GetRequiredService<SerginUiModuleCatalog>();
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        Assert.Contains(
            modules.Modules,
            module => module.Schema == "st" && module.Name == "Settings" && module.NavGroup == SerginNavGroups.Administration);
        Assert.True(catalog.TryGet("settings.languages.title", out _), "'settings.languages.title' is not a declared text.");
    }
}
