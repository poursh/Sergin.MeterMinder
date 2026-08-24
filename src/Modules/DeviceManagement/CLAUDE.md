# DeviceManagement module

Schema `dm`. The Head-End System (HES) module — device communication and data collection for smart electricity/gas/water meters.

See the root `.claude/CLAUDE.md` for cross-module conventions (layering, CQRS split, permissions, etc.) — this file only covers what's specific to the `Devices`/`DeviceModels` aggregates.

## `Devices` aggregate

`Sergin.MeterMinder.DeviceManagement.Domain/Devices/Device.cs` — `AggregateRoot<DeviceIntenralId>` (note the misspelling — it's the real type name, match it). `DeviceId` is the business-facing string key; `DeviceIntenralId` is the internal `Guid` PK. `Device` also carries a mandatory `ManufacturerId` FK (see `Manufacturers` aggregate below) — set via `Device.Create(DeviceId, ManufacturerId)`.

**`DeviceModel` is an unfinished, dangling piece**: `Sergin.MeterMinder.DeviceManagement.Domain/DeviceModels/DeviceModel.cs` defines a `DeviceModel` aggregate, and `Device.cs` has a commented-out `ModelId`/`Model` relationship and a commented-out `Create(DeviceModelInternalId)` factory overload. Neither is wired into anything — no repository, no Application slice, no endpoint, no EF configuration. Don't build a new feature on top of this relationship without checking with the user first; if you need device-model data, treat `DeviceModel` as a bare aggregate stub, not an established pattern. This is unrelated to the (fully wired) `Manufacturers` relationship below.

Implemented feature slices (`Devices/Commands/<Feature>/` in Application, mirrored in Infrastructure/Presentation; the command/query request and response records for this aggregate live in `Sergin.MeterMinder.DeviceManagement.Application.Contracts`, not `.Application`):

| Feature | Kind | Route | Permission |
|---|---|---|---|
| `Create` | command | `POST /dm/devices` | none |
| `GetOne` | query | `GET /dm/devices/{deviceId:guid}` | `permission.dm.devices.read` |
| `GetList` | query | `GET /dm/devices` (`[AsParameters] ListQueryRequestModel`) | none |

`GetOne` also has a second transport, alongside its WebApi endpoint above: `Sergin.MeterMinder.DeviceManagement.Presentation.Grpc.Client` (`GetDeviceByIdGrpcInvoker`) and `.Server` (`DeviceGrpcService`), both compiling against `.Contracts`' generated proto code, implement the same `GetDeviceByIdQueryCommand` over gRPC — the one real proof slice for the platform's dual-mode (MediatR/gRPC) dispatch mechanism documented in the root `CLAUDE.md` under "Host / module composition". Both still end in `ISender.Send(GetDeviceByIdQueryCommand)` — the WebApi side directly via `ISender.Send`, the gRPC side via the same call inside `DeviceGrpcService` — no wrapper on either side — only the transport in front differs. **No `ISerginRemoteModule` implementation exists for this module today.** `DeviceManagementRemoteModule`/`AddDeviceManagementRemoteServices` (`DeviceManagementRemoteServicesExtensions`) used to live in the `Client` project, wiring a `RemoteForwardingHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>` bound to `GetDeviceByIdGrpcInvoker`'s `IRemoteInvoker<,>` so a host passing `[new DeviceManagementRemoteModule()]` as `AddSerginCore`'s `remoteModules` (instead of `DeviceManagementModule` in `localModules`) could dispatch this command through gRPC — that class pair was deliberately removed for now, pending a more generalized way to wire a module's `AddRemoteServices` (see the root `CLAUDE.md`'s Overview). `RemoteForwardingHandler<TRequest, TResponse>` itself now lives in `Sergin.SharedKernel.Presentation.Grpc.Dispatching`, alongside `IRemoteInvoker<,>`. **Live-but-unhosted**: nothing maps `DeviceGrpcService` into a running host today, and the real host's `Program.cs` passes `DeviceManagementModule` as a `localModules` entry — so `GetDeviceByIdGrpcInvoker`/`DeviceGrpcService` are exercised only by `DeviceGrpcRoundTripTests` in the outer test project, which hosts `DeviceGrpcService` on its own loopback Kestrel server and wires `RemoteForwardingHandler` manually rather than through a module's `AddRemoteServices`.

## `Manufacturers` aggregate

`Sergin.MeterMinder.DeviceManagement.Domain/Manufacturers/Manufacturer.cs` — `AggregateRoot<ManufacturerId>`, private ctor + `static Create(ManufacturerName, ManufacturerAddress?)` factory. `Name` is mandatory, `Address` is optional (nullable value object, nullable `ManufacturerAddressConverter`). `Device.ManufacturerId` is a required FK to this aggregate (`dm.device.manufacturer_id` → `dm.manufacturer.id`, configured via `HasOne<Manufacturer>().WithMany()` in `DeviceEntityTypeConfiguration` — no navigation property either direction, matching the rest of this module's style).

Implemented feature slices (`Manufacturers/Commands/<Feature>/` in Application, mirrored in Infrastructure/Presentation; the command/query request and response records for this aggregate also live in `Sergin.MeterMinder.DeviceManagement.Application.Contracts`, not `.Application`):

| Feature | Kind | Route | Permission |
|---|---|---|---|
| `Create` | command | `POST /dm/manufacturers` | none |
| `GetOne` | query | `GET /dm/manufacturers/{manufacturerId:guid}` | `permission.dm.manufacturers.read` |
| `GetList` | query | `GET /dm/manufacturers` (`[AsParameters] ListQueryRequestModel`) | none |

## Repositories

- `IDeviceRepository` (`Domain/Devices/`) extends the generic `IRepository<Device, DeviceIntenralId>` with one extra method, `GetByDeviceId(DeviceId)` — a precedent for adding aggregate-specific lookups to the repository interface when the generic CRUD isn't enough, rather than reaching into EF from the Application layer.
- Query repositories follow the same one-interface-per-feature split as UserAccess (`IGetDeviceQueryRepository`, `IGetDeviceListQueryRepository`, `IDeviceAllQueryRepositoriy` — note the existing typo in that last name, match it), all implemented by a single `DeviceQueryRepository` class. The list-query interface lives in `IGetDeviceListQueryRepository.cs` — file and type names now match (a prior mismatch between them was fixed).
- `IManufacturerRepository` (`Domain/Manufacturers/`) — plain `IRepository<Manufacturer, ManufacturerId>`, no custom methods. Query repositories follow the same split (`IGetManufacturerQueryRepository`, `IGetManufacturerListQueryRepository`, `IManufacturerAllQueryRepository` — correctly spelled this time, don't propagate the `Devices` typo here), all implemented by `ManufacturerQueryRepository`. `ManufacturerAddressConverter` is the reference example for a **nullable** value-object EF converter (see the value-converter template in root `CLAUDE.md`) — copy its null-ternary shape for any new optional value object, not the non-nullable `UserNameConverter`/`DeviceIdConverter` shape.
