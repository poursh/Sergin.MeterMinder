using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Times;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The generated detail view as the prerender draws it on the test-only <c>/tst/view-probes/{id}</c> page: the fields
/// from the GetOne response, the header buttons, the actions from the probe's commands (hidden without their
/// permission), the split button and scheduled-tasks section of a schedulable command, and the not-found panel.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedViewProbeTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;
    private ViewProbeStore store = default!;
    private PlainProbeStore plainStore = default!;

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            ProbeHandlers.AddTo(services);
            ScheduleProbeUiModule.AddTo(services);
            ProbeUserContextFactory.AddTo(services);
            ViewProbeTestHost.AddTo(services);
            PlainProbeTestHost.AddTo(services);
            services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
        }));

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        store = host.Services.GetRequiredService<ViewProbeStore>();
        plainStore = host.Services.GetRequiredService<PlainProbeStore>();
        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ProbeDetail_RendersFieldsHeaderAndActions()
    {
        Guid probe = store.Add("Probe alpha");
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Probe alpha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewProbeName", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Copy link\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Information\"", html, StringComparison.Ordinal);
        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.Contains(">Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_OverviewSlot_ReceivesLoadedOverview()
    {
        Guid probe = store.Add("Probe overview");
        await GrantAsync(ViewProbePermissions.Read);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains($"probe-figure:{GetViewProbeOverviewQueryHandler.Figure}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_SchedulableCommand_RendersSplitButtonWithScheduleItems()
    {
        Guid probe = store.Add("Probe beta");
        await GrantAsync(ViewProbePermissions.Run, ScheduledTaskPermissions.Manage);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"More actions\"", html, StringComparison.Ordinal);
        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_WithoutManagePermission_RendersPlainRunButton()
    {
        Guid probe = store.Add("Probe gamma");
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("More actions", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_WithoutRunPermission_HidesRunButton()
    {
        Guid probe = store.Add("Probe delta");
        await GrantAsync(ViewProbePermissions.Read);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Probe delta", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Run probe", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_UnknownId_ShowsProblemPanel()
    {
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{Guid.CreateVersion7()}");

        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Run probe", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeDetail_NotVersioned_RendersRecord()
    {
        Guid probe = plainStore.Add("Plain alpha");

        string html = await GetAsync($"/tst/plain-probes/{probe}");

        Assert.Contains("Plain alpha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_RendersEnumValueText_NotMemberName()
    {
        Guid probe = plainStore.Add("Plain enum");

        string html = await GetAsync($"/tst/plain-probes/{probe}");

        Assert.Contains("Runner-up value", html, StringComparison.Ordinal);
        Assert.DoesNotContain($">{nameof(ProbeEnum.Second)}<", html, StringComparison.Ordinal);
        Assert.Contains("Switched off", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_ShowsFieldHint()
    {
        Guid probe = plainStore.Add("Plain hint");

        string html = await GetAsync($"/tst/plain-probes/{probe}/edit");

        Assert.Contains("Probe hint text", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeEdit_NotVersioned_RendersPrefilledForm()
    {
        Guid probe = plainStore.Add("Plain beta");

        string html = await GetAsync($"/tst/plain-probes/{probe}/edit");

        Assert.Contains("value=\"Plain beta\"", html, StringComparison.Ordinal);
        Assert.Contains(">Save<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeEdit_NotVersioned_LoadsAndSubmitsWithoutAVersion()
    {
        Guid probe = plainStore.Add("Plain gamma");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<PlainProbe>();
        Assert.False(model.Features.Versioned);

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);

        Assert.False(loaded.IsError, loaded.IsError ? loaded.Errors[0].Code : null);
        Assert.Null(loaded.Version);

        Dictionary<string, object?> values = new(StringComparer.Ordinal) { [nameof(UpdatePlainProbeCommand.Name)] = "Plain delta" };
        ViewSendResult saved = await model.SubmitFormAsync(dispatcher, probe, values, loaded.Version);

        Assert.False(saved.IsError, saved.IsError ? saved.Errors[0].Code : null);
        Assert.Equal("Plain delta", plainStore.Find(probe)!.Name.Value);
    }

    private async Task GrantAsync(params PermissionDefinition[] permissions) =>
        users.Current = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, [.. permissions.Select(permission => permission.Permission)]);

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}: {body[..Math.Min(body.Length, 3000)]}");

        return body;
    }
}
