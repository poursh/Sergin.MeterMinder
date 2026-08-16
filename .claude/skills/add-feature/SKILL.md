---
name: add-feature
description: Scaffold a new CQRS vertical slice (command or query) in a Sergin module — Application handler, Infrastructure repository wiring, Presentation endpoint, optional Blazor page, and DI/route registration — following the existing UserAccess module pattern. Invoke with /add-feature.
disable-model-invocation: false
---

Scaffold a new vertical-slice feature for: $ARGUMENTS

Expected input: `<ModuleName> <AggregateFolder> <FeatureName> <command|query>`, e.g. `/add-feature UserAccess Users DeactivateUser command`. Ask the user for whatever is missing before generating anything — don't guess the module, aggregate, or verb.

This repo has no scaffolding CLI; slices are hand-authored following a strict, repeated shape. Use `src/Modules/UserAccess/**/Users/**` as the reference implementation for every file below — read the matching file there before writing the new one, and match its style exactly (sealed records, primary constructors, `ErrorOr<T>` returns, no comments).

## Layout to create (module = e.g. `UserAccess`, aggregate = e.g. `Users`, feature = e.g. `DeactivateUser`)

**Command** (state-changing):
1. `src/Modules/<Module>/Sergin.<Module>.Application/<Aggregate>/Commands/<Feature>/<Feature>Command.cs` — `public sealed record <Feature>Command(...) : ICommand<<Feature>CommandResponse>;`
2. `.../<Feature>/<Feature>CommandResponse.cs` — `public sealed record <Feature>CommandResponse(...);`
3. `.../<Feature>/<Feature>CommandHandler.cs` — `internal sealed class` implementing `ICommandHandler<TCommand, TResponse>`, primary-constructor-injects `I<Module>UnitOfWork` + the domain repository, calls a domain factory/behavior method, calls `unitOfWork.SaveChangesAsync`, returns the response.
4. If the domain aggregate needs a new factory method or behavior (e.g. `User.Deactivate()`), add it to the aggregate class in `Sergin.<Module>.Domain`. Don't add public setters — mutate via methods on the aggregate.
5. Presentation: `src/Modules/<Module>/Sergin.<Module>.Presentation.WebApi/<Aggregate>/Endpoints/<Feature>/<Feature>Endpoint.cs` implementing `IEndpoint.MapEndpoint`, mapping the appropriate HTTP verb, binding a request model (add one alongside the endpoint if the command needs a body, e.g. `New<X>Model.cs`), sending via `ISender`, returning `res.ToApiResult()`.
6. Register the endpoint in the module's `<Aggregate>InstallationExtensions.Map<Aggregate>Endpoints` (e.g. `UserInstallationExtensions.MapUserEndpoints`) — instantiate and call `.MapEndpoint(routeBuilder)`. For a brand-new aggregate, create that file first (copy `UserInstallationExtensions.cs`) and wire it into the module class: `services.Add<Aggregate>Dependencies()` in `<Module>Module.AddServices` and `group.Map<Aggregate>Endpoints()` in `<Module>Module.MapEndpoints`.
7. If a new repository interface/dependency is needed, register it in the same file's `Add<Aggregate>Dependencies` (`services.AddTransient<IFoo, Foo>()`).

**Query** (read-side, bypasses EF):
Same shape but under `Commands/<Feature>/` still (this repo keeps queries in the `Commands` folder alongside commands — match that, don't invent a `Queries` folder), implementing `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>` from `Sergin.SharedKernel.Application.Commands.Queries`. The handler depends on a dedicated `I<Feature>QueryRepository` interface (returns nullable response, handler maps null to `Error.NotFound()`). Implement that interface in `Sergin.<Module>.Infrastructure/<Aggregate>/Repositories/Queries/<Aggregate>QueryRepository.cs` using `IDbConnectionFactory` + raw SQL against the module's Postgres schema (see `UserQueryRepository.cs` for the `QuerySingleOrDefaultAsync` / `QueryMultipleAsync` Dapper-style pattern) — never use EF Core for reads. If the query needs authorization, add `[RequiredPermissions("permission.<schema>.<resource>.<action>")]` on the query record.

## Optional: the UI slice

Ask whether the feature also needs a **Blazor page**. Skip this whole section if not — plenty of slices are API-only, and the module's `.Presentation.Blazor` project may not exist at all (it's optional; see `/add-module`). The slice above is complete and shippable without it: pages consume the same MediatR handlers through `ISerginUiDispatcher`, so nothing in Application/Infrastructure changes.

Reference implementations to read before writing — cite these, don't improvise:
- `src/Modules/UserAccess/Sergin.UserAccess.Presentation.Blazor/Users/Pages/` — `UserListPage`, `UserDetailPage` (which also carries a mutate action), `CreateUserPage`, each a `.razor` + `.razor.cs` pair.
- `src/Modules/MeterMinder/Sergin.MeterMinder.Presentation.Blazor/Devices/Pages/` — `DeviceListPage`, `DeviceDetailPage`, `CreateDevicePage` (whose `OnInitializedAsync` loads a second list to populate a picker).
- Form models: `Users/Models/NewUserFormModel.cs`, `Devices/Models/NewDeviceFormModel.cs`.

**Layout** — `src/Modules/<Module>/Sergin.<Module>.Presentation.Blazor/<Aggregate>/`:

| Path | Route | Purpose |
|---|---|---|
| `Pages/<Aggregate>ListPage.razor{,.cs}` | `/<schema>/<aggregate>` | paged `MudTable` |
| `Pages/<Aggregate>DetailPage.razor{,.cs}` | `/<schema>/<aggregate>/{Id:guid}` | one record + any mutate actions |
| `Pages/Create<Aggregate>Page.razor{,.cs}` | `/<schema>/<aggregate>/new` | `EditForm` over a form model |
| `Models/New<Aggregate>FormModel.cs` | — | `public sealed class`, `System.ComponentModel.DataAnnotations` attributes |

**Every `@page` template must start with `/<schema>/`.** A startup guard in `UseSerginWebUiAsync` reflects over the module's `UiAssembly` and throws, naming the offending component and template, otherwise — Razor `@page` templates are compile-time constants, so unlike minimal-API routes there is no `MapGroup(schema)` to add the prefix centrally. Note this is the opposite of the endpoint rule above, where the route string must *not* include the schema.

**`.razor` holds markup only; every line of C# goes in the `.razor.cs` partial.** Code in an `@code { }` block compiles through the Razor source generator into output the analyzer pipeline doesn't gate — an unaudited hole in a repo where warnings are errors. There are zero `@code` blocks in the repo today; don't be the first.

**Code-behind shape** — `public sealed partial class <Page>`, property injection (not primary constructors):

```csharp
[Inject]
private ISerginUiDispatcher Dispatcher { get; set; } = default!;

[Inject]
private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

[Inject]
private NavigationManager Navigation { get; set; } = default!;   // only if the page navigates

[Parameter]
public Guid Id { get; set; }                                     // detail pages only
```

**Inject `ISerginUiDispatcher`, never `ISender`/`IMediator`.** In Blazor Server "scoped" is the SignalR circuit's lifetime, not a request's, so a directly resolved `ISender` would share one `DbContext` for as long as the tab is open — unbounded change tracker, stale first-level-cache reads, and "a second operation was started on this context" on parallel renders. `ScopedSerginUiDispatcher` opens a fresh scope per send.

Two calls, both returning `ErrorOr<T>`:
- `await Dispatcher.SendAsync(new Get<Aggregate>ByIdQueryCommand(Id))` → `ErrorOr<<Aggregate>QueryResponse>`; same call for commands → `ErrorOr<<Feature>CommandResponse>`.
- `await Dispatcher.SendListAsync<Get<Aggregate>ListItem>(state.PageSize, state.Page + 1, cancellationToken)` → `ErrorOr<ListQueryResponse<TItem>>`, whose `.Data` is `IReadOnlyCollection<TItem>` and `.Total` an `int`. The extension exists because list features have no dedicated command type. **`pageIndex` is 1-based while MudBlazor's `TableState.Page` is 0-based** — hence the `+ 1`, which every existing list page comments.

**Error handling, two shapes** — both go through `IUiErrorPresenter`, which maps the `Error` through the same `SerginProblemFactory` the API uses, so both surfaces render identical text for a given `error.Code`:
- **List page / any submit** → `ErrorPresenter.Notify(result.FirstError)` (snackbar), and for a list return `new TableData<TItem> { Items = [], TotalItems = 0 }`.
- **Detail page load** → `problem = ErrorPresenter.Present(result.FirstError)` into a `private SerginProblem? problem;` field, rendered by `<SerginProblemPanel Problem="problem" />` in the markup. Set `problem = null` on the success path. This is what makes a `[RequiredPermissions]` failure or a missing record show up as an inline alert instead of a blank page.

**Markup conventions** (copy from the reference pages): list page = `<MudTable T="TItem" ServerData="LoadAsync" OnRowClick="@(args => Open(args.Item))" RowsPerPage="10">` with `<MudTablePager />`; create page = `<EditForm Model="model" OnValidSubmit="SubmitAsync">` + `<DataAnnotationsValidator />` + `MudTextField @bind-Value="model.X" For="@(() => model.X)"`, with the submit button `Disabled="submitting"` against a `private bool submitting;` field. **Don't add sort/filter/search controls** — `ListQuery` carries `Term`/`Filtering`/`Sorting` fields, but `SendListAsync` forwards none of them and no query repository reads them, so such a control would silently do nothing. Wiring them through is a read-side feature of its own.

**Registration** — pages need no DI registration; the module's `UiAssembly` is already scanned. Two follow-ups only:
1. Add a `SerginNavItem` to `<Module>Navigation.Items` for the **list** page (detail/create pages are reached by navigation, not the menu). Existing entries: `new SerginNavItem("Devices", "/mm/devices", Icons.Material.Filled.Router, Order: 100)` and `new SerginNavItem("Users", "/ua/users", Icons.Material.Filled.People, Order: 200)`.
2. Add any new `@using` for the feature's Application namespace to the project's `_Imports.razor` (not to individual `.razor` files). The `.razor.cs` files use ordinary `using` statements on top of that project's `GlobalUsings.cs`.

If the query behind a page carries `[RequiredPermissions]`, add that permission string to `Sergin:DevUser:Permissions` in `src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All/appsettings.json` — the UI host has no real authentication and runs every request as that one configured user. An invalid entry fails startup naming the exact key and value.

## After scaffolding

1. Check each new project's `GlobalUsings.cs` before adding `using` statements — many namespaces (`ErrorOr`, `Sergin.SharedKernel.*`) are already global. In `.Presentation.Blazor` check `_Imports.razor` as well — it covers the markup, `GlobalUsings.cs` covers the code-behind.
2. If the feature needs new/changed columns, add or update the `IEntityTypeConfiguration` in `Sergin.<Module>.Infrastructure.Data`, then generate a migration:
   ```
   dotnet ef migrations add <Name> --project src/Modules/<Module>/Sergin.<Module>.Infrastructure.Data --startup-project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All
   ```
3. Build to confirm it compiles cleanly — this repo treats every analyzer/style warning as a build error:
   ```
   dotnet build Sergin.MeterMinder.slnx
   ```
4. If the slice added a UI page, run the UI host and hit the new route — the route-prefix guard and `Sergin:DevUser` validation both fail at startup, before a port opens, so "it starts" is itself a meaningful check:
   ```
   dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All   # http://localhost:5002
   ```
   The WebUi integration suite (`tests/Sergin.MeterMinder.IntegrationTests.WebUi.All`) asserts pages render server-side; add an `[InlineData]` route to `Shell/ModulePageRenderingTests.cs` for a new list or create page.
