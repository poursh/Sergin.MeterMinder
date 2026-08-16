using Sergin.MeterMinder;
using Sergin.MeterMinder.Hosts.WebUi.All.Components;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("sergin-webui-all");

IReadOnlyCollection<ISerginModule> modules = [new MeterMinderModule(), new UserAccessModule()];

builder.AddSerginWebUi(modules);

WebApplication app = builder.Build();

await app.UseSerginWebUiAsync<App>(modules);

await app.RunAsync();

public partial class Program;
