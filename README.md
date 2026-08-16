# Sergin Meter Minder

A .NET 10 **modular monolith** platform, built with Domain-Driven Design (DDD), Clean Architecture, and per-feature vertical slices. PostgreSQL is the storage; Docker Compose orchestrates locally, with .NET Aspire providing service defaults (OpenTelemetry, health checks) and an observability dashboard.

The central component is the **MeterMinder** module — a Head-End System (HES) for smart electricity/gas/water meters, the primary entry point for IoT device communication, data processing, and integration with other subsystems — alongside a **UserAccess** module for identity and access concerns. Both are composed into two runnable hosts: a **Web API** and a **Blazor Server UI**.

This repo (`Sergin.MeterMinder`) is the root/hostable repo of a three-repo split — **`src/SharedKernel/`** and **`src/Modules/UserAccess/`** are git submodules pointing at their own repos, [Sergin.SharedKernel](https://github.com/poursh/Sergin.SharedKernel) and [Sergin.UserAccess](https://github.com/poursh/Sergin.UserAccess). See "Getting Started" below for the clone step this requires.

## 🏗 Architectural Approach

The solution follows modern architecture practices to keep domain logic clear and the system maintainable and scalable:

- **Domain-Driven Design (DDD)** – Rich domain model with aggregates, strongly-typed IDs, domain events, and clear boundaries.
- **Clean Architecture** – Strict dependency direction across `Domain → Application → Infrastructure / Presentation`.
- **Modular Monolith** – Independent, self-contained modules (`MeterMinder`, `UserAccess`) that can later be decomposed into services.
- **CQRS** – Writes flow through MediatR commands to EF Core repositories; reads use dedicated query repositories backed by raw SQL for performance.

## 🧱 Solution Structure

```
.
├── src/
│   ├── Hosts/
│   │   ├── Sergin.MeterMinder.Hosts.WebApi.All/  # Runnable all-in-one Web API (composition root)
│   │   └── Sergin.MeterMinder.Hosts.WebUi.All/   # Runnable all-in-one Blazor Server UI (Development only)
│   ├── Modules/
│   │   ├── MeterMinder/                          # Head-End System (HES) for smart meters
│   │   └── UserAccess/                           # Identity & access module (git submodule)
│   └── SharedKernel/                             # Framework-level building blocks (git submodule)
│       ├── Sergin.SharedKernel.Hosts             # Aspire service defaults + AddSerginCore
│       ├── Sergin.SharedKernel.Hosts.WebApi      # Sergin WebApi bootstrap (OpenAPI, endpoints)
│       ├── Sergin.SharedKernel.Hosts.WebUi       # Sergin Blazor bootstrap (Razor components, dev user)
│       └── ...                                   # Other framework-level building blocks
├── tests/
│   ├── Sergin.MeterMinder.IntegrationTests.WebApi.All/  # xUnit + Testcontainers, exercises the real API host
│   └── Sergin.MeterMinder.IntegrationTests.WebUi.All/   # ... and the real UI host (server-side page rendering)
└── docker-compose/                               # API + UI + postgres:17 + Aspire dashboard
```

Each module is split into `.Domain`, `.Application`, `.Infrastructure`, `.Infrastructure.Data` (DbContext + migrations), `.Presentation.WebApi` (minimal-API endpoints), and optionally `.Presentation.Blazor` (a Razor Class Library of MudBlazor pages), plus a composition project that wires it into the hosts. Each module owns its own `DbContext`, migrations, and PostgreSQL schema.

## 📌 Key Features

- **MeterMinder**, a Head-End System (HES) for smart meter device and data management, plus a **UserAccess** module for users and permissions.
- Clean separation between domain, application, and infrastructure layers, enforced by project dependencies.
- CQRS with MediatR pipeline behaviors for permission checks and validation.
- Domain-event infrastructure on `AggregateRoot`, dispatched on `SaveChanges` via an EF Core interceptor (wired and ready; no aggregate raises an event yet).
- A Blazor Server UI composed from the same modules, each contributing its own pages and nav entries.
- Extensible design for adding future modules with minimal coupling.

## 🛠 Technologies & Libraries

- **.NET 10** – Core development framework
- **.NET Aspire** – Service defaults (OpenTelemetry, health checks, resilience) plus the observability dashboard (via the `aspire-dashboard` container in Docker Compose). There is no Aspire AppHost; Docker Compose does the orchestration.
- **Blazor Server + MudBlazor** – Interactive server-rendered UI, one Razor Class Library per module
- **Entity Framework Core** – ORM for the write side, migrations, and value converters
- **Dapper / raw SQL** – High-performance read-side query repositories via `IDbConnectionFactory`
- **PostgreSQL** – Relational database backend (per-module schemas)
- **MediatR** – In-process messaging for CQRS and decoupled communication
- **FluentValidation** – Strongly-typed, fluent request validation
- **ErrorOr** – Result/error modeling for handlers, mapped to ProblemDetails at the API edge

## 🚀 Getting Started

Requires the **.NET 10 SDK** (VS 17.13+ / Rider). Run all commands from the repo root.

```bash
# Clone with submodules (SharedKernel + UserAccess live in their own repos)
git clone --recurse-submodules https://github.com/poursh/Sergin.MeterMinder.git

# ...or, for an existing clone that didn't use --recurse-submodules:
git submodule update --init --recursive

# Build (warnings are treated as errors — analyzers + SonarAnalyzer enforced)
dotnet build Sergin.MeterMinder.slnx
```

### Run the API

```bash
# Directly on the host — the Development profile applies EF migrations on startup.
# Needs a Sergin:ConnectionStrings:Database connection string, e.g. as a user secret
# (the host declares a UserSecretsId) pointing at a Postgres instance you have running.
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All
# → http://localhost:5000, Scalar UI at /scalar/v1

# ...or run everything in Docker (API + UI + postgres:17 + Aspire dashboard) — no secrets
# needed, the connection string is set via environment variable in docker-compose.yml.
# NB: submodules must be initialized first (above) — the Docker build context
# copies the whole working tree, submodule content included.
docker compose -f docker-compose/docker-compose.yml up --build
# → API at http://localhost:5000, UI at http://localhost:5002, Aspire dashboard at http://localhost:18888
```

### Run the Blazor UI

```bash
# Same connection-string requirement as the API. Landing page is /mm/devices.
dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.WebUi.All
# → http://localhost:5002
```

> **The UI host is Development-only.** It has no authentication yet — every request runs as the single
> user configured under `Sergin:DevUser` in its `appsettings.json` — so it deliberately throws at startup
> in any other environment rather than serving unauthenticated pages.

### Ports

| Port | Service |
|---|---|
| 5000 / 5001 | Web API host (http / https) |
| 5002 / 5003 | Blazor UI host (http / https) |
| 5432 | PostgreSQL |
| 18888 | Aspire dashboard |
| 4317 | OTLP telemetry ingest |

### Run from Visual Studio

If you use **Visual Studio** (17.13+), open `Sergin.MeterMinder.slnx`, set **`docker-compose`**
(`docker-compose/docker-compose.dcproj`) as the startup project, and press **F5**.
Visual Studio builds the images and launches the full stack (API + UI + `postgres:17` +
Aspire dashboard) via Docker Compose, then attaches the debugger to the API.

### EF Core migrations

Each module owns its own `DbContext` and migrations. Example for the MeterMinder module:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/MeterMinder/Sergin.MeterMinder.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.WebApi.All
```

Migrations are applied automatically at startup **only in the Development environment**.

### Run the integration tests

```bash
# Need Docker — each spins up a real postgres:17 via Testcontainers
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebApi.All/Sergin.MeterMinder.IntegrationTests.WebApi.All.csproj
dotnet test tests/Sergin.MeterMinder.IntegrationTests.WebUi.All/Sergin.MeterMinder.IntegrationTests.WebUi.All.csproj
```

> **Note:** `Directory.Build.props` enables `TreatWarningsAsErrors`, `AnalysisMode=All`, and SonarAnalyzer with `EnforceCodeStyleInBuild`. Any analyzer, style, or nullable warning will fail the build.

## 📄 License

[MIT](LICENSE) © Pejman Pourshirazi. `SharedKernel` and `UserAccess` are separate repos, each under their own MIT license.

---

See [`.claude/CLAUDE.md`](.claude/CLAUDE.md) for the full architecture and workflow reference used by Claude Code — equally useful as a deeper-dive for human contributors.
