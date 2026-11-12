using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The device detail page as the real host prerenders it: Read profile and Schedule… in the actions bar, the device's
/// schedules section, and — the host's engine being the stand-in, which fires no trigger — the line saying scheduled runs
/// are off. A caller without the read-profile permission sees neither action.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceScheduleUiTests(SerginWebApiFactory<Program> factory)
{
    private const string EngineOff = "Scheduled runs are off until the scheduling engine is configured. Run now still works.";

    [Fact]
    public async Task DeviceDetail_ShowsReadProfileSplitButton()
    {
        Guid device = await CreateDeviceAsync();

        string html = await GetAsync(factory, $"/dm/devices/{device}");

        // Schedule… lives in the split button's menu, which is not prerendered.
        Assert.Contains("Read profile", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"More actions\"", html, StringComparison.Ordinal);
        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule…", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceDetail_WithoutManagePermission_ShowsPlainReadProfile()
    {
        Guid device = await CreateDeviceAsync();

        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(ProbeUserContextFactory.AddTo));

        RbacScenario tree = await RbacScenario.BuildAsync(host);
        host.Services.GetRequiredService<ProbeUserContextFactory>().Current = await tree.CreateGrantedCallerAsync(
            Scope.RootId.Value, DevicePermissions.Read.Permission, DevicePermissions.ReadProfile.Permission);

        string html = await GetAsync(host, $"/dm/devices/{device}");

        Assert.Contains("Read profile", html, StringComparison.Ordinal);
        Assert.DoesNotContain("More actions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduled tasks", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceDetail_ShowsEngineOffLine()
    {
        Guid device = await CreateDeviceAsync();

        string html = await GetAsync(factory, $"/dm/devices/{device}");

        Assert.Contains(EngineOff, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceDetail_WithoutReadProfilePermission_HidesBoth()
    {
        Guid device = await CreateDeviceAsync();

        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(ProbeUserContextFactory.AddTo));

        RbacScenario tree = await RbacScenario.BuildAsync(host);
        host.Services.GetRequiredService<ProbeUserContextFactory>().Current = await tree.CreateGrantedCallerAsync(
            Scope.RootId.Value, DevicePermissions.Read.Permission, ScheduledTaskPermissions.Manage.Permission);

        string html = await GetAsync(host, $"/dm/devices/{device}");

        // The page and its schedules section are there (read and manage are held); the two actions are not.
        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Read profile", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule…", html, StringComparison.Ordinal);
    }

    private async Task<Guid> CreateDeviceAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");

        return await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);
    }

    private static async Task<string> GetAsync(WebApplicationFactory<Program> host, string path)
    {
        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}");

        return body;
    }
}
