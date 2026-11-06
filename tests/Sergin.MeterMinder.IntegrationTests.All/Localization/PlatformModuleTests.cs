using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Platform module (spec decision 20) is composed into the host: its UI module is in the shell's catalog under
/// schema <c>pf</c>, and its menu title is a declared text under the <c>platform</c> key segment.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PlatformModuleTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void PlatformModule_IsComposed()
    {
        SerginUiModuleCatalog modules = factory.Services.GetRequiredService<SerginUiModuleCatalog>();
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        Assert.Contains(modules.Modules, module => module.Schema == "pf" && module.Name == "Platform");
        Assert.True(catalog.TryGet("platform.menu-title", out _), "'platform.menu-title' is not a declared text.");
    }
}
