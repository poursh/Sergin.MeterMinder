using Sergin.Administration.Settings;
using Sergin.Administration.UserAccess;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.Hosts.All.Components;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Modules;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("sergin-all");

IReadOnlyCollection<ISerginModule> modules = [new DeviceManagementModule(), new UserAccessModule(), new SettingsModule()];

builder.AddSerginBlazorApp(modules, configureHome: home => home.UseComponent<MeterMinderHome>());

// The host's own texts (its home slot's product copy), beside the modules' and SharedKernel's in the catalog.
builder.Services.AddSingleton(TextSource.FromTypes("MeterMinder", TextOrigin.Ui, typeof(MeterMinderTexts)));

WebApplication app = builder.Build();

await app.UseSerginWebUiAsync<App>(modules);

await app.RunAsync();

public partial class Program;
