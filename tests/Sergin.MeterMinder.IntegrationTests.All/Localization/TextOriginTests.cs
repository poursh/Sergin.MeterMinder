using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.Hosts.All.Components;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// Every catalog entry says whether the application or the UI declares it, from the source it was registered through,
/// never from its key: a module's RCL, SharedKernel's Blazor kit and the host's own texts are the UI; everything else
/// (a module's ConfigurationsAssembly, SharedKernel's common texts, the permission labels) is the application.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TextOriginTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Entry_IsUi_ExactlyWhenAUiAssemblyDeclaresIt()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        HashSet<Assembly> uiAssemblies =
        [
            .. factory.Services.GetRequiredService<SerginUiModuleCatalog>().Modules.Select(module => module.UiAssembly),
            typeof(ShellTexts).Assembly,
            typeof(Program).Assembly,
        ];

        Assert.All(catalog.Entries, entry => Assert.Equal(
            uiAssemblies.Contains(entry.DeclaringType.Assembly) ? TextOrigin.Ui : TextOrigin.Application,
            entry.Origin));
    }

    [Fact]
    public void Origin_FollowsTheSource_ForEachKind()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        Assert.Equal(TextOrigin.Application, Origin(catalog, CommonTexts.Save));
        Assert.Equal(TextOrigin.Ui, Origin(catalog, ShellTexts.Navigation.Home));
        Assert.Contains(catalog.Entries, entry => entry.DeclaringType == typeof(MudBlazorTexts) && entry.Origin == TextOrigin.Ui);
        Assert.Contains(catalog.Entries, entry => entry.DeclaringType == typeof(MeterMinderTexts) && entry.Origin == TextOrigin.Ui);
    }

    private static TextOrigin Origin(TextCatalog catalog, TextDefinition definition) =>
        Assert.Single(catalog.Entries, entry => entry.Definition.Key == definition.Key).Origin;
}
