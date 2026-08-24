# DeviceManagement gRPC Contracts/Client/Server Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` (one project holding proto codegen, client invoker, and server implementation together) into three projects — `.Presentation.Grpc.Contracts`, `.Presentation.Grpc.Client`, `.Presentation.Grpc.Server` — nested under a new `Presentation/Grpc/` solution folder, with no behavior change.

**Architecture:** `Contracts` compiles `devices.proto` once (`GrpcServices="Both"`) and holds only generated code. `Client` (the invoker + `ISerginRemoteModule` DI wiring) and `Server` (`DeviceGrpcService`) each reference `Contracts` instead of recompiling the proto, which avoids the duplicate-message-class collision that would otherwise hit anything referencing both sides (the existing round-trip test does). All moved code is unchanged — only project boundaries move.

**Tech Stack:** .NET 10, Grpc.Tools/Google.Protobuf/Grpc.Core.Api/Grpc.Net.Client, MSBuild central package management (`Directory.Packages.props`), `.slnx` solution format.

**Spec:** `docs/superpowers/specs/2026-08-24-devicemanagement-grpc-client-server-split-design.md`

## Global Constraints

- `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer.CSharp — every new/moved file must build clean, no exceptions.
- Central Package Management is on: every `PackageReference` in a `.csproj` is version-less; every package version lives only in `Directory.Packages.props`, alphabetically ordered.
- No new host project, no `docker-compose.yml` changes, no UserAccess gRPC slice — out of scope per the spec.
- C# namespaces of moved types do not change (`Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` / `...Presentation.Grpc.Devices`) — only which project physically compiles them changes.
- Moved files must use `git mv` (or an equivalent move that preserves history), not delete+recreate, wherever the file's content is unchanged.

---

## File Structure

```
src/Modules/DeviceManagement/Presentation/Grpc/
  Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/
    Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj   (new)
    Protos/devices.proto                                                     (moved)
  Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/
    Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj      (new)
    GlobalUsings.cs                                                          (moved)
    DeviceManagementRemoteModule.cs                                          (moved)
    DeviceManagementRemoteServicesExtensions.cs                              (moved)
    Devices/GetDeviceByIdGrpcInvoker.cs                                      (moved)
  Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/
    Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj      (new)
    GlobalUsings.cs                                                          (new — same 2 lines as Client's)
    Devices/DeviceGrpcService.cs                                             (moved)
```

The old `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/` directory is deleted once empty (Task 4).

Files modified in place (not moved): `Directory.Packages.props`, `Sergin.MeterMinder.slnx`, `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`, root `.claude/CLAUDE.md`, `src/Modules/DeviceManagement/CLAUDE.md`.

---

### Task 1: Contracts project

**Files:**
- Modify: `Directory.Packages.props`
- Create: `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj`
- Move: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Protos/devices.proto` → `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Protos/devices.proto`

**Interfaces:**
- Consumes: `Sergin.SharedKernel.Presentation.Grpc`'s `error.proto` (via `AdditionalImportDirs`) — unchanged reference target.
- Produces: generated types `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.DeviceService` (`.DeviceServiceClient`, `.DeviceServiceBase`), `GetDeviceByIdRequest`, `GetDeviceByIdReply`, `DeviceData` — same namespace/names Task 2 and Task 3 will reference.

- [ ] **Step 1: Add `Grpc.Core.Api` to `Directory.Packages.props`**

Edit `Directory.Packages.props`, inserting alphabetically between the existing `Grpc.AspNetCore` and `Grpc.Net.Client` lines:

```xml
		<PackageVersion Include="Grpc.AspNetCore" Version="2.83.0" />
		<PackageVersion Include="Grpc.Core.Api" Version="2.83.0" />
		<PackageVersion Include="Grpc.Net.Client" Version="2.83.0" />
```

- [ ] **Step 2: Create the Contracts project directory and move the proto file**

```bash
mkdir -p "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Protos"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Protos/devices.proto" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Protos/devices.proto"
```

- [ ] **Step 3: Write the Contracts csproj**

Create `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<PackageReference Include="Google.Protobuf" />
		<PackageReference Include="Grpc.Core.Api" />
		<PackageReference Include="Grpc.Tools">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
	</ItemGroup>

	<ItemGroup>
		<Protobuf Include="Protos\devices.proto" GrpcServices="Both" AdditionalImportDirs="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Protos" />
	</ItemGroup>

</Project>
```

- [ ] **Step 4: Verify it builds standalone**

Run: `dotnet build "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj"`
Expected: `Build succeeded.` — confirms `devices.proto` compiles standalone with the new package set and the `AdditionalImportDirs` path resolves `error.proto` correctly from the deeper directory.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts"
git commit -m "Add DeviceManagement gRPC Contracts project"
```

---

### Task 2: Client project

**Files:**
- Create: `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj`
- Move: `.../Presentation.Grpc/GlobalUsings.cs` → `.../Grpc.Client/GlobalUsings.cs`
- Move: `.../Presentation.Grpc/DeviceManagementRemoteModule.cs` → `.../Grpc.Client/DeviceManagementRemoteModule.cs`
- Move: `.../Presentation.Grpc/DeviceManagementRemoteServicesExtensions.cs` → `.../Grpc.Client/DeviceManagementRemoteServicesExtensions.cs`
- Move: `.../Presentation.Grpc/Devices/GetDeviceByIdGrpcInvoker.cs` → `.../Grpc.Client/Devices/GetDeviceByIdGrpcInvoker.cs`

**Interfaces:**
- Consumes: `DeviceService.DeviceServiceClient` from Task 1's Contracts project; `GetDeviceByIdQueryCommand`/`DeviceQueryResponse` from `.Application.Contracts`; `IRemoteInvoker<,>`/`Error.ToErrorReply()`/`ErrorReply.ToErrorOr<T>()` from `SharedKernel.Presentation.Grpc`; `RemoteForwardingHandler<,>` from `SharedKernel.Infrastructure`; `ISerginRemoteModule` from `SharedKernel.Modules`.
- Produces: `DeviceManagementRemoteModule` (`ISerginRemoteModule`, schema `"dm"`), `AddDeviceManagementRemoteServices` extension — unchanged public surface, now in this project.

- [ ] **Step 1: Create the Client project directory and move its four files**

```bash
mkdir -p "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Devices"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/GlobalUsings.cs" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/GlobalUsings.cs"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteModule.cs" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/DeviceManagementRemoteModule.cs"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/DeviceManagementRemoteServicesExtensions.cs" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/DeviceManagementRemoteServicesExtensions.cs"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Devices/GetDeviceByIdGrpcInvoker.cs" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Devices/GetDeviceByIdGrpcInvoker.cs"
```

- [ ] **Step 2: Write the Client csproj**

Create `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<PackageReference Include="Grpc.Net.Client" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj" />
		<ProjectReference Include="..\..\..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
		<ProjectReference Include="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
		<ProjectReference Include="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Infrastructure\Sergin.SharedKernel.Infrastructure.csproj" />
		<ProjectReference Include="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Modules\Sergin.SharedKernel.Modules.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 3: Verify it builds standalone**

Run: `dotnet build "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj"`
Expected: `Build succeeded.` — confirms `GetDeviceByIdGrpcInvoker`/`DeviceManagementRemoteModule`/`DeviceManagementRemoteServicesExtensions` compile unchanged against Task 1's `Contracts` project.

- [ ] **Step 4: Commit**

```bash
git add "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client"
git commit -m "Add DeviceManagement gRPC Client project"
```

---

### Task 3: Server project

**Files:**
- Create: `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj`
- Create: `.../Grpc.Server/GlobalUsings.cs`
- Move: `.../Presentation.Grpc/Devices/DeviceGrpcService.cs` → `.../Grpc.Server/Devices/DeviceGrpcService.cs`

**Interfaces:**
- Consumes: `DeviceService.DeviceServiceBase` from Task 1's Contracts project; `GetDeviceByIdQueryCommand`/`DeviceQueryResponse` from `.Application.Contracts`; `Error.ToErrorReply()` from `SharedKernel.Presentation.Grpc`; `ISender` (MediatR, via `GlobalUsings.cs`).
- Produces: `DeviceGrpcService : DeviceService.DeviceServiceBase` — unchanged public surface, now in this project. A future host references this project + adds its own `Grpc.AspNetCore`/`FrameworkReference Microsoft.AspNetCore.App` to call `AddGrpc()`/`MapGrpcService<DeviceGrpcService>()`.

- [ ] **Step 1: Create the Server project directory and move `DeviceGrpcService.cs`**

```bash
mkdir -p "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Devices"
git mv "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Devices/DeviceGrpcService.cs" \
       "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Devices/DeviceGrpcService.cs"
```

- [ ] **Step 2: Write `GlobalUsings.cs`**

Create `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/GlobalUsings.cs`:

```csharp
global using ErrorOr;
global using MediatR;
```

- [ ] **Step 3: Write the Server csproj**

Create `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<ItemGroup>
		<ProjectReference Include="..\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj" />
		<ProjectReference Include="..\..\..\Sergin.MeterMinder.DeviceManagement.Application.Contracts\Sergin.MeterMinder.DeviceManagement.Application.Contracts.csproj" />
		<ProjectReference Include="..\..\..\..\..\SharedKernel\Sergin.SharedKernel.Presentation.Grpc\Sergin.SharedKernel.Presentation.Grpc.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 4: Verify it builds standalone**

Run: `dotnet build "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj"`
Expected: `Build succeeded.` — confirms `DeviceGrpcService` compiles with no `Grpc.AspNetCore`/`FrameworkReference` at all, proving `ServerCallContext`/`DeviceServiceBase` really do come transitively through `Contracts`' `Grpc.Core.Api` reference.

- [ ] **Step 5: Commit**

```bash
git add "src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server"
git commit -m "Add DeviceManagement gRPC Server project"
```

---

### Task 4: Remove the old project, update the solution and the test project, verify everything together

**Files:**
- Delete: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/` (now-empty directory tree plus its `.csproj`)
- Modify: `Sergin.MeterMinder.slnx`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`

**Interfaces:**
- Consumes: nothing new — this task only repoints references to the projects Tasks 1–3 already produced.
- Produces: a solution where the only surviving `.Presentation.Grpc*` projects are the three new ones, and the round-trip test proves Local/Remote behavior is unchanged.

- [ ] **Step 1: Delete the old project's leftover `.csproj` and the now-empty directory**

Everything else under the old project directory was already moved by Tasks 1–3; only the `.csproj` itself remains.

```bash
git rm "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj"
```

Confirm the directory is now empty (ignoring `bin`/`obj`, which are gitignored build output, not tracked content):

```bash
find "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc" -type f | grep -v -e /obj/ -e /bin/
```

Expected: no output. If `bin`/`obj` are the only things left, remove the empty directory tree:

```bash
rm -rf "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc"
```

- [ ] **Step 2: Update `Sergin.MeterMinder.slnx`**

Find this block:

```xml
  <Folder Name="/src/Modules/DeviceManagement/Presentation/">
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj" />
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj" />
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj" />
  </Folder>
```

Replace with:

```xml
  <Folder Name="/src/Modules/DeviceManagement/Presentation/">
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi/Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.csproj" />
    <Project Path="src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.csproj" />
  </Folder>
  <Folder Name="/src/Modules/DeviceManagement/Presentation/Grpc/">
    <Project Path="src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj" />
    <Project Path="src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj" />
    <Project Path="src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj" />
  </Folder>
```

- [ ] **Step 3: Repoint the integration test project's reference**

In `tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`, find:

```xml
  <ItemGroup>
    <!--
      Needed for DeviceGrpcRoundTripTests.cs, which spins up its own from-scratch WebApplication/Kestrel
      host on a loopback port. Not transitively supplied by the ProjectReferences below: Hosts.All is
      Microsoft.NET.Sdk.Web (implicit reference, not transitive to a plain Microsoft.NET.Sdk consumer),
      and DeviceManagement.Presentation.Grpc's own FrameworkReference does not propagate either.
    -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

Replace with (only the trailing clause of the comment changes — none of the three new projects carry a `FrameworkReference` at all, unlike the old combined project):

```xml
  <ItemGroup>
    <!--
      Needed for DeviceGrpcRoundTripTests.cs, which spins up its own from-scratch WebApplication/Kestrel
      host on a loopback port. Not transitively supplied by the ProjectReferences below: Hosts.All is
      Microsoft.NET.Sdk.Web (implicit reference, not transitive to a plain Microsoft.NET.Sdk consumer),
      and none of DeviceManagement.Presentation.Grpc.{Contracts,Client,Server} carry a FrameworkReference
      of their own either.
    -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

Then find:

```xml
    <ProjectReference Include="..\..\src\Modules\DeviceManagement\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.csproj" />
```

Replace with:

```xml
    <ProjectReference Include="..\..\src\Modules\DeviceManagement\Presentation\Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Contracts.csproj" />
    <ProjectReference Include="..\..\src\Modules\DeviceManagement\Presentation\Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client.csproj" />
    <ProjectReference Include="..\..\src\Modules\DeviceManagement\Presentation\Grpc\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server\Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Server.csproj" />
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: `Build succeeded.`, zero warnings (`TreatWarningsAsErrors=true`), no project referencing the deleted path.

- [ ] **Step 5: Run the round-trip tests**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceGrpcRoundTripTests"`
Expected: 3 passed (`RemoteDispatch_ForExistingDevice_ReturnsSameResultAsLocalHandler`, `RemoteDispatch_ForMissingDevice_ReturnsNotFound`, `RemoteDispatch_WithoutRequiredPermission_ReturnsForbidden`), 0 failed. This test needs no Docker/Testcontainers — it hosts its own loopback Kestrel server — so the filter avoids requiring the rest of the suite's Postgres container.

- [ ] **Step 6: Commit**

```bash
git add -A -- "src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc" \
              "Sergin.MeterMinder.slnx" \
              "tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj"
git commit -m "Remove old DeviceManagement.Presentation.Grpc project, repoint solution and tests to the split"
```

---

### Task 5: Update root CLAUDE.md

**Files:**
- Modify: `.claude/CLAUDE.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Rewrite the `.Presentation.Grpc` paragraph in the Overview section**

Find this paragraph (in the Overview section, the one starting "**`.Presentation.Grpc` is the same story, one layer deeper.**"):

```
**`.Presentation.Grpc` is the same story, one layer deeper.** DeviceManagement carries a `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` project (client invoker + server `DeviceService`, one real feature slice: `GetDeviceById`), and SharedKernel carries the shared contract types in `Sergin.SharedKernel.Presentation.Grpc` (`IRemoteInvoker<,>`, generated `ErrorReply`/`ProtoErrorType`). This is half of the dual-mode dispatch mechanism — `AddSerginCore` takes two collections, `IReadOnlyCollection<ISerginModule> localModules` and an optional `IReadOnlyCollection<ISerginRemoteModule>? remoteModules`, and which collection a module is passed in *is* the Local/Remote choice; there is no runtime config key. A Remote module's `AddRemoteServices` registers `RemoteForwardingHandler<TRequest, TResponse>` (`Sergin.SharedKernel.Infrastructure.Dispatching`) per feature — a real MediatR `IRequestHandler` that forwards to an `IRemoteInvoker<,>` over gRPC instead of running the real handler in-process, so `ISender.Send` routes a Remote request through the same pipeline behaviors (`PermissionCheckPipelineBehavior`, `ValidationPipelineBehavior`) a Local handler gets, with no separate route-resolution step. DeviceManagement's `.Presentation.Grpc` project carries the one real `ISerginRemoteModule` implementation, `DeviceManagementRemoteModule`/`DeviceManagementRemoteServicesExtensions`, for the `dm` schema. **Today the real host passes only `localModules`** (`[new DeviceManagementModule(), new UserAccessModule()]`) and no `remoteModules` at all, so `.Presentation.Grpc` — and `DeviceManagementRemoteModule` with it — is **live-but-unhosted, not dead**, the same as `.Presentation.WebApi`: it's referenced only by the integration test project (`DeviceGrpcRoundTripTests`, which spins up a real loopback Kestrel gRPC server itself), not by any running host process. Making `dm` Remote in the real host would need a second real host process actually serving `DeviceGrpcService`, plus passing `[new DeviceManagementRemoteModule()]` as `remoteModules` here instead of the local module — nothing in the repo stands either half up yet.
```

Replace with:

```
**`.Presentation.Grpc` is the same story, one layer deeper — and it's a three-project pattern, not a single one.** A module's gRPC dispatch slice splits into `.Presentation.Grpc.Contracts` (compiles the module's `.proto` exactly once, `GrpcServices="Both"` — holds only generated code, referenced by both of the next two), `.Presentation.Grpc.Client` (the invoker implementing `IRemoteInvoker<,>` plus the module's `ISerginRemoteModule` DI wiring), and `.Presentation.Grpc.Server` (the `XxxService : XxxServiceBase` implementation) — nested under a `Presentation/Grpc/` solution folder alongside the module's `Presentation/WebApi`/`Presentation/Blazor` folder. Splitting the proto compile out into its own project matters because a service-bearing `.proto` generates message classes *and* the client stub *and* the server base together — compiling it twice (once each in `Client` and `Server`) would duplicate the message classes under the same namespace in two assemblies, colliding the moment anything references both sides in one process, which `DeviceGrpcRoundTripTests` does. DeviceManagement carries the one real instance of this pattern today: `src/Modules/DeviceManagement/Presentation/Grpc/Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.{Contracts,Client,Server}` (one real feature slice: `GetDeviceById`), and SharedKernel carries the shared contract types in `Sergin.SharedKernel.Presentation.Grpc` (`IRemoteInvoker<,>`, generated `ErrorReply`/`ProtoErrorType`). This is half of the dual-mode dispatch mechanism — `AddSerginCore` takes two collections, `IReadOnlyCollection<ISerginModule> localModules` and an optional `IReadOnlyCollection<ISerginRemoteModule>? remoteModules`, and which collection a module is passed in *is* the Local/Remote choice; there is no runtime config key. A Remote module's `AddRemoteServices` registers `RemoteForwardingHandler<TRequest, TResponse>` (`Sergin.SharedKernel.Infrastructure.Dispatching`) per feature — a real MediatR `IRequestHandler` that forwards to an `IRemoteInvoker<,>` over gRPC instead of running the real handler in-process, so `ISender.Send` routes a Remote request through the same pipeline behaviors (`PermissionCheckPipelineBehavior`, `ValidationPipelineBehavior`) a Local handler gets, with no separate route-resolution step. DeviceManagement's `.Presentation.Grpc.Client` project carries the one real `ISerginRemoteModule` implementation, `DeviceManagementRemoteModule`/`DeviceManagementRemoteServicesExtensions`, for the `dm` schema; `.Presentation.Grpc.Server` carries `DeviceGrpcService`. **Today the real host passes only `localModules`** (`[new DeviceManagementModule(), new UserAccessModule()]`) and no `remoteModules` at all, so this whole slice — and `DeviceManagementRemoteModule` with it — is **live-but-unhosted, not dead**, the same as `.Presentation.WebApi`: it's referenced only by the integration test project (`DeviceGrpcRoundTripTests`, which spins up a real loopback Kestrel gRPC server itself), not by any running host process. Making `dm` Remote in the real host would need a second real host process actually serving `DeviceGrpcService`, plus passing `[new DeviceManagementRemoteModule()]` as `remoteModules` here instead of the local module — nothing in the repo stands either half up yet. Any future module adding gRPC dispatch (UserAccess or otherwise) should copy this same three-project-under-a-nested-`Grpc`-solution-folder shape from the start.
```

- [ ] **Step 2: Update the `.Presentation.Grpc` mention in the `ApplicationAssembly` vs. `ContractsAssembly` paragraph**

Find:

```
Every `.Presentation.WebApi`/`.Presentation.Blazor` project (and, for DeviceManagement, `.Presentation.Grpc`) references `.Application.Contracts` instead of `.Application`, so presentation no longer transitively pulls in handlers, repository interfaces, or `IUnitOfWork`.
```

Replace with:

```
Every `.Presentation.WebApi`/`.Presentation.Blazor` project (and, for DeviceManagement, `.Presentation.Grpc.Client`/`.Presentation.Grpc.Server` — `.Presentation.Grpc.Contracts` references neither, it's generated code only) references `.Application.Contracts` instead of `.Application`, so presentation no longer transitively pulls in handlers, repository interfaces, or `IUnitOfWork`.
```

- [ ] **Step 3: Commit**

```bash
git add .claude/CLAUDE.md
git commit -m "Generalize root CLAUDE.md's .Presentation.Grpc writeup into the Contracts/Client/Server pattern"
```

---

### Task 6: Update DeviceManagement's own CLAUDE.md

**Files:**
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Update the "gRPC dispatch slice" paragraph's project names**

Find this paragraph (in the `Devices` aggregate section, right after the feature-slice table):

```
`GetOne` also has a second transport, alongside its WebApi endpoint above: `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc` (`GetDeviceByIdGrpcInvoker` client-side, `DeviceGrpcService` server-side) implements the same `GetDeviceByIdQueryCommand` over gRPC — the one real proof slice for the platform's dual-mode (MediatR/gRPC) dispatch mechanism documented in the root `CLAUDE.md` under "Host / module composition". Both still end in `ISender.Send(GetDeviceByIdQueryCommand)` — the WebApi side directly via `ISender.Send`, the gRPC side via the same call inside `DeviceGrpcService` — no wrapper on either side — only the transport in front differs. This same project also carries `DeviceManagementRemoteModule`/`AddDeviceManagementRemoteServices` (`DeviceManagementRemoteServicesExtensions`), the module's `ISerginRemoteModule` implementation for schema `dm`: it registers a `RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>` bound to `GetDeviceByIdGrpcInvoker`'s `IRemoteInvoker<,>`, so a host that passes `[new DeviceManagementRemoteModule()]` as `AddSerginCore`'s `remoteModules` (instead of `DeviceManagementModule` in `localModules`) would dispatch this same command through gRPC with no other code change. **Live-but-unhosted**: nothing maps `DeviceGrpcService` into a running host today, and the real host's `Program.cs` passes `DeviceManagementModule` as a `localModules` entry, never `DeviceManagementRemoteModule` as a `remoteModules` one — so this project is exercised only by `DeviceGrpcRoundTripTests` in the outer test project, which hosts it on its own loopback Kestrel server.
```

Replace with:

```
`GetOne` also has a second transport, alongside its WebApi endpoint above: `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client` (`GetDeviceByIdGrpcInvoker`) and `.Server` (`DeviceGrpcService`), both compiling against `.Contracts`' generated proto code, implement the same `GetDeviceByIdQueryCommand` over gRPC — the one real proof slice for the platform's dual-mode (MediatR/gRPC) dispatch mechanism documented in the root `CLAUDE.md` under "Host / module composition". Both still end in `ISender.Send(GetDeviceByIdQueryCommand)` — the WebApi side directly via `ISender.Send`, the gRPC side via the same call inside `DeviceGrpcService` — no wrapper on either side — only the transport in front differs. The `Client` project also carries `DeviceManagementRemoteModule`/`AddDeviceManagementRemoteServices` (`DeviceManagementRemoteServicesExtensions`), the module's `ISerginRemoteModule` implementation for schema `dm`: it registers a `RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>` bound to `GetDeviceByIdGrpcInvoker`'s `IRemoteInvoker<,>`, so a host that passes `[new DeviceManagementRemoteModule()]` as `AddSerginCore`'s `remoteModules` (instead of `DeviceManagementModule` in `localModules`) would dispatch this same command through gRPC with no other code change. **Live-but-unhosted**: nothing maps `DeviceGrpcService` into a running host today, and the real host's `Program.cs` passes `DeviceManagementModule` as a `localModules` entry, never `DeviceManagementRemoteModule` as a `remoteModules` one — so these projects are exercised only by `DeviceGrpcRoundTripTests` in the outer test project, which hosts it on its own loopback Kestrel server.
```

- [ ] **Step 2: Commit**

```bash
git add "src/Modules/DeviceManagement/CLAUDE.md"
git commit -m "Update DeviceManagement CLAUDE.md project names for the gRPC Contracts/Client/Server split"
```

---

## Self-Review

**Spec coverage:**
- Three-project split (Contracts/Client/Server), reasoning about proto duplication → Task 1–3.
- Nested `Presentation/Grpc/` solution folder (both `.slnx` and physical disk layout) → Task 1–4.
- `Directory.Packages.props` gets `Grpc.Core.Api` → Task 1.
- `DeviceGrpcRoundTripTests` repointed, still passing → Task 4.
- Root `CLAUDE.md` generalized to the standard pattern → Task 5.
- `src/Modules/DeviceManagement/CLAUDE.md` project names updated → Task 6.
- Explicit non-goals (no new host, no docker-compose, no UserAccess slice) → nothing in this plan touches any of those files; confirmed no task references `docker-compose.yml`, no task creates a `Hosts.*` project, no task creates anything under `src/Modules/UserAccess/`.

**Placeholder scan:** no TBD/TODO, no "add appropriate X", no "similar to Task N" — every task's code blocks are complete file contents or exact find/replace text.

**Type consistency:** `DeviceService.DeviceServiceClient`/`DeviceServiceBase`, `GetDeviceByIdQueryCommand`, `DeviceQueryResponse`, `IRemoteInvoker<,>`, `RemoteForwardingHandler<,>`, `ISerginRemoteModule` are used identically (same names, same namespaces) across Tasks 1–4 — none were renamed, matching the spec's explicit "namespace stays the same" decision.
