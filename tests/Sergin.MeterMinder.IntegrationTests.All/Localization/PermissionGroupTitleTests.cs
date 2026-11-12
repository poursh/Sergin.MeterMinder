using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.IntegrationTests.All.Permissions;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.Administration.UserAccess.Application.Configurations.Roles;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The permission picker's group headings are declared texts, not aggregate type names: the name the root declares with
/// <c>Named(...)</c>, the platform's own heading for the platform permission, and one declared fallback for a root that
/// names none.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PermissionGroupTitleTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Group_IsTitledWithTheRootsDeclaredName()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();

        PermissionCatalogGroup devices = catalog.Grouped()
            .Single(module => module.Schema == "dm").Groups
            .Single(group => group.Name == "Device");
        PermissionCatalogGroup roles = catalog.Grouped()
            .Single(module => module.Schema == "ua").Groups
            .Single(group => group.Name == "Role");

        Assert.Same(DeviceTexts.Singular, devices.Title);
        Assert.Same(RoleTexts.Singular, roles.Title);
    }

    [Fact]
    public void PlatformGroup_IsTitledWithADeclaredText()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();

        PermissionCatalogGroup platform = Assert.Single(catalog.Grouped().Single(module => module.Schema == "sys").Groups, group => group.Name == "Platform");

        Assert.Same(CommonTexts.PermissionGroupPlatform, platform.Title);
        Assert.Equal("common.permissions.groups.platform", platform.Title.Key.Value);
    }

    [Fact]
    public void UnnamedRoot_FallsBackToADeclaredText()
    {
        var catalog = PermissionCatalog.FromSources([ModuleConfigurationSource.FromTypes(typeof(CatalogTestRootConfiguration))]);

        PermissionCatalogGroup group = Assert.Single(catalog.Grouped().Single(module => module.Schema == "test").Groups);

        Assert.Same(CommonTexts.PermissionGroupOther, group.Title);
    }

    [Fact]
    public void Titles_AreInTheTextCatalog()
    {
        TextCatalog texts = factory.Services.GetRequiredService<TextCatalog>();

        Assert.True(texts.TryGet(CommonTexts.PermissionGroupPlatform.Key.Value, out _));
        Assert.True(texts.TryGet(CommonTexts.PermissionGroupOther.Key.Value, out _));
        Assert.True(texts.TryGet(DeviceTexts.Singular.Key.Value, out _));
    }

    // The role pages render the heading as Localizer.Text(group.Title). The picker's items live in a MudSelect popover,
    // which is not prerendered, so the heading is checked through the same lookup rather than in the page HTML.
    [Fact]
    public async Task GroupTitle_IsTranslated()
    {
        TestTranslationStore store = new();
        store.Set("de", DeviceTexts.Singular.Key.Value, "Zähler-Gruppe", DeviceTexts.Singular.Default);

        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync("de", CancellationToken.None);

        PermissionCatalogGroup devices = host.Services.GetRequiredService<PermissionCatalog>().Grouped()
            .Single(module => module.Schema == "dm").Groups
            .Single(group => group.Name == "Device");

        using CultureScope culture = new("de");
        using IServiceScope scope = host.Services.CreateScope();

        Assert.Equal("Zähler-Gruppe", scope.ServiceProvider.GetRequiredService<IStringLocalizer>().Text(devices.Title));
    }
}
