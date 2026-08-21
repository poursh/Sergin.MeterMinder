using Sergin.MeterMinder.DeviceManagement;
using Sergin.MeterMinder.Hosts.All.Components;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("sergin-all");

IReadOnlyCollection<ISerginModule> modules = [new DeviceManagementModule(), new UserAccessModule()];

builder.AddSerginBlazorApp(modules, configureHome: home => home.UseComponent<MeterMinderHome>());

WebApplication app = builder.Build();

await app.UseSerginWebUiAsync<App>(modules);

await app.RunAsync();

public partial class Program;
