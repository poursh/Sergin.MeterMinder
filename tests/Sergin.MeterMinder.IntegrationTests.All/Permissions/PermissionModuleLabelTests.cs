using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations;
using Sergin.MeterMinder.IntegrationTests.All.Localization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.UserAccess.Application.Configurations.Roles;
using Sergin.UserAccess.Presentation.Blazor.Roles;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>
/// A module's subheader on the role pages (<see cref="PermissionLabels.Module"/>): the module's drawer group title, in
/// the caller's culture; the platform's own heading for the <c>sys</c> schema; the schema itself for a module this host
/// shows no group for — never a raw resource key.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PermissionModuleLabelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Module_ForAModuleSchema_IsItsGroupTitle()
    {
        using CultureScope culture = new("en");

        Assert.Equal(DeviceManagementTexts.MenuTitle.Default, Module(factory, "dm"));
    }

    [Fact]
    public async Task Module_ForAModuleSchema_IsTranslated()
    {
        TestTranslationStore store = new();
        store.Set("de", DeviceManagementTexts.MenuTitle.Key.Value, "Geräteverwaltung", DeviceManagementTexts.MenuTitle.Default);

        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Assert.Equal("Geräteverwaltung", Module(host, "dm"));
    }

    [Fact]
    public void Module_ForThePlatform_IsThePlatformHeading()
    {
        using CultureScope culture = new("en");

        Assert.Equal(RoleTexts.PlatformModule.Default, Module(factory, "sys"));
    }

    [Fact]
    public void Module_ForAnUnknownSchema_IsTheSchema()
    {
        using CultureScope culture = new("en");

        Assert.Equal("zz", Module(factory, "zz"));
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

    private static string Module(WebApplicationFactory<Program> host, string schema)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return PermissionLabels.Module(
            scope.ServiceProvider.GetRequiredService<SerginUiModuleCatalog>(),
            scope.ServiceProvider.GetRequiredService<IStringLocalizer>(),
            schema);
    }
}
