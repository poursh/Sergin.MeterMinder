# Dual-Mode Dispatch Contract — Design Spec

- **Date**: 2026-08-21
- **Status**: Approved (brainstorming dialogue, all sections signed off)
- **Goal**: Give `ISerginUiDispatcher` a dispatch contract that lets each module run **in-process (MediatR)** or **as an independently-deployed service (gRPC)**, switchable **per module** via config, with page call sites unchanged either way. This spec answers Open Question 1 ("what is the unit of remote dispatch?") from the prior investigation into splitting DeviceManagement and UserAccess into independently-deployed services (not committed to this repo — an ad-hoc report from the same conversation this spec was brainstormed in).

## Problem

Today `ISerginUiDispatcher.SendAsync<TResponse>(IRequest<ErrorOr<TResponse>>)` resolves its handler by MediatR assembly-scanning the module's `.Application` assembly, loaded in the same process as the UI. Nothing in the repo defines how a command, query, or `ErrorOr<T>` result would serialize across a process boundary, and the shared `ListQuery<T>` type has no discriminator beyond its CLR generic argument — three modules' list features would be wire-identical. There is no way today to run one module in-process while another runs remotely, and no config surface to choose per module.

## Decisions made during brainstorming

1. **Scope: both modules** (DeviceManagement, UserAccess) — the contract must generalize, not fit one module's shape.
2. **Topology: one shared Blazor UI host**, module *backends* (Application + Domain + Infrastructure) move out; the UI host stays a single process reaching module handlers over a boundary that may or may not be a network hop.
3. **Transport: gRPC.** Rejected HTTP/JSON reuse of the existing (unhosted) `.Presentation.WebApi` endpoints — user wants a distinct services boundary, not repurposed API endpoints. Rejected a message bus — turns a synchronous page-submit-and-wait interaction into request-reply-over-queue, a bigger behavioral shift than the UI warrants today.
4. **Contract-first `.proto`**, compiled by `Grpc.Tools` — not code-first `protobuf-net.Grpc`. Standard tooling, explicit flat wire schema, and (see decision 6) solves the list-query discriminator for free.
5. **One rpc method per feature** (`CreateDevice`, `GetDeviceById`, `GetDeviceList`, …), not a single generic `Dispatch(envelope)` rpc. Chosen because it kills the list-query discriminator problem structurally, and it turned out to also be the shape that makes decision 6 cheap.
6. **Dev/prod switch is per module, not global.** DeviceManagement can run Local while UserAccess runs Remote, or any combination. The page call site (`Dispatcher.SendAsync(new CreateDeviceCommand(...))`) must be identical in both modes — this is the requirement that shapes the whole design below. It falls out of decision 5 almost for free: a request-type → strategy registry can point any given type at either a local MediatR send or a remote gRPC call independently, so per-module (or even per-feature) granularity costs nothing extra once the registry exists.
7. **Routing approach: per-request-type route registry** (one small adapter per feature, matching the repo's existing "one interface, one `AddTransient`, per feature" convention) — chosen over a central switch-statement dispatcher (grows unbounded, fights the "feature folder owns its slice" convention used everywhere else in this codebase) and over keeping a generic envelope on the wire (reopens the discriminator problem decision 5 just closed, abandons contract-first).
8. **MediatR/`ISender` stays the single gateway into the Application layer, regardless of transport.** Application is the core; every Presentation adapter — Blazor's dispatcher, `.Presentation.WebApi`'s `IEndpoint`s, and now `.Presentation.Grpc`'s server-side services — is a thin translator that ends in `ISender.Send(...)`, never a second path into a handler. `IRemoteInvoker<TRequest,TResponse>` (decision 7) is a **client-side** stub only, used because the UI process has no handler for a Remote module's requests loaded locally. It is not a bypass of MediatR — see §3.

## Non-goals

- **Does not add authentication.** No auth exists in this repo today (see the linked investigation, §02) and this spec does not add any. It carries minimal identity in gRPC metadata for the remote side to log/assert against — not a trust boundary.
- **Does not decide whether cross-module transactions are needed.** The investigation found none exist today; this spec doesn't change that.
- **Does not implement Remote-mode test coverage.** Flagged as follow-up (see §8, Testing).
- **Does not turn on real service discovery infrastructure beyond what Remote mode strictly needs** — it uncomments and configures the existing `AddServiceDiscovery()`/`AddStandardResilienceHandler()` calls in `AddServiceDefaults`, nothing more.

## Architecture

| Piece | Home | Content |
|---|---|---|
| `*.proto` files, one per aggregate | **new** `src/Modules/<Module>/Sergin.<Module>.Presentation.Grpc` | Contract-first service + message definitions, mirrors `.Presentation.WebApi`'s per-aggregate file layout |
| `IRemoteInvoker<TRequest, TResponse>` implementations | same `.Presentation.Grpc` project | One per feature — maps request → proto, calls generated client, maps `oneof` reply → `ErrorOr<TResponse>` |
| `Error` proto message + `ToErrorOr()`/`ToErrorReply()` | **new** `Sergin.SharedKernel.Presentation.Grpc` | Shared `{Code, Description, Type}` mapping, written once, reused by every invoker |
| `RoutingSerginUiDispatcher`, `IDispatchRouteResolver` | `Sergin.SharedKernel.Presentation.Blazor.Dispatching` | Replaces `ScopedSerginUiDispatcher` as the `ISerginUiDispatcher` implementation |
| `<Aggregate>GrpcService : <Aggregate>Service.<Aggregate>ServiceBase` | same `.Presentation.Grpc` project, **server-side** | Runs inside the module's own process when Remote. Proto request → Application command/query → `ISender.Send(...)` → `ErrorOr<T>` → proto reply. Structurally the same adapter shape as `IEndpoint`, just a different transport |
| `DispatchModeOptions`, `DispatchModeOptionsValidator` | `Sergin.SharedKernel.Hosts` | Per-module (schema-keyed) Local/Remote config, validated like `DevUserOptions` |
| `<Module>Module` split into Backend half + Shell half | each module's composition root | `Schema`/`UiAssembly`/`NavItems` always registered; `AddServices`/`MigrateAsync`/`ApplicationAssembly` only wired when that module's mode is Local |

## 1. Contract shape per feature

One `.proto` per aggregate, matching the existing `Map<Aggregate>Endpoints` file-per-aggregate convention:

```proto
service DeviceService {
  rpc CreateDevice (CreateDeviceRequest) returns (CreateDeviceReply);
  rpc GetDeviceById (GetDeviceByIdRequest) returns (GetDeviceReply);
  rpc GetDeviceList (GetDeviceListRequest) returns (GetDeviceListReply);
}

message CreateDeviceRequest {
  string device_id = 1;
  string manufacturer_id = 2;
}

message CreateDeviceReply {
  oneof result {
    CreateDeviceCommandResponse success = 1;
    sergin.shared.ErrorReply error = 2;
  }
}
```

Messages carry flat primitives, mirroring the flattening the WebApi endpoints already do (`NewDeviceModel`) — never a domain value object (`DeviceId`, `ManufacturerId`) on the wire. `GetDeviceList` and `GetUserList` are distinct rpc methods on distinct services with distinct message types; the discriminator problem the investigation flagged does not exist in this shape — there is nothing that needs one.

## 2. Routing abstraction

```csharp
// Sergin.SharedKernel.Presentation.Grpc
internal interface IRemoteInvoker<TRequest, TResponse>
    where TRequest : IRequest<ErrorOr<TResponse>>
{
    Task<ErrorOr<TResponse>> InvokeAsync(TRequest request, CancellationToken ct);
}
```

```csharp
// src/Modules/DeviceManagement/.../Presentation.Grpc/Devices/CreateDeviceGrpcInvoker.cs
internal sealed class CreateDeviceGrpcInvoker(DeviceService.DeviceServiceClient client)
    : IRemoteInvoker<CreateDeviceCommand, CreateDeviceCommandResponse>
{
    public async Task<ErrorOr<CreateDeviceCommandResponse>> InvokeAsync(
        CreateDeviceCommand request, CancellationToken ct)
    {
        CreateDeviceReply reply = await client.CreateDeviceAsync(
            new CreateDeviceRequest
            {
                DeviceId = request.DeviceId.Value,
                ManufacturerId = request.ManufacturerId.Value.ToString(),
            },
            cancellationToken: ct);

        return reply.ResultCase == CreateDeviceReply.ResultOneofCase.Error
            ? reply.Error.ToErrorOr<CreateDeviceCommandResponse>()
            : new CreateDeviceCommandResponse(Guid.Parse(reply.Success.Id));
    }
}
```

Registered the same way every other one-interface-per-feature type in this codebase is: `services.AddTransient<IRemoteInvoker<CreateDeviceCommand, CreateDeviceCommandResponse>, CreateDeviceGrpcInvoker>()`, called from the module's Grpc-client registration extension.

```csharp
// Sergin.SharedKernel.Presentation.Blazor.Dispatching
internal sealed class RoutingSerginUiDispatcher(
    IServiceScopeFactory scopeFactory,
    IServiceProvider rootProvider,
    IDispatchRouteResolver routeResolver) : ISerginUiDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> invokerTypeCache = new();

    public async Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default)
    {
        Type requestType = request.GetType();

        if (routeResolver.IsRemote(requestType))
        {
            Type invokerType = invokerTypeCache.GetOrAdd(
                (requestType, typeof(TResponse)),
                key => typeof(IRemoteInvoker<,>).MakeGenericType(key.Request, key.Response));

            object invoker = rootProvider.GetRequiredService(invokerType);
            return await ((dynamic)invoker).InvokeAsync((dynamic)request, cancellationToken);
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }
}
```

**Implementation note, stated plainly rather than hidden:** `SendAsync<TResponse>` only has the closed `TResponse` at compile time, not the concrete `TRequest` — the same constraint MediatR's own `ISender.Send` resolves internally via `request.GetType()`. The `dynamic` double-dispatch above is the mechanical cost of keeping the page-facing signature unchanged; it is resolved once per distinct request type via the cache, not per call.

`IDispatchRouteResolver.IsRemote(Type requestType)` maps the request type's declaring assembly to a module schema (same reflection style the `@page` prefix guard already uses at startup) and looks up that schema in `DispatchModeOptions`.

## 3. Module-side gRPC adapter — the third Presentation adapter

`IRemoteInvoker<TRequest,TResponse>` (§2) lives on the **caller's** side — the UI process, which under Remote mode never loads that module's `.Application`/`.Domain` assemblies and therefore has no handler to send to locally. It is a client stub, nothing more.

Inside the module's own process (wherever it actually runs when configured Remote), the picture is unchanged from today: `ISender`/MediatR is still the only way into a handler. The new piece is a server-side gRPC service, one per aggregate, in the same `.Presentation.Grpc` project, structurally identical to an `IEndpoint` in `.Presentation.WebApi` — just a different transport wrapping the same call:

```csharp
// src/Modules/DeviceManagement/.../Presentation.Grpc/Devices/DeviceGrpcService.cs
internal sealed class DeviceGrpcService(ISender sender) : DeviceService.DeviceServiceBase
{
    public override async Task<CreateDeviceReply> CreateDevice(
        CreateDeviceRequest request, ServerCallContext context)
    {
        ErrorOr<CreateDeviceCommandResponse> result = await sender.Send(
            new CreateDeviceCommand(
                new DeviceId(request.DeviceId),
                new ManufacturerId(Guid.Parse(request.ManufacturerId))),
            context.CancellationToken);

        return result.Match(
            response => new CreateDeviceReply { Success = new() { Id = response.Id.ToString() } },
            errors => new CreateDeviceReply { Error = errors[0].ToErrorReply() });
    }
}
```

Same MediatR pipeline behaviors run either way — `PermissionCheckPipelineBehavior`, `ValidationPipelineBehavior` — because both paths converge on the same `ISender.Send`. Local mode's `RoutingSerginUiDispatcher` and Remote mode's `DeviceGrpcService` are two doors into the same gateway; Application never has to know or care which one was used. This is the same relationship `IEndpoint` already has to `ISender` today — gRPC does not introduce a second kind of entry point into this codebase, it adds a second *transport* for the one that already exists.

## 4. Mode selection, per module

```csharp
public sealed class DispatchModeOptions
{
    public required IReadOnlyDictionary<string, DispatchMode> Modules { get; init; } // key: schema, e.g. "dm", "ua"
}

public enum DispatchMode { Local, Remote }
```

Bound and validated the same way `DevUserOptions` is (`.Bind(...).ValidateOnStart()` + a dedicated `IValidateOptions<DispatchModeOptions>`): startup fails naming the missing schema if a registered module has no entry, rather than silently defaulting it. Config shape: `Sergin:Dispatch:Modules:dm=Local`, `Sergin:Dispatch:Modules:ua=Remote`.

`Remote` mode requires a configured gRPC channel per module, resolved through `Microsoft.Extensions.ServiceDiscovery` — this is where `AddServiceDefaults`'s currently-commented-out `AddServiceDiscovery()` / `http.AddStandardResilienceHandler()` / `http.AddServiceDiscovery()` get switched on (see the linked investigation's Cross-cutting section). `Local` mode requires the module's `.Application`/`.Domain`/`.Infrastructure` assemblies loaded and `AddServices` called, exactly as today.

**Composition root change.** `Program.cs` stops unconditionally doing `[new DeviceManagementModule(), new UserAccessModule()]` with every module fully wired. Per configured module mode:

- **Local** — register as today: `DbContext`, MediatR handler scanning, repositories, `MigrateAsync` runs at startup in Development.
- **Remote** — register only the module's `.Presentation.Grpc` client + its `IRemoteInvoker<,>` implementations + its `Schema`/`UiAssembly`/`NavItems`. No `DbContext`, no `.Application`/`.Infrastructure` assembly is loaded.

This is the concrete answer to the investigation's Q5/Q6: `<Module>Module` splits into a **Backend half** (`AddServices`, `MigrateAsync`, `ApplicationAssembly` — wired only under Local) and an always-present **Shell half** (`Schema`, `UiAssembly`, `NavItems`). `Schema` moves off the internal `DeviceManagementDbContext` const onto the composition root itself as a plain string constant, so it is readable without loading Infrastructure — required for Remote mode to satisfy the `@page` prefix guard without ever touching the DbContext.

## 5. Identity / permission propagation

`PermissionCheckPipelineBehavior` only runs inside the MediatR pipeline — Local only. For Remote, the same check (reflect `[RequiredPermissionsAttribute]` off the request type, evaluate against `IUserContext.HasPermission`) moves into `RoutingSerginUiDispatcher`, run before either branch, reusing the `IUserContext` already resolved per circuit. Local mode then checks twice — once in the dispatcher, once in the pipeline — a deliberate, cheap redundancy that keeps both paths honest rather than trusting Remote's ambient service to be the only enforcement point.

Stated plainly: this does not solve authentication. No token, claims principal, or session exists anywhere in this repo today, and this spec does not introduce one. `RoutingSerginUiDispatcher` attaches `UserId` and the resolved `Permissions` set to the gRPC call's metadata headers for the remote side to log or assert against — an audit aid, not a security boundary. Real cross-process trust is out of scope here and stays an open problem.

## 6. Error mapping

`Error` is `{Code, Description, Type}` — `ErrorType` maps 1:1 to a proto enum. The mapping runs both directions — client-side `IRemoteInvoker`s decode a reply's error (§2), server-side `<Aggregate>GrpcService`s encode one (§3) — so it is written once, in `Sergin.SharedKernel.Presentation.Grpc`, and referenced by every module's invokers and services rather than reimplemented per feature:

```csharp
public static class ErrorReplyExtensions
{
    public static ErrorOr<T> ToErrorOr<T>(this ErrorReply error) =>
        Error.Custom((int)error.Type.ToErrorType(), error.Code, error.Description);

    public static ErrorReply ToErrorReply(this Error error) =>
        new() { Code = error.Code, Description = error.Description, Type = error.Type.ToProtoErrorType() };
}
```

## 7. List-query fallout

Decisions 5/6 solve the discriminator, but force a real scope question into the open: Remote mode needs a feature-specific list-query proto message (`GetDeviceListRequest`, `GetUserListRequest`) per feature, where today there is no `GetUserListQueryCommand` C# type at all — `GetUserListQueryCommandHandler` implements `IListQueryHandler<GetUserListItem>` directly against the shared generic `ListQuery<GetUserListItem>`. For Local/Remote symmetry (the same request type must work down either branch of the router), this spec recommends introducing real per-feature list-query command types and retiring the shared generic `ListQuery<T>` handler pattern — the CQRS structural gap CLAUDE.md already names, forced open by this work rather than deferred further.

`Filtering`/`Sorting` — already dead plumbing per CLAUDE.md (`ListQueryRequestModel.ToListQuery<T>()` forwards `Term` but not these; no query repository reads them) — are dropped from the proto messages entirely rather than carried across a network boundary to nowhere.

> **Status, 2026-08-26 — done, with two changes.** Per-feature list-query types now exist for all three list features (`GetDeviceListQueryCommand`, `GetManufacturerListQueryCommand`, `GetUserListQueryCommand`), each carrying `[RequiredPermissions]`, and `IListQueryHandler<TQuery, TResponseData>` binds the handler to the concrete type. Two points of this section were decided differently in the event:
>
> 1. **`ListQuery<T>` was not retired — it was made `abstract` and kept as the base to derive from.** All three `ListQuery` types (the base and both generics) are abstract, which enforces the per-feature record more strongly than deleting the generic would have: there is now no dispatchable list-query type that is not a feature record. `ListQueryFactory` was deleted instead.
> 2. **`Filtering`/`Sorting` are carried on the C# record after all.** The WebApi endpoints pass all three of `Term`/`Filtering`/`Sorting` into the feature record, since `ToPaggination()` replaced `ToListQuery<T>()` and the endpoint composes the record itself. This does not settle the proto question — no list rpc exists yet — but whoever writes `GetDeviceListRequest` should know the fields are populated on the request object, and decide deliberately whether the wire carries them. No query repository reads any of the three, so this section's reasoning for dropping them still stands.

## 8. Testing

`CreateAndGetUserTests` resolves `ISerginUiDispatcher` from `factory.Services` and sends `CreateUserCommand` today; this keeps working unchanged as long as the test host configures UserAccess as `Local` in `DispatchModeOptions` — same real in-process round trip through Postgres the existing CLAUDE.md guidance calls for. Remote-mode coverage (a gRPC server via Testcontainers or an in-memory channel) is new work, not addressed here — flagged as follow-up, not silently assumed to be free.

## Open follow-ups (explicitly out of scope for this spec)

- Real authentication/trust boundary for Remote mode (§5).
- ~~Retiring `ListQuery<T>` and introducing per-feature list-query types (§7) — a CQRS-layer change, not purely a dispatch-contract one; likely its own spec.~~ **Done 2026-08-26** (no separate spec — went straight to implementation). The per-feature types exist; `ListQuery<T>` was made abstract rather than retired. See the status note in §7.
- Remote-mode integration test infrastructure (§8).
- Which repo builds and ships each module's service image once UserAccess (embed-only, no standalone `.slnx`) needs to run as its own Remote-mode process — flagged in the investigation's Cross-cutting section, unresolved here.
- Identity-metadata propagation (§5's stated attachment of `UserId`/`Permissions` to the gRPC call's metadata headers) was not implemented in this reference-slice pass: the shipped `RoutingSerginUiDispatcher` resolves `IUserContext` for its own Local/Remote permission gate only, and never forwards that identity to the Remote branch — `invoker.InvokeAsync(request, ct)` carries no identity parameter today. Closing this gap requires widening `IRemoteInvoker<TRequest,TResponse>`'s public signature, which would ripple into every existing implementer (`GetDeviceByIdGrpcInvoker`/`DeviceGrpcService`); flagged here rather than done silently.
