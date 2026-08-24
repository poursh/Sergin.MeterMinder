# DeviceManagement gRPC Presentation split: Contracts / Client / Server

## Motivation

`Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` today is one project holding
everything: the generated proto code (messages + client stub + server base), the
client-side invoker (`GetDeviceByIdGrpcInvoker`), the Remote-module DI wiring
(`DeviceManagementRemoteModule`, `DeviceManagementRemoteServicesExtensions`), and the
server-side implementation (`DeviceGrpcService`). It is live-but-unhosted: no running
host maps `DeviceGrpcService` or consumes the Remote module today, so this project's
only real consumer is `DeviceGrpcRoundTripTests`, which stands up its own throwaway
Kestrel server.

Standing up a real second host that actually serves `DeviceGrpcService` (a client
process pulling in `Grpc.Net.Client` + the invoker, a server process pulling in
`Grpc.AspNetCore` + the service impl) would otherwise force both processes to
reference this one project and everything in it — including the half they don't run.
Splitting the project now, ahead of that host existing, means the boundary is right
the first time a consumer shows up, and gives every future module a template to copy
when it adds its own gRPC dispatch slice instead of re-deriving the shape from
scratch.

## Scope

**In scope:**
- Split DeviceManagement's `Presentation.Grpc` project into three: `Contracts`,
  `Client`, `Server`.
- Move the solution-explorer location of these three projects into a new nested
  `Grpc` solution folder under DeviceManagement's existing `Presentation` folder.
- Repoint `DeviceGrpcRoundTripTests`' project references to the new three projects.
- Generalize the root `CLAUDE.md`'s `.Presentation.Grpc` writeup so the three-project
  shape reads as the standard pattern for *any* module adding gRPC dispatch, not a
  DeviceManagement-specific one.
- Add the one new NuGet dependency this split requires (`Grpc.Core.Api`) to
  `Directory.Packages.props`.

**Explicitly out of scope for this round:**
- No new host project. Nothing stands up a real (non-test) Kestrel process serving
  `DeviceGrpcService`. `Sergin.MeterMinder.Hosts.All` keeps running
  `DeviceManagementModule` Local, unchanged — no flip to Remote.
- No `docker-compose.yml` changes.
- No UserAccess (or any other module) gRPC slice. UserAccess has no proto and no
  `ISerginRemoteModule` implementation today; this spec only documents the shape a
  future module should follow, it doesn't build one.

Both of these are natural follow-ups once something actually needs to run
`DeviceGrpcService` for real — tracked as future work, not part of this change.

## Current state

```
Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/
  DeviceManagementRemoteModule.cs        (ISerginRemoteModule impl, schema "dm")
  DeviceManagementRemoteServicesExtensions.cs   (AddDeviceManagementRemoteServices)
  Devices/
    DeviceGrpcService.cs                 (server: DeviceService.DeviceServiceBase)
    GetDeviceByIdGrpcInvoker.cs          (client: IRemoteInvoker<,>)
  Protos/devices.proto                   (GrpcServices="Both")
  GlobalUsings.cs
```

One `.csproj`: `FrameworkReference Microsoft.AspNetCore.App` +
`Google.Protobuf` + `Grpc.AspNetCore` + `Grpc.Net.Client` + `Grpc.Tools`, referencing
`.Application.Contracts`, `SharedKernel.Presentation.Grpc`,
`SharedKernel.Infrastructure`, `SharedKernel.Modules`.

Consumers today: only itself (self-contained) and
`tests/Sergin.MeterMinder.IntegrationTests.All` (`DeviceGrpcRoundTripTests`, which
references `DeviceGrpcService`, `GetDeviceByIdGrpcInvoker`, and the generated
`DeviceService.DeviceServiceClient`/messages all in the same test). The
DeviceManagement composition root (`Sergin.MeterMinder.DeviceManagement`) does **not**
reference this project at all — it's reached only by whichever host chooses to wire a
module Remote, which is nobody today.

## Design

### Why three projects, not two

`devices.proto` declares a real service (`DeviceService`), so compiling it produces
three things together: message classes (`DeviceData`, `GetDeviceByIdRequest`,
`GetDeviceByIdReply`), a server base class (`DeviceService.DeviceServiceBase`), and a
client stub (`DeviceService.DeviceServiceClient`). If `Client` and `Server` each
compiled their own copy of the proto (`GrpcServices="Client"` /
`GrpcServices="Server"`), Grpc.Tools still emits the message classes in *both* — same
namespace, same type names, two different assemblies. Anything that needs both sides
in one process — which `DeviceGrpcRoundTripTests` does today, and any future
in-process host composing Local+Remote for a demo would too — hits `CS0433` ambiguous
type the moment it references both projects.

A third project that compiles the proto exactly once (`GrpcServices="Both"`) and is
referenced by both `Client` and `Server` avoids the duplication entirely. This is the
same shape `Sergin.SharedKernel.Presentation.Grpc` already uses for `error.proto`
(one compile, shared downstream), just extended to a proto that has a service instead
of only messages.

### `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts`

Compiles `Protos/devices.proto` (moved from the old project, `GrpcServices="Both"`,
same `AdditionalImportDirs` pointing at SharedKernel's `error.proto`). Holds only
generated code — no hand-written `.cs` files.

- Packages: `Google.Protobuf`, `Grpc.Core.Api` (new — see Packages below),
  `Grpc.Tools` (build-only, `PrivateAssets=all`).
- No `FrameworkReference Microsoft.AspNetCore.App` — nothing here needs ASP.NET
  Core, only the lightweight `Grpc.Core.Api` types (`ServerCallContext`,
  `ClientBase<T>`) that both the generated base class and client stub derive from.
- `ProjectReference`: `SharedKernel.Presentation.Grpc` (for the `error.proto` import
  and the `ErrorReply`/`ProtoErrorType` types `devices.proto` references).

### `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client`

Moved verbatim from the old project: `GetDeviceByIdGrpcInvoker`,
`DeviceManagementRemoteModule`, `DeviceManagementRemoteServicesExtensions`.
Unchanged behavior — same `IRemoteInvoker<,>`/`ISerginRemoteModule` implementations,
same DI wiring, same `GrpcAddress` config lookup.

- Packages: `Grpc.Net.Client` (needed for `GrpcChannel.ForAddress` in
  `DeviceManagementRemoteServicesExtensions`).
- No `FrameworkReference Microsoft.AspNetCore.App` — a gRPC client has no ASP.NET
  Core dependency; a future non-ASP.NET-Core consumer (a console gateway, a
  worker service) could reference this project without pulling in the web
  framework.
- `ProjectReference`: `Contracts`, `.Application.Contracts`,
  `SharedKernel.Presentation.Grpc`, `SharedKernel.Infrastructure` (for
  `RemoteForwardingHandler<,>`), `SharedKernel.Modules` (for `ISerginRemoteModule`).

### `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server`

Moved verbatim: `DeviceGrpcService`. Unchanged behavior — same `ISender.Send`
dispatch, same `DeviceServiceBase` override.

- No extra gRPC package. `ServerCallContext`/`DeviceServiceBase` come transitively
  through `Contracts`' `Grpc.Core.Api` reference. `AddGrpc()`/`MapGrpcService<T>()`
  are host-composition calls, not something this library needs — it only has to
  compile the class, not map it.
- No `FrameworkReference Microsoft.AspNetCore.App` for the same reason.
- `ProjectReference`: `Contracts`, `.Application.Contracts`,
  `SharedKernel.Presentation.Grpc` (for `Error.ToErrorReply()`).

*(Whenever a real host is added later, it references `Server` + `Grpc.AspNetCore`
+ `FrameworkReference Microsoft.AspNetCore.App` itself to call `AddGrpc()` /
`MapGrpcService<DeviceGrpcService>()` — none of that lives in `Server` today, by
design, since no host exists yet to make that call.)*

### Solution folder placement

`Sergin.MeterMinder.slnx` currently has:

```xml
<Folder Name="/src/Modules/DeviceManagement/Presentation/">
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/...csproj" />
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/...csproj" />
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/...csproj" />
</Folder>
```

New shape nests a `Grpc` child folder under the existing `Presentation` folder,
holding the three new projects; the old flat `Presentation.Grpc` entry is removed:

```xml
<Folder Name="/src/Modules/DeviceManagement/Presentation/">
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/...csproj" />
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/...csproj" />
</Folder>
<Folder Name="/src/Modules/DeviceManagement/Presentation/Grpc/">
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/...csproj" />
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/...csproj" />
  <Project Path=".../Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/...csproj" />
</Folder>
```

This is the shape a future module's own gRPC slice should copy: a nested `Grpc`
solution folder under its own `Presentation` folder, holding its own
`Contracts`/`Client`/`Server` trio.

**Amended after initial implementation:** physical directories stay flat on disk —
`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.{Contracts,Client,Server}/`,
siblings of `.Application`, `.Domain`, `.Infrastructure`, `.Presentation.WebApi`, and
`.Presentation.Blazor` directly under `src/Modules/DeviceManagement/` — matching how
every other Presentation-layer project in this repo already sits. The `Presentation/`
and `Presentation/Grpc/` nesting shown above is `.slnx`-only (a `<Folder>` grouping),
never a physical directory. The original version of this section called for disk to
mirror the solution-folder nesting; that was reverted per explicit user instruction
after the branch's initial implementation and review, once it was pointed out this
made the gRPC trio the only Presentation-layer projects with a different on-disk
convention from WebApi/Blazor's flat layout — the .slnx-only grouping removes that
asymmetry instead of introducing it.

### Directory.Packages.props

Add one new entry, alphabetically placed among the existing `Grpc.*` entries:

```xml
<PackageVersion Include="Grpc.Core.Api" Version="2.83.0" />
```

(Same version as the other `Grpc.*` packages already pinned, for consistency.)
`Grpc.AspNetCore` is no longer referenced by any DeviceManagement project after this
split (it was only ever needed for the combined project's `FrameworkReference` +
hosting bits, none of which survive into `Contracts`/`Client`/`Server`) — left in
`Directory.Packages.props` regardless, since removing an unused central version entry
isn't required and SharedKernel or a future host may still need it.

### Test project changes

`tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`'s
single `ProjectReference` to the old `Presentation.Grpc` project becomes three
references, one each to `Contracts`, `Client`, `Server`. `DeviceGrpcRoundTripTests.cs`
itself needs no logic changes — its `using` statements already name
`Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` /
`...Presentation.Grpc.Devices` namespaces, which stay the same (namespace is
independent of which project a type physically lives in); only the project reference
resolving those types changes.

### CLAUDE.md documentation updates

Root `CLAUDE.md`'s `.Presentation.Grpc` paragraph (in the Overview and in "Host /
module composition") currently describes DeviceManagement's project by name as *the*
shape. Reword it to describe the **three-project split as the pattern**: `Contracts`
(proto codegen, referenced by both), `Client` (invoker + `ISerginRemoteModule` +
`AddRemoteServices` DI wiring), `Server` (the `XxxService : XxxServiceBase` impl) —
with DeviceManagement's as the one existing example, not the template's only instance.
Note explicitly that a future module adding gRPC dispatch should create its own
`<Module>.Presentation.Grpc.{Contracts,Client,Server}` trio under a nested
`Presentation/Grpc/` solution folder, rather than one combined project.

`src/Modules/DeviceManagement/CLAUDE.md`'s "gRPC dispatch slice" paragraph gets its
project name references updated (`Presentation.Grpc` → `Presentation.Grpc.Server` /
`.Client` as appropriate per class mentioned), content otherwise unchanged — it's
still live-but-unhosted, still the one proof slice, still exercised only by the
round-trip test.

## Non-goals recap

No new host, no docker-compose changes, no UserAccess gRPC slice — all deliberately
deferred until something real needs to consume this. The point of this change is that
DeviceManagement's gRPC layer already has the boundary that future consumer will want,
not that a consumer exists yet.

## Testing

`dotnet build Sergin.MeterMinder.slnx` must succeed (analyzers are errors —
`Directory.Build.props`). `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/...`
must still pass, in particular `DeviceGrpcRoundTripTests`' three existing `[Fact]`s
unchanged, proving the split didn't alter Local/Remote behavior — only project
boundaries.
