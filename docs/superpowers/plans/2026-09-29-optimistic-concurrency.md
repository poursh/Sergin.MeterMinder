# Optimistic Concurrency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refuse a write made against a version of a DeviceManagement aggregate the caller has not seen, with the version carried beside the request instead of on command or response records.

**Architecture:** A scoped `ConcurrencyContext` (SharedKernel.Application) holds the version a caller expects and the version a send produced. Front ends fill and read it: the Blazor dispatcher, a WebApi endpoint filter (`If-Match`/`ETag`) and gRPC interceptors. A `row_version uuid` shadow column, added by the aggregate-feature convention through `builder.Versioned()`, is checked by one interceptor (first in the chain) and rewritten by another (last). A pipeline behavior enforces `[RequiresExpectedVersion]` and turns a concurrency conflict into a 412 error.

**Tech Stack:** .NET 10, EF Core + Npgsql, MediatR, ErrorOr, Dapper, Blazor Server + MudBlazor, Grpc.AspNetCore, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-09-29-optimistic-concurrency-design.md`

## Global Constraints

- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: any analyzer warning fails the build. Nullable enabled.
- Central Package Management: no `Version` attribute on a `PackageReference`.
- New SharedKernel code lives in the `src/SharedKernel` submodule and is committed there, on its own branch. Host code, migrations and tests are committed in the host repo.
- Never add a `Co-Authored-By: Claude` trailer to any commit (root CLAUDE.md).
- File-scoped namespaces everywhere (IDE0161), including the hand-converted migration.
- `.razor` files hold markup only; all C# goes in `.razor.cs`.
- IDs and versions come from `Guid.CreateVersion7()` (`RowVersion.Create()`), never `Guid.NewGuid()`, except the migration backfill, which uses Postgres `gen_random_uuid()`.
- Error codes: `General.VersionRequired` (type 428), `General.VersionStale` (type 412).
- Column `row_version`, shadow property `RowVersion`, annotations `Sergin:Versioned` and `Sergin:VersionRoot`.
- gRPC metadata keys: `sergin-expected-version` (request header), `sergin-version` (response trailer).
- Integration tests need Docker running (Docker Desktop is often down at session start: launch it and poll `docker info`).

## Review Focus

1. A version sent with a command that changes nothing: the send succeeds, no 412. Pinned in Task 3 (`ExpectedVersion_WithNoChange_Succeeds`).
2. Delete of an already-deleted record sent with some version: answers not-found, not 412 or 428. Pinned in Task 6 (`DeleteDevice_AlreadyDeleted_IsNotFound`).
3. A version read from manufacturer A sent with a command against manufacturer B: answers 412. Pinned in Task 6 (`AddDeviceModel_WithAnotherManufacturersVersion_IsStale`).
4. A create (no version sent) still returns the new record's first version. Pinned in Task 3 (`Insert_SetsAVersion_AndPublishesIt`).
5. One save touching two versioned roots while a version is set: a programming error with both roots named, not a silent check of one. Pinned in Task 3 (`ExpectedVersion_WithTwoRootsTouched_Throws`).

---

### Task 0: Worktree and branches

**Files:** none.

- [ ] **Step 1: Create the host worktree and branch**

Run from the repo root:
```bash
git worktree add .claude/worktrees/optimistic-concurrency -b feat/optimistic-concurrency
cd .claude/worktrees/optimistic-concurrency
git submodule update --init --recursive
git -C src/SharedKernel checkout -b feat/optimistic-concurrency
```

- [ ] **Step 2: Verify a clean build**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: `Build succeeded.` with 0 warnings.

All later paths are relative to the worktree root.

---

### Task 1: Concurrency carrier, attribute, errors, pipeline behavior

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/ConcurrencyContext.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/RequiresExpectedVersionAttribute.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/VersionErrors.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/ConcurrencyConflictException.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/Versioned.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/ExpectedVersionPipelineBehavior.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/ErrorOrResponse.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/PermissionCheckPipelineBehavior.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (the `AddMediatR` block and the `UserContextAccessor` registration)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation/Errors/SerginProblemFactory.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Protos/error.proto`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/ExpectedVersionPipelineTests.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/VersionProblemRenderingTests.cs`

**Interfaces:**
- Produces:
  - `Sergin.SharedKernel.Application.Concurrency.ConcurrencyContext { RowVersion? Expected { get; set; } RowVersion? Current { get; set; } }`, registered scoped.
  - `RequiresExpectedVersionAttribute` (class attribute, no members).
  - `VersionErrors.RequiredType = 428`, `VersionErrors.StaleType = 412`, `Error VersionErrors.Required`, `Error VersionErrors.Stale`, `bool VersionErrors.IsStale(Error)`.
  - `ConcurrencyConflictException : Exception` (three standard constructors).
  - `Versioned<T>(T Value, RowVersion Version)` sealed record.
  - internal `ErrorOrResponse.TryFrom<TResponse>(Error, out TResponse)`.

- [ ] **Step 1: Write the failing pipeline tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/ExpectedVersionPipelineTests.cs`:
```csharp
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// ExpectedVersionPipelineBehavior on its own, over a bare MediatR container with test-only commands: a marked
/// command without a version is refused before its handler runs, a conflict thrown by the save becomes the
/// stale-version error, and a version sent with an unmarked command is still honoured. No database.
/// </summary>
public sealed class ExpectedVersionPipelineTests
{
    [Fact]
    public async Task MarkedCommand_WithoutVersion_IsRefusedWith428_AndTheHandlerDoesNotRun()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        HandlerCalls calls = scope.ServiceProvider.GetRequiredService<HandlerCalls>();

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GuardedCommand(Conflict: false));

        Assert.Equal(VersionErrors.RequiredType, (int)result.FirstError.Type);
        Assert.Equal("General.VersionRequired", result.FirstError.Code);
        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public async Task MarkedCommand_WithVersion_ReachesItsHandler()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Expected = RowVersion.Create();

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GuardedCommand(Conflict: false));

        Assert.False(result.IsError);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task ConflictThrownByTheSave_BecomesTheStaleError()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Expected = RowVersion.Create();

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GuardedCommand(Conflict: true));

        Assert.True(VersionErrors.IsStale(result.FirstError));
        Assert.Equal("General.VersionStale", result.FirstError.Code);
    }

    [Fact]
    public async Task UnmarkedCommand_WithoutVersion_ReachesItsHandler()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new UnguardedCommand(Conflict: false));

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task UnmarkedCommand_WithVersion_IsStillChecked()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Expected = RowVersion.Create();

        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new UnguardedCommand(Conflict: true));

        Assert.True(VersionErrors.IsStale(result.FirstError));
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();

        services.AddScoped<ConcurrencyContext>();
        services.AddScoped<HandlerCalls>();

        // Pointed at the DM contracts assembly only to give AddMediatR something to scan: it holds records,
        // no handlers, so the explicit registrations below are the only ones.
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblyContaining<GetDeviceByIdQueryCommand>();
            options.AddOpenBehavior(typeof(ExpectedVersionPipelineBehavior<,>));
        });

        services.AddTransient<IRequestHandler<GuardedCommand, ErrorOr<Success>>, GuardedHandler>();
        services.AddTransient<IRequestHandler<UnguardedCommand, ErrorOr<Success>>, UnguardedHandler>();

        return services.BuildServiceProvider();
    }

    [RequiresExpectedVersion]
    internal sealed record GuardedCommand(bool Conflict) : ICommand<Success>;

    internal sealed record UnguardedCommand(bool Conflict) : ICommand<Success>;

    internal sealed class HandlerCalls
    {
        public int Count { get; set; }
    }

    internal sealed class GuardedHandler(HandlerCalls calls) : IRequestHandler<GuardedCommand, ErrorOr<Success>>
    {
        public Task<ErrorOr<Success>> Handle(GuardedCommand request, CancellationToken cancellationToken)
        {
            calls.Count++;

            return request.Conflict
                ? throw new ConcurrencyConflictException("Simulated conflict.")
                : Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    internal sealed class UnguardedHandler : IRequestHandler<UnguardedCommand, ErrorOr<Success>>
    {
        public Task<ErrorOr<Success>> Handle(UnguardedCommand request, CancellationToken cancellationToken) =>
            request.Conflict
                ? throw new ConcurrencyConflictException("Simulated conflict.")
                : Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}
```

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/VersionProblemRenderingTests.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>Both version errors render with their HTTP status and their code's title and detail.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class VersionProblemRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Required_RendersAs428()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Required, Localizer());

        Assert.Equal(StatusCodes.Status428PreconditionRequired, problem.StatusCode);
        Assert.Equal(Localizer()["General.VersionRequired.title"].Value, problem.Title);
        Assert.Equal(Localizer()["General.VersionRequired"].Value, problem.Detail);
    }

    [Fact]
    public void Stale_RendersAs412()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Stale, Localizer());

        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
        Assert.Equal(Localizer()["General.VersionStale.title"].Value, problem.Title);
        Assert.Equal(Localizer()["General.VersionStale"].Value, problem.Detail);
    }

    private ILocalizer Localizer() => factory.Services.GetRequiredService<ILocalizer>();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~Concurrency"`
Expected: build FAILS with `CS0246` on `ConcurrencyContext`, `RequiresExpectedVersionAttribute`, `VersionErrors`, `ConcurrencyConflictException`, `ExpectedVersionPipelineBehavior`.

- [ ] **Step 3: Add the carrier types**

`Concurrency/ConcurrencyContext.cs`:
```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>
/// Carries a request's aggregate version beside the request, never on it. A front end sets
/// <see cref="Expected"/> before the send (the Blazor dispatcher, the WebApi If-Match filter, the gRPC server
/// interceptor) and reads <see cref="Current"/> after it. During the send, a GetOne handler sets
/// <see cref="Current"/> from the row it read, and the row-version interceptor sets it to the version it wrote.
/// <para>
/// Registered scoped, next to <see cref="Securities.Users.UserContextAccessor"/> and for the same reason: each
/// Blazor send and each gRPC call runs in its own scope, so a value never crosses requests.
/// </para>
/// </summary>
public sealed class ConcurrencyContext
{
    /// <summary>The version the caller last saw. Null means the caller sent none.</summary>
    public RowVersion? Expected { get; set; }

    /// <summary>The aggregate's version after the send: read by a GetOne, or written by a save.</summary>
    public RowVersion? Current { get; set; }
}
```

`Concurrency/RequiresExpectedVersionAttribute.cs`:
```csharp
namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>
/// Marks a command that must carry an expected version in <see cref="ConcurrencyContext.Expected"/>. Without
/// one, ExpectedVersionPipelineBehavior answers <see cref="VersionErrors.Required"/> before the handler runs.
/// A version sent with an unmarked command is still checked: the attribute makes it required, not possible.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresExpectedVersionAttribute : Attribute;
```

`Concurrency/VersionErrors.cs`:
```csharp
namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>
/// The two version errors. Custom types numbered as their HTTP statuses, so SerginProblemFactory renders the
/// right status with no WebApi special case, and a page recognises a stale version by type.
/// </summary>
public static class VersionErrors
{
    public const int RequiredType = 428;
    public const int StaleType = 412;

    public static Error Required { get; } = Error.Custom(
        RequiredType,
        "General.VersionRequired",
        "This change must say which version of the record it was made against.");

    public static Error Stale { get; } = Error.Custom(
        StaleType,
        "General.VersionStale",
        "The record changed after it was loaded. Reload it and try again.");

    public static bool IsStale(Error error) => (int)error.Type == StaleType;
}
```

`Concurrency/ConcurrencyConflictException.cs`:
```csharp
namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>
/// A save found the aggregate's row at another version than the one expected. Thrown by SerginDbContext in
/// place of EF's DbUpdateConcurrencyException, so the Application layer can catch it without referencing EF.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

`Concurrency/Versioned.cs`:
```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>A read model together with the version of the aggregate row it was read from.</summary>
public sealed record Versioned<T>(T Value, RowVersion Version);
```

- [ ] **Step 4: Extract `ErrorOrResponse` and use it in the permission behavior**

`src/SharedKernel/Sergin.SharedKernel.Application/ErrorOrResponse.cs`:
```csharp
using System.Reflection;

namespace Sergin.SharedKernel.Application;

/// <summary>
/// Builds a pipeline behavior's TResponse from one <see cref="Error"/>, for the behaviors that short-circuit a
/// request: TResponse is only known as a type parameter, so an ErrorOr&lt;T&gt; is built through reflection.
/// </summary>
internal static class ErrorOrResponse
{
    public static bool TryFrom<TResponse>(Error error, out TResponse response)
    {
        if (typeof(TResponse) == typeof(IErrorOr))
        {
            response = (TResponse)(object)error;
            return true;
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(ErrorOr<>))
        {
            Type resultType = typeof(TResponse).GetGenericArguments()[0];

            MethodInfo fromMethod = typeof(ErrorOr<>)
                .MakeGenericType(resultType)
                .GetMethod(nameof(ErrorOr<object>.From))!;

            response = (TResponse)fromMethod.Invoke(null, [new List<Error>([error])])!;
            return true;
        }

        response = default!;
        return false;
    }
}
```

Replace the body of `PermissionCheckPipelineBehavior.Handle` (keep the class declaration; drop the now-unused `using System.Reflection;`):
```csharp
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        RequiredPermissionsAttribute? att = request.GetType().GetCustomAttribute<RequiredPermissionsAttribute>();

        if (att is null || userContext.HasPermission(att.Permissionas))
        {
            return await next(cancellationToken);
        }

        if (ErrorOrResponse.TryFrom(Error.Forbidden(), out TResponse forbidden))
        {
            return forbidden;
        }

        throw new ForbiddenException();
    }
```
`GetCustomAttribute<T>` is an extension in `System.Reflection`: keep that `using` if the compiler asks for it (CS1061); remove it only if IDE0005 reports it unused.

- [ ] **Step 5: Add the pipeline behavior**

`Concurrency/ExpectedVersionPipelineBehavior.cs`:
```csharp
using System.Reflection;
using MediatR;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.SharedKernel.Application.Concurrency;

/// <summary>
/// Runs between the permission check and validation. Refuses a command marked
/// <see cref="RequiresExpectedVersionAttribute"/> that arrives without an expected version, and turns the
/// <see cref="ConcurrencyConflictException"/> a save throws on a version mismatch into
/// <see cref="VersionErrors.Stale"/>. The check itself is the database's: the row-version interceptor puts the
/// expected version into the UPDATE's WHERE clause.
/// </summary>
internal sealed class ExpectedVersionPipelineBehavior<TRequest, TResponse>(ConcurrencyContext concurrency)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (concurrency.Expected is null
            && request.GetType().GetCustomAttribute<RequiresExpectedVersionAttribute>() is not null)
        {
            return ErrorOrResponse.TryFrom(VersionErrors.Required, out TResponse required)
                ? required
                : throw new InvalidOperationException(
                    $"{request.GetType().FullName} requires an expected version but does not answer ErrorOr, so the refusal cannot be returned.");
        }

        try
        {
            return await next(cancellationToken);
        }
        catch (ConcurrencyConflictException) when (ErrorOrResponse.TryFrom(VersionErrors.Stale, out TResponse stale))
        {
            return stale;
        }
    }
}
```
If the compiler refuses `stale` inside the catch block (CS0165), assign in the filter to a local declared before `try`:
```csharp
        TResponse stale = default!;

        try
        {
            return await next(cancellationToken);
        }
        catch (ConcurrencyConflictException) when (ErrorOrResponse.TryFrom(VersionErrors.Stale, out stale))
        {
            return stale;
        }
```

- [ ] **Step 6: Register the context and the behavior**

In `SerginCoreExtensions.AddSerginCore`, change the behavior registrations inside `AddMediatR` to:
```csharp
            options.AddOpenBehavior(typeof(PermissionCheckPipelineBehavior<,>));
            options.AddOpenBehavior(typeof(ExpectedVersionPipelineBehavior<,>));
            options.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
```
and directly after `builder.Services.AddScoped<UserContextAccessor>();` add:
```csharp
        // Seeded by each front end before a send and read back after it; see ConcurrencyContext.
        builder.Services.AddScoped<ConcurrencyContext>();
```
Add `using Sergin.SharedKernel.Application.Concurrency;`.

- [ ] **Step 7: Render both errors**

In `SerginProblemFactory`, add `using Sergin.SharedKernel.Application.Concurrency;` and one arm in each switch, before the `_` arm:
```csharp
            (ErrorType)VersionErrors.RequiredType => StatusCodes.Status428PreconditionRequired,
            (ErrorType)VersionErrors.StaleType => StatusCodes.Status412PreconditionFailed,
```
```csharp
            (ErrorType)VersionErrors.RequiredType => localizer[$"{error.Code}.title"],
            (ErrorType)VersionErrors.StaleType => localizer[$"{error.Code}.title"],
```
```csharp
            (ErrorType)VersionErrors.RequiredType => localizer[error.Code],
            (ErrorType)VersionErrors.StaleType => localizer[error.Code],
```
There are no resource files: `DefaultLocalizer` answers the key itself, the same as for every other code.

In `error.proto`, add to `ProtoErrorType` after `FORBIDDEN = 6;`, and extend the comment above the enum with one line saying these two match `VersionErrors`' custom types:
```proto
  PRECONDITION_FAILED = 412;
  PRECONDITION_REQUIRED = 428;
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~Concurrency|FullyQualifiedName~DispatcherUserContextTests|FullyQualifiedName~DeviceGrpcRoundTripTests"`
Expected: all PASS (the last two prove the permission refactor kept Forbidden).

- [ ] **Step 9: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Add ConcurrencyContext, version errors and the expected-version pipeline behavior"
git add tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency src/SharedKernel
git commit -m "Test the expected-version pipeline behavior"
```

---

### Task 2: `Versioned()` and the model shape

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatures.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureBuilder.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureRegistry.cs` (`ForChild`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/RowVersionColumns.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AggregateFeatureConvention.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureRegistryTests.cs:28-30`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/TestVersionedDbContext.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/RowVersionShapeTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `AggregateFeatures(bool Audited, bool SoftDeletable, bool Versioned)`; `AggregateFeatures.None` all false.
  - `AggregateFeatureBuilder<TRoot>.Versioned()` returning the builder.
  - `RowVersionColumns`: `VersionedAnnotation = "Sergin:Versioned"`, `VersionRootAnnotation = "Sergin:VersionRoot"`, `RowVersion = "RowVersion"`, `RowVersionColumn = "row_version"`, `bool IsVersioned(IReadOnlyEntityType)`, `string? RootOf(IReadOnlyEntityType)` (the root entity type's `Name`).
  - Test types `TestVersionedDbContext` (schema `test_versioned`), `Pallet` (root: `Code`, `Bays`, `Rename(string)`, `AddBay(string)`, `Nudge(Guid neighbourId)`), `Bay` (child), `PalletNudged` event.

- [ ] **Step 1: Write the test module**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/TestVersionedDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// A test-only module with one versioned aggregate: <see cref="Pallet"/> with a child entity in its own table
/// (<see cref="Bay"/>, like DeviceModel). Audited and soft-deletable too, so the row-version interceptors run
/// alongside the real audit and soft-delete ones. <see cref="Pallet.Nudge"/> raises an event whose handler
/// renames another pallet, for the "bumped but not checked" case. Mapped into its own test_versioned schema
/// through the real AddModuleDbContext.
/// </summary>
internal interface ITestVersionedDbContext : IDbContext;

internal interface ITestVersionedUnitOfWork : IUnitOfWork;

internal sealed class TestVersionedDbContext(DbContextOptions<TestVersionedDbContext> options)
    : SerginDbContext(options), ITestVersionedDbContext, ITestVersionedUnitOfWork
{
    public const string Schema = "test_versioned";

    public DbSet<Pallet> Pallets => Set<Pallet>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(PalletAggregateFeatureConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Pallet>(pallet =>
        {
            pallet.ToTable("pallets");
            pallet.HasKey(x => x.Id);
            pallet.Property(x => x.Code);
            pallet.Ignore(x => x.DomainEvents);

            pallet.HasMany(x => x.Bays)
                .WithOne()
                .HasForeignKey(bay => bay.PalletId)
                .OnDelete(DeleteBehavior.Cascade);
            pallet.Navigation(x => x.Bays).HasField("bays").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Bay>(bay =>
        {
            bay.ToTable("bays");
            bay.HasKey(x => x.Id);
            bay.Property(x => x.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class Pallet : AggregateRoot<Guid>
{
    private readonly List<Bay> bays = [];

    private Pallet()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyCollection<Bay> Bays => bays;

    public static Pallet Create(string code) => new() { Id = Guid.CreateVersion7(), Code = code };

    public void Rename(string code) => Code = code;

    public void AddBay(string name) => bays.Add(Bay.Create(Id, name));

    public void Nudge(Guid neighbourId) =>
        Raise(new PalletNudged(Guid.CreateVersion7(), DateTime.UtcNow, neighbourId));
}

internal sealed class Bay : Entity<Guid>
{
    private Bay()
    {
    }

    public Guid PalletId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    internal static Bay Create(Guid palletId, string name) =>
        new() { Id = Guid.CreateVersion7(), PalletId = palletId, Name = name };
}

internal sealed record PalletNudged(Guid Id, DateTime OccurredOnUtc, Guid NeighbourId) : IDomainEvent;

internal sealed class PalletAggregateFeatureConfiguration : IAggregateFeatureConfiguration<Pallet>
{
    public void Configure(AggregateFeatureBuilder<Pallet> builder) => builder.Audited().SoftDeletable().Versioned();
}
```
Check `TestEventsDbContext` (`tests/.../Events/TestEventsDbContext.cs`) for how it maps `DomainEvents`; if it does not `Ignore` it, drop the `pallet.Ignore(x => x.DomainEvents);` line to match.

- [ ] **Step 2: Write the failing shape tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/RowVersionShapeTests.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// What Versioned() adds to a model: a required Guid shadow column row_version marked as a concurrency token on
/// the root only, and on each child an annotation naming its root, so the interceptors can find it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RowVersionShapeTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> versionedFactory = default!;

    public Task InitializeAsync()
    {
        versionedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
            services.AddModuleDbContext<TestVersionedDbContext, ITestVersionedDbContext, ITestVersionedUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestVersionedDbContext.Schema)));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await versionedFactory.DisposeAsync();

    [Fact]
    public void Builder_DeclaresVersioned_AndChildrenNeverTakeIt()
    {
        AggregateFeatureRegistry registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(PalletAggregateFeatureConfiguration)]);

        Assert.True(registry.For(typeof(Pallet)).Versioned);
        Assert.False(registry.ForChild(typeof(Pallet), typeof(Bay)).Versioned);
    }

    [Fact]
    public void Root_CarriesARequiredConcurrencyTokenColumn()
    {
        IEntityType pallet = FindEntityType(typeof(Pallet));
        IProperty version = Assert.IsAssignableFrom<IProperty>(pallet.FindProperty(RowVersionColumns.RowVersion));

        Assert.True(RowVersionColumns.IsVersioned(pallet));
        Assert.True(version.IsShadowProperty());
        Assert.Equal(typeof(Guid), version.ClrType);
        Assert.False(version.IsNullable);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(RowVersionColumns.RowVersionColumn, version.GetColumnName());
    }

    [Fact]
    public void Child_HasNoColumn_AndNamesItsRoot()
    {
        IEntityType bay = FindEntityType(typeof(Bay));

        Assert.False(RowVersionColumns.IsVersioned(bay));
        Assert.Null(bay.FindProperty(RowVersionColumns.RowVersion));
        Assert.Equal(FindEntityType(typeof(Pallet)).Name, RowVersionColumns.RootOf(bay));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = versionedFactory.Services.CreateScope();
        DbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        return Assert.IsAssignableFrom<IEntityType>(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(entityType));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RowVersionShapeTests"`
Expected: build FAILS with `CS1061` (`Versioned` not on the builder) and `CS0103` (`RowVersionColumns`).

- [ ] **Step 4: Add the flag, builder method and child rule**

`AggregateFeatures.cs`:
```csharp
namespace Sergin.SharedKernel.Application.Aggregates;

/// <summary>The features one entity type has switched on. Grows a flag per feature.</summary>
public sealed record AggregateFeatures(bool Audited, bool SoftDeletable, bool Versioned)
{
    public static AggregateFeatures None { get; } = new(Audited: false, SoftDeletable: false, Versioned: false);
}
```

In `AggregateFeatureBuilder<TAggregateRoot>`, after `SoftDeletable()`:
```csharp
    /// <summary>
    /// Adds a row_version concurrency token to the root's table and rewrites it on every save that changes the
    /// aggregate, a change to a child alone included. One version covers the whole aggregate: children get no
    /// column. A command marked [RequiresExpectedVersion] is then refused when its expected version is stale.
    /// Calling it twice is harmless.
    /// </summary>
    public AggregateFeatureBuilder<TAggregateRoot> Versioned()
    {
        Features = Features with { Versioned = true };
        return this;
    }
```

In `AggregateFeatureRegistry.ForChild`, change the `with` block to:
```csharp
        return declaration.Features with
        {
            Audited = declaration.Features.Audited && !declaration.AuditExceptions.Contains(childType),
            Versioned = false,
        };
```
and add to that method's summary: `A child is never versioned itself: its root's version covers it.`

In `AggregateFeatureRegistryTests.cs`, add `, Versioned: false` to both `new AggregateFeatures(...)` calls (lines 28 and 30).

- [ ] **Step 5: Add `RowVersionColumns`**

`Aggregates/RowVersionColumns.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// The one spelling of the row-version shadow property, its column and its annotations, shared by the
/// convention that adds them, the interceptors that check and rewrite them, and every raw-SQL GetOne that
/// selects the column. A versioned root carries <see cref="VersionedAnnotation"/>; each of its child entity
/// types carries <see cref="VersionRootAnnotation"/> holding the root entity type's name. A string, not a
/// Type: annotations are written into the migrations model snapshot.
/// </summary>
public static class RowVersionColumns
{
    public const string VersionedAnnotation = "Sergin:Versioned";
    public const string VersionRootAnnotation = "Sergin:VersionRoot";

    public const string RowVersion = "RowVersion";
    public const string RowVersionColumn = "row_version";

    /// <summary>Walks the <see cref="IReadOnlyEntityType.BaseType"/> chain, for the same reason as <see cref="AuditColumns.IsAudited"/>.</summary>
    public static bool IsVersioned(IReadOnlyEntityType entityType) =>
        Find(entityType, VersionedAnnotation) is not null;

    /// <summary>The name of the versioned root <paramref name="entityType"/> is a child of; null when it is none.</summary>
    public static string? RootOf(IReadOnlyEntityType entityType) =>
        Find(entityType, VersionRootAnnotation) as string;

    private static object? Find(IReadOnlyEntityType entityType, string annotation)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (IReadOnlyEntityType? current = entityType; current is not null; current = current.BaseType)
        {
            if (current.FindAnnotation(annotation) is { } found)
            {
                return found.Value;
            }
        }

        return null;
    }
}
```

- [ ] **Step 6: Teach the convention**

In `AggregateFeatureConvention.ProcessModelFinalizing`, extend the second loop (it already destructures `(entityType, (_, features))`; change `_` to `rootType`):
```csharp
        foreach ((IConventionEntityType entityType, (Type rootType, AggregateFeatures features)) in resolved)
        {
            if (features.Audited && !SharesItsOwnersTable(entityType))
            {
                AddAuditProperties(entityType.Builder);
            }

            if (features.SoftDeletable && entityType.FindOwnership() is null)
            {
                AddSoftDeleteShape(entityType);
            }

            // ForChild never answers Versioned, so this is the root; a child is marked with its root instead.
            if (features.Versioned)
            {
                AddRowVersionShape(entityType);
            }
            else if (entityType.ClrType != rootType
                && registry.For(rootType).Versioned
                && modelBuilder.Metadata.FindEntityType(rootType) is { } root)
            {
                entityType.Builder.HasAnnotation(RowVersionColumns.VersionRootAnnotation, root.Name);
            }
        }
```
(Keep the existing owned-type comment above the soft-delete `if`.)

Change `AddShadowProperty` to return the property builder (`private static IConventionPropertyBuilder AddShadowProperty(...)`, ending with `return property;`); existing callers discard the result.

Add:
```csharp
    private static void AddRowVersionShape(IConventionEntityType entityType)
    {
        IConventionEntityTypeBuilder builder = entityType.Builder;

        builder.HasAnnotation(RowVersionColumns.VersionedAnnotation, true);

        // A derived type shares its base's table and column.
        if (entityType.BaseType is not null)
        {
            return;
        }

        AddShadowProperty(builder, typeof(Guid), RowVersionColumns.RowVersion, RowVersionColumns.RowVersionColumn, required: true)
            .IsConcurrencyToken(true);
    }
```
Update the class summary's first sentence to list "the row-version column" among the shapes a module no longer writes.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RowVersionShapeTests|FullyQualifiedName~Aggregates|FullyQualifiedName~SoftDelete|FullyQualifiedName~Audit"`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Add Versioned() aggregate feature: row_version concurrency token on the root"
git add tests/Sergin.MeterMinder.IntegrationTests.All src/SharedKernel
git commit -m "Test the row-version model shape"
```

---

### Task 3: Check and bump interceptors, conflict translation

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/VersionedRoots.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Interceptors/ExpectedVersionInterceptor.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Interceptors/RowVersionBumpInterceptor.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/SerginDbContext.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ModuleDbContextExtensions.cs:30-36`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (interceptor registrations)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/RowVersionInterceptorTests.cs`

**Interfaces:**
- Consumes: `ConcurrencyContext`, `ConcurrencyConflictException` (Task 1); `RowVersionColumns` (Task 2); test module (Task 2).
- Produces: `internal static class VersionedRoots { IReadOnlyList<EntityEntry> Touched(DbContext context, bool includeAdded); }`; `ExpectedVersionInterceptor.CheckedRoot` (`object?`), read by `RowVersionBumpInterceptor(ConcurrencyContext, ExpectedVersionInterceptor)`; both interceptors registered scoped; `SerginDbContext.SaveChanges[Async]` throws `ConcurrencyConflictException` on a token mismatch.

- [ ] **Step 1: Write the failing interceptor tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/RowVersionInterceptorTests.cs`:
```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Audit;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// The two row-version interceptors against the test-only Pallet aggregate, with the host's real interceptor
/// chain: every save that changes the aggregate writes a new version and publishes it; a stale expected
/// version refuses the save and persists nothing; a change to a child alone still moves the root's version;
/// a soft delete is checked like any update. Each step runs in its own scope, as a request would.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RowVersionInterceptorTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ResetSchemaSql =
        "DROP SCHEMA IF EXISTS test_versioned CASCADE; CREATE SCHEMA test_versioned;";

    private static readonly string[] FilterName = [SoftDeleteColumns.QueryFilterName];

    private WebApplicationFactory<Program> versionedFactory = default!;

    public async Task InitializeAsync()
    {
        versionedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
        {
            services.AddModuleDbContext<TestVersionedDbContext, ITestVersionedDbContext, ITestVersionedUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestVersionedDbContext.Schema);
            services.AddTransient<INotificationHandler<DomainEventNotification<PalletNudged>>, RenameNeighbourHandler>();
        }));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync() => await versionedFactory.DisposeAsync();

    [Fact]
    public async Task Insert_SetsAVersion_AndPublishesIt()
    {
        (Guid id, RowVersion? published) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        Assert.NotNull(published);
        Assert.Equal(published.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task Update_WithTheCurrentVersion_WritesANewOne()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? second = await ChangeAsync(id, first, pallet => pallet.Rename(UniqueCode()));

        Assert.NotNull(second);
        Assert.NotEqual(first!.Value, second.Value);
        Assert.Equal(second.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ChildOnlyChange_MovesTheRootsVersion()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? second = await ChangeAsync(id, first, pallet => pallet.AddBay("bay-1"), includeBays: true);

        Assert.NotEqual(first!.Value, second!.Value);
        Assert.Equal(second.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task StaleVersion_RefusesTheSave_AndPersistsNothing()
    {
        string code = UniqueCode();
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(code));
        await ChangeAsync(id, first, pallet => pallet.AddBay("bay-1"), includeBays: true);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            ChangeAsync(id, first, pallet => pallet.Rename("stale-rename")));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        Assert.Equal(code, (await context.Pallets.SingleAsync(pallet => pallet.Id == id)).Code);
    }

    [Fact]
    public async Task SoftDelete_WithAStaleVersion_IsRefused()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        await ChangeAsync(id, first, pallet => pallet.Rename(UniqueCode()));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => RemoveAsync(id, first));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        Assert.True(await context.Pallets.AnyAsync(pallet => pallet.Id == id));
    }

    [Fact]
    public async Task SoftDelete_WithTheCurrentVersion_MovesTheVersion()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        await RemoveAsync(id, first);

        Assert.NotEqual(first!.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ExpectedVersion_WithNoChange_Succeeds()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        await ChangeAsync(id, RowVersion.Create(), _ => { });

        Assert.Equal(first!.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ExpectedVersion_WithTwoRootsTouched_Throws()
    {
        (Guid a, RowVersion? versionA) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        (Guid b, _) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        using IServiceScope scope = ScopeWith(versionA);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        foreach (Pallet pallet in await context.Pallets.Where(p => p.Id == a || p.Id == b).ToListAsync())
        {
            pallet.Rename(UniqueCode());
        }

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains(nameof(Pallet), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootChangedByAnEventHandler_IsBumped_ButNotChecked()
    {
        (Guid a, RowVersion? versionA) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        (Guid b, RowVersion? versionB) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? published = await ChangeAsync(a, versionA, pallet => pallet.Nudge(b));

        Assert.Equal(published!.Value, await ReadVersionAsync(a));
        Assert.NotEqual(versionB!.Value, await ReadVersionAsync(b));
    }

    private static string UniqueCode() => $"pallet-{Guid.CreateVersion7()}";

    private IServiceScope ScopeWith(RowVersion? expected)
    {
        IServiceScope scope = versionedFactory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestUserContext(new UserId(Guid.CreateVersion7()));
        scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Expected = expected;
        return scope;
    }

    private async Task<(Guid Id, RowVersion? Published)> SaveNewAsync(Pallet pallet)
    {
        using IServiceScope scope = ScopeWith(expected: null);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        context.Pallets.Add(pallet);
        await context.SaveChangesAsync();

        return (pallet.Id, scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Current);
    }

    private async Task<RowVersion?> ChangeAsync(Guid id, RowVersion? expected, Action<Pallet> change, bool includeBays = false)
    {
        using IServiceScope scope = ScopeWith(expected);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        IQueryable<Pallet> pallets = includeBays ? context.Pallets.Include(p => p.Bays) : context.Pallets;
        change(await pallets.SingleAsync(p => p.Id == id));
        await context.SaveChangesAsync();

        return scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Current;
    }

    private async Task RemoveAsync(Guid id, RowVersion? expected)
    {
        using IServiceScope scope = ScopeWith(expected);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        context.Pallets.Remove(await context.Pallets.SingleAsync(p => p.Id == id));
        await context.SaveChangesAsync();
    }

    private async Task<Guid> ReadVersionAsync(Guid id)
    {
        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        return await context.Pallets
            .IgnoreQueryFilters(FilterName)
            .Where(pallet => pallet.Id == id)
            .Select(pallet => EF.Property<Guid>(pallet, RowVersionColumns.RowVersion))
            .SingleAsync();
    }

    /// <summary>Renames the nudged pallet on the same save, without saving: a second root the handler never loaded itself.</summary>
    private sealed class RenameNeighbourHandler(TestVersionedDbContext context) : IDomainEventHandler<PalletNudged>
    {
        public async Task Handle(PalletNudged domainEvent, CancellationToken cancellationToken)
        {
            Pallet neighbour = await context.Pallets.SingleAsync(p => p.Id == domainEvent.NeighbourId, cancellationToken);
            neighbour.Rename($"nudged-{Guid.CreateVersion7()}");
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~RowVersionInterceptorTests"`
Expected: FAIL. `Insert_SetsAVersion_AndPublishesIt` fails on `Assert.NotNull(published)`; the stale tests fail with "no exception thrown"; the insert itself may fail with a NOT NULL violation on `row_version` (nothing writes it yet).

- [ ] **Step 3: Add `VersionedRoots`**

`Aggregates/VersionedRoots.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// Finds the versioned aggregate roots a pending save changes: a root entry that is itself changed, and the
/// root of every changed child entry, found by walking the child's foreign keys up the aggregate's own
/// navigations (the same principal-to-dependent navigations the convention walked down) to a tracked entry.
/// A changed child whose root is not tracked is refused: every write goes through the root's behaviour, so the
/// root is always loaded.
/// </summary>
internal static class VersionedRoots
{
    public static IReadOnlyList<EntityEntry> Touched(DbContext context, bool includeAdded)
    {
        EntityEntry[] tracked = [.. context.ChangeTracker.Entries()];
        Dictionary<object, EntityEntry> roots = new(ReferenceEqualityComparer.Instance);

        foreach (EntityEntry entry in tracked.Where(IsChanged))
        {
            EntityEntry? root = RowVersionColumns.IsVersioned(entry.Metadata)
                ? entry
                : RowVersionColumns.RootOf(entry.Metadata) is { } rootName
                    ? RootOf(tracked, entry, rootName)
                        ?? throw new InvalidOperationException(
                            $"{entry.Metadata.DisplayName()} changed, but its aggregate root {rootName} is not tracked. "
                            + "Load the root and change its child through it, so the root's version moves with the change.")
                    : null;

            if (root is not null && (includeAdded || root.State != EntityState.Added))
            {
                roots.TryAdd(root.Entity, root);
            }
        }

        return [.. roots.Values];
    }

    private static bool IsChanged(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static EntityEntry? RootOf(EntityEntry[] tracked, EntityEntry child, string rootName)
    {
        foreach (IForeignKey foreignKey in child.Metadata.GetForeignKeys())
        {
            IEntityType principalType = foreignKey.PrincipalEntityType;

            bool alongTheAggregate = (foreignKey.PrincipalToDependent is not null || foreignKey.IsOwnership)
                && (RowVersionColumns.IsVersioned(principalType)
                    ? IsOrDerivesFrom(principalType, rootName)
                    : RowVersionColumns.RootOf(principalType) == rootName);

            if (!alongTheAggregate)
            {
                continue;
            }

            object?[] key = [.. foreignKey.Properties.Select(property => child.Property(property.Name).CurrentValue)];

            EntityEntry? principal = tracked.FirstOrDefault(entry =>
                principalType.IsAssignableFrom(entry.Metadata)
                && foreignKey.PrincipalKey.Properties
                    .Select(property => entry.Property(property.Name).CurrentValue)
                    .SequenceEqual(key));

            if (principal is null)
            {
                return null;
            }

            return RowVersionColumns.IsVersioned(principal.Metadata) ? principal : RootOf(tracked, principal, rootName);
        }

        return null;
    }

    private static bool IsOrDerivesFrom(IReadOnlyEntityType entityType, string name)
    {
        for (IReadOnlyEntityType? current = entityType; current is not null; current = current.BaseType)
        {
            if (current.Name == name)
            {
                return true;
            }
        }

        return false;
    }
}
```

- [ ] **Step 4: Add the check interceptor**

`Interceptors/ExpectedVersionInterceptor.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Interceptors;

/// <summary>
/// Puts <see cref="ConcurrencyContext.Expected"/> into the save as the original value of the changed root's
/// row_version, so the UPDATE (or soft-delete UPDATE, or DELETE) matches only a row still at that version, and
/// a mismatch is EF's DbUpdateConcurrencyException. Registered first, before EventDispatcherInterceptor, so it
/// sees only what the handler changed: a root a domain-event handler changes later is bumped by
/// RowVersionBumpInterceptor but not checked. An expected version guards exactly one root; a save changing
/// more than one with a version set is a programming error.
/// </summary>
internal sealed class ExpectedVersionInterceptor(ConcurrencyContext concurrency) : SaveChangesInterceptor
{
    /// <summary>
    /// The entity whose version the current save checks, for RowVersionBumpInterceptor: it re-applies the
    /// original value (a later interceptor's state change can reset it) and publishes that root's new version.
    /// </summary>
    public object? CheckedRoot { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? context)
    {
        CheckedRoot = null;

        if (context is null || concurrency.Expected is not { } expected)
        {
            return;
        }

        IReadOnlyList<EntityEntry> roots = VersionedRoots.Touched(context, includeAdded: false);

        if (roots.Count > 1)
        {
            throw new InvalidOperationException(
                $"An expected version guards one aggregate, but this save changes {roots.Count} versioned roots: "
                + $"{string.Join(", ", roots.Select(root => root.Metadata.DisplayName()))}. "
                + "Change one aggregate per command, or send no expected version.");
        }

        if (roots.Count == 1)
        {
            roots[0].Property(RowVersionColumns.RowVersion).OriginalValue = expected.Value;
            CheckedRoot = roots[0].Entity;
        }
    }
}
```

- [ ] **Step 5: Add the bump interceptor**

`Interceptors/RowVersionBumpInterceptor.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Interceptors;

/// <summary>
/// Writes a new row_version to every versioned root the save changes, whoever changed it: the handler, a
/// domain-event handler, the soft-delete cascade. Registered last, after SoftDeleteInterceptor, so it sees all
/// of them. A root left unchanged while its child changed has only its row_version marked modified: that is
/// what makes one version cover the whole aggregate. A hard-deleted root is left alone; its DELETE is checked
/// against the original value. After a successful save, the version is published to
/// <see cref="ConcurrencyContext.Current"/>: the checked root's (named by ExpectedVersionInterceptor, the same
/// scoped instance), or the only root's when nothing was checked.
/// </summary>
internal sealed class RowVersionBumpInterceptor(ConcurrencyContext concurrency, ExpectedVersionInterceptor check)
    : SaveChangesInterceptor
{
    private EntityEntry? toPublish;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Bump(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Bump(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Publish();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish();
        return base.SavedChanges(eventData, result);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        toPublish = null;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        toPublish = null;
        base.SaveChangesFailed(eventData);
    }

    private void Bump(DbContext? context)
    {
        toPublish = null;

        if (context is null)
        {
            return;
        }

        IReadOnlyList<EntityEntry> roots =
            [.. VersionedRoots.Touched(context, includeAdded: true).Where(root => root.State != EntityState.Deleted)];

        EntityEntry? checkedRoot = roots.FirstOrDefault(root => ReferenceEquals(root.Entity, check.CheckedRoot));

        // Re-applied, not trusted: SoftDeleteInterceptor sets a deleted root back to Unchanged before stamping
        // it, and a state change can reset original values to the current ones, which would drop the check.
        if (checkedRoot is not null && concurrency.Expected is { } expected)
        {
            checkedRoot.Property(RowVersionColumns.RowVersion).OriginalValue = expected.Value;
        }

        foreach (EntityEntry root in roots)
        {
            PropertyEntry version = root.Property(RowVersionColumns.RowVersion);
            version.CurrentValue = RowVersion.Create().Value;

            if (root.State == EntityState.Unchanged)
            {
                version.IsModified = true;
            }
        }

        toPublish = checkedRoot ?? (roots.Count == 1 ? roots[0] : null);
    }

    private void Publish()
    {
        if (toPublish is not null)
        {
            concurrency.Current = RowVersion.Create((Guid)toPublish.Property(RowVersionColumns.RowVersion).CurrentValue!);
            toPublish = null;
        }
    }
}
```

- [ ] **Step 6: Translate the conflict**

In `SerginDbContext`, add `using Sergin.SharedKernel.Application.Concurrency;` and:
```csharp
    /// <summary>
    /// A row_version mismatch surfaces as <see cref="ConcurrencyConflictException"/>, which the Application
    /// layer can catch without referencing EF; ExpectedVersionPipelineBehavior turns it into the stale-version
    /// error. The save's transaction has rolled back by then.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The aggregate changed since the expected version was read.", exception);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The aggregate changed since the expected version was read.", exception);
        }
    }
```

- [ ] **Step 7: Register and order the interceptors**

In `SerginCoreExtensions.AddSerginCore`, after `builder.Services.AddScoped<SoftDeleteInterceptor>();`:
```csharp
        builder.Services.AddScoped<ExpectedVersionInterceptor>();
        builder.Services.AddScoped<RowVersionBumpInterceptor>();
```

In `ModuleDbContextExtensions.AddModuleDbContext`, replace the comment and `AddInterceptors` call with:
```csharp
            // Order matters: EF runs interceptors in registration order. The expected version is applied first,
            // so it guards only what the handler changed, not what domain-event handlers change during dispatch.
            // Stamping must see whatever those handlers added or changed; soft delete runs after audit so a
            // delete it turns into an update is not stamped as a modification; the row version is bumped last,
            // once every other interceptor has decided what the save changes.
            .AddInterceptors(
                sp.GetRequiredService<ExpectedVersionInterceptor>(),
                sp.GetRequiredService<EventDispatcherInterceptor>(),
                sp.GetRequiredService<AuditStampInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<RowVersionBumpInterceptor>()));
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~Concurrency|FullyQualifiedName~SoftDelete|FullyQualifiedName~Audit|FullyQualifiedName~Events"`
Expected: all PASS. If `ChildOnlyChange_MovesTheRootsVersion` fails, do not set `root.State = EntityState.Modified` (it would rewrite every column); check that `VersionedRoots.Touched` found the root through the `RootOf` walk.

- [ ] **Step 9: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Check and bump row_version in the save pipeline, and translate conflicts"
git add tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency src/SharedKernel
git commit -m "Test the row-version interceptors"
```

---

### Task 4: Version DeviceManagement's aggregates

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/DeviceAggregateFeatureConfiguration.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/ManufacturerAggregateFeatureConfiguration.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<timestamp>_AddRowVersionColumns.cs` (+ `.Designer.cs`, snapshot, both generated)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementRowVersionColumnsTests.cs`

**Interfaces:**
- Consumes: `Versioned()`, `RowVersionColumns` (Task 2).
- Produces: `dm.device.row_version` and `dm.manufacturer.row_version`, `uuid NOT NULL`.

- [ ] **Step 1: Write the failing model test**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementRowVersionColumnsTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>The dm model: both roots carry the row_version token, DeviceModel names Manufacturer as its root, and an unconfigured type carries nothing.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementRowVersionColumnsTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData(typeof(Device))]
    [InlineData(typeof(Manufacturer))]
    public void Root_CarriesTheToken(Type entityType)
    {
        IProperty version = Assert.IsAssignableFrom<IProperty>(FindEntityType(entityType).FindProperty(RowVersionColumns.RowVersion));

        Assert.True(version.IsConcurrencyToken);
        Assert.False(version.IsNullable);
        Assert.Equal(RowVersionColumns.RowVersionColumn, version.GetColumnName());
    }

    [Fact]
    public void DeviceModel_HasNoColumn_AndNamesManufacturer()
    {
        IEntityType model = FindEntityType(typeof(DeviceModel));

        Assert.Null(model.FindProperty(RowVersionColumns.RowVersion));
        Assert.Equal(FindEntityType(typeof(Manufacturer)).Name, RowVersionColumns.RootOf(model));
    }

    [Fact]
    public void UnconfiguredType_CarriesNone()
    {
        IEntityType outbox = FindEntityType(typeof(OutboxMessage));

        Assert.False(RowVersionColumns.IsVersioned(outbox));
        Assert.Null(RowVersionColumns.RootOf(outbox));
        Assert.Null(outbox.FindProperty(RowVersionColumns.RowVersion));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        return Assert.IsAssignableFrom<IEntityType>(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(entityType));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementRowVersionColumnsTests"`
Expected: `Root_CarriesTheToken` and `DeviceModel_HasNoColumn_AndNamesManufacturer` FAIL (property null / root null).

- [ ] **Step 3: Turn it on**

`DeviceAggregateFeatureConfiguration.Configure`: `builder.Audited().SoftDeletable().Versioned();`
`ManufacturerAggregateFeatureConfiguration.Configure`: `builder.Audited().SoftDeletable().Versioned();`

- [ ] **Step 4: Scaffold and hand-edit the migration**

Run:
```bash
dotnet ef migrations add AddRowVersionColumns \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```
Replace the generated `<timestamp>_AddRowVersionColumns.cs` body with a file-scoped namespace and this `Up`/`Down` (keep the generated class name, namespace and `using`s; model it on `20260923090420_AddAuditColumns.cs`):
```csharp
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Added nullable, backfilled, then made NOT NULL: existing rows need a version before the constraint.
        // gen_random_uuid() is v4, not v7; a backfilled version only has to differ from the next one written.
        migrationBuilder.AddColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "manufacturer",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql("UPDATE dm.device SET row_version = gen_random_uuid() WHERE row_version IS NULL;");
        migrationBuilder.Sql("UPDATE dm.manufacturer SET row_version = gen_random_uuid() WHERE row_version IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "manufacturer",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "row_version", schema: "dm", table: "device");
        migrationBuilder.DropColumn(name: "row_version", schema: "dm", table: "manufacturer");
    }
```
Leave `.Designer.cs` and the snapshot as generated. Check the snapshot diff: it must show `row_version` with `.IsConcurrencyToken()` on `Device` and `Manufacturer`, `Sergin:Versioned` on both, and `Sergin:VersionRoot` on `DeviceModel`, and nothing else.

- [ ] **Step 5: Run to verify it passes, plus the dm suites**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementRowVersionColumnsTests|FullyQualifiedName~SoftDelete|FullyQualifiedName~Audit|FullyQualifiedName~Manufacturers|FullyQualifiedName~Devices"`
Expected: all PASS (commands are not marked yet, so existing write tests still send without a version).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency
git commit -m "Version Device and Manufacturer with a row_version column"
```

---

### Task 5: Read side and the Blazor dispatcher

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/VersionedResult.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ISerginDispatcher.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Dispatching/ScopedSerginDispatcher.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/IGetDeviceQueryRepository.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/Commands/GetOne/GetDeviceByIdQueryCommandHandler.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/IGetManufacturerQueryRepository.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/Commands/GetOne/GetManufacturerByIdQueryCommandHandler.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Devices/Repositories/Queries/DeviceQueryRepository.cs` (`GetDeviceById`)
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure/Manufacturers/Repositories/Queries/ManufacturerQueryRepository.cs` (`GetManufacturerById`)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs` (stub repository, server registration)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.cs`

**Interfaces:**
- Consumes: `ConcurrencyContext`, `Versioned<T>` (Task 1); dm columns (Task 4).
- Produces:
  - `VersionedResult<TResponse>(ErrorOr<TResponse> Result, RowVersion? Version)`.
  - `ISerginDispatcher.SendVersionedAsync<TResponse>(IRequest<ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default)` → `Task<VersionedResult<TResponse>>`.
  - `IGetDeviceQueryRepository.GetDeviceById` → `Task<Versioned<DeviceQueryResponse>?>`; `IGetManufacturerQueryRepository.GetManufacturerById` → `Task<Versioned<ManufacturerQueryResponse>?>`.

- [ ] **Step 1: Write the failing read tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.cs`:
```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// DeviceManagement's versioned slices through ISerginDispatcher, the way a Blazor page sends them: a GetOne
/// hands back the aggregate's version beside its read model, and a guarded write is accepted at the current
/// version and refused at any other.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class DeviceManagementConcurrencyTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetManufacturerById_ReturnsItsVersion_AndTheSameOneTwice()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> first =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        VersionedResult<ManufacturerQueryResponse> second =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        Assert.False(first.Result.IsError);
        Assert.NotNull(first.Version);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task GetDeviceById_ReturnsItsVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeviceQueryResponse> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));

        Assert.False(loaded.Result.IsError);
        Assert.NotNull(loaded.Version);
    }

    [Fact]
    public async Task GetManufacturerById_ForAMissingRecord_HasNoVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, loaded.Result.FirstError.Type);
        Assert.Null(loaded.Version);
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId)
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        VersionedResult<AddDeviceModelCommandResponse> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            loaded.Version);
        Assert.False(added.Result.IsError, added.Result.IsError ? added.Result.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Result.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher)
    {
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, await CreateManufacturerAsync(dispatcher));
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), modelId));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
```
Keep `partial`: Task 6 adds the write tests in a second file.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementConcurrencyTests"`
Expected: build FAILS with `CS1061` (`SendVersionedAsync`) and `CS0246` (`VersionedResult`).

- [ ] **Step 3: Add the dispatcher overload**

`Dispatching/VersionedResult.cs`:
```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

/// <summary>
/// A send's result together with the aggregate version it read or wrote. <see cref="Version"/> is null when the
/// send failed, or touched no versioned aggregate.
/// </summary>
public sealed record VersionedResult<TResponse>(ErrorOr<TResponse> Result, RowVersion? Version);
```

`ISerginDispatcher.cs`:
```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Presentation.Blazor.Dispatching;

public interface ISerginDispatcher
{
    Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="request"/> with <paramref name="expected"/> as the version the caller last saw, and
    /// answers the version the send read or wrote. A detail page loads through this and hands the version back
    /// with its write, so a write made against a record someone else changed since is refused.
    /// </summary>
    Task<VersionedResult<TResponse>> SendVersionedAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default);
}
```

In `ScopedSerginDispatcher`, add `using Sergin.SharedKernel.Application.Concurrency;` and `using Sergin.SharedKernel.Domain;`, then:
```csharp
    public async Task<VersionedResult<TResponse>> SendVersionedAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = userContext;

        // The version travels in the child scope beside the user, never on the request.
        ConcurrencyContext concurrency = scope.ServiceProvider.GetRequiredService<ConcurrencyContext>();
        concurrency.Expected = expected;

        ErrorOr<TResponse> result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);

        return new VersionedResult<TResponse>(result, result.IsError ? null : concurrency.Current);
    }
```
Search for any other `ISerginDispatcher` implementation (`rg "ISerginDispatcher" src tests -g "*.cs"`); none exists today, but add the member to any you find.

- [ ] **Step 4: Read the version in the two GetOne slices**

`IGetDeviceQueryRepository.cs`:
```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

public interface IGetDeviceQueryRepository
{
    Task<Versioned<DeviceQueryResponse>?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default);
}
```

`GetDeviceByIdQueryCommandHandler.cs`:
```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

// The version goes out beside the response, through the scope's ConcurrencyContext, never on the record.
internal sealed class GetDeviceByIdQueryCommandHandler(IGetDeviceQueryRepository repository, ConcurrencyContext concurrency)
    : IQueryHandler<GetDeviceByIdQueryCommand, DeviceQueryResponse>
{
    public async Task<ErrorOr<DeviceQueryResponse>> Handle(GetDeviceByIdQueryCommand request, CancellationToken cancellationToken)
    {
        Versioned<DeviceQueryResponse>? res = await repository.GetDeviceById(new DeviceIntenralId(request.Id), cancellationToken);

        if (res is null)
        {
            return Error.NotFound();
        }

        concurrency.Current = res.Version;

        return res.Value;
    }
}
```

`IGetManufacturerQueryRepository.cs` and `GetManufacturerByIdQueryCommandHandler.cs`: the same change — return type `Task<Versioned<ManufacturerQueryResponse>?>`, handler takes `ConcurrencyContext concurrency`, sets `concurrency.Current = res.Version;` and returns `res.Value`.

`DeviceQueryRepository.GetDeviceById` (add `using Sergin.SharedKernel.Application.Concurrency;` and `using Sergin.SharedKernel.Domain;`):
```csharp
    // row_version is split off into its own mapped part: DeviceQueryResponse binds through its constructor, which
    // has no parameter for it, and the version travels beside the response, not on it.
    public async Task<Versioned<DeviceQueryResponse>?> GetDeviceById(
        DeviceIntenralId Id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT d.id, d.device_id AS deviceId, d.device_model_id AS deviceModelId, dm_.name AS deviceModelName,
                   d.row_version AS rowVersion
            FROM dm.device d
            JOIN dm.device_model dm_ ON dm_.id = d.device_model_id
            WHERE d.id = @Id AND d.deleted_at_utc IS NULL;
            """;

        IEnumerable<Versioned<DeviceQueryResponse>> rows = await connection.QueryAsync<DeviceQueryResponse, Guid, Versioned<DeviceQueryResponse>>(
            queries,
            (device, rowVersion) => new Versioned<DeviceQueryResponse>(device, RowVersion.Create(rowVersion)),
            new { Id = Id.Value },
            splitOn: "rowVersion");

        return rows.SingleOrDefault();
    }
```

`ManufacturerQueryRepository.GetManufacturerById`, same shape:
```csharp
    public async Task<Versioned<ManufacturerQueryResponse>?> GetManufacturerById(
        ManufacturerId id, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = await connectionFactory.CreateConnectionAsync();

        string queries =
           """
            SELECT id, name, address, row_version AS rowVersion
            FROM dm.manufacturer
            WHERE id = @Id AND deleted_at_utc IS NULL;
            """;

        IEnumerable<Versioned<ManufacturerQueryResponse>> rows = await connection.QueryAsync<ManufacturerQueryResponse, Guid, Versioned<ManufacturerQueryResponse>>(
            queries,
            (manufacturer, rowVersion) => new Versioned<ManufacturerQueryResponse>(manufacturer, RowVersion.Create(rowVersion)),
            new { Id = id.Value },
            splitOn: "rowVersion");

        return rows.SingleOrDefault();
    }
```
Add the same comment above it as above `GetDeviceById`.

- [ ] **Step 5: Keep the gRPC test compiling**

In `DeviceGrpcRoundTripTests`:
- `InitializeAsync`, after `builder.Services.AddScoped(p => p.GetRequiredService<IUserContextFactory>().CreateUserContext());`: `builder.Services.AddScoped<ConcurrencyContext>();`
- `StubDeviceQueryRepository`:
```csharp
    private sealed class StubDeviceQueryRepository : IGetDeviceQueryRepository
    {
        private readonly Dictionary<DeviceIntenralId, Versioned<DeviceQueryResponse>> devices = [];

        public void Add(DeviceIntenralId id, DeviceQueryResponse response, RowVersion version) =>
            devices[id] = new Versioned<DeviceQueryResponse>(response, version);

        public Task<Versioned<DeviceQueryResponse>?> GetDeviceById(DeviceIntenralId Id, CancellationToken cancellationToken = default) =>
            Task.FromResult(devices.GetValueOrDefault(Id));
    }
```
- In `RemoteDispatch_ForExistingDevice_ReturnsSameResultAsLocalHandler`: `repository.Add(internalId, expected, RowVersion.Create());`
- In `BuildRemoteSender`: `services.AddScoped<ConcurrencyContext>();` (the remote side's pipeline does not need it yet, but Task 9 does).
- Add `using Sergin.SharedKernel.Application.Concurrency;` and `using Sergin.SharedKernel.Domain;`.

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementConcurrencyTests|FullyQualifiedName~DeviceGrpcRoundTripTests|FullyQualifiedName~Devices|FullyQualifiedName~Manufacturers|FullyQualifiedName~Shell"`
Expected: all PASS. If Dapper throws on the split (`When using the multi-mapping APIs ensure you set the splitOn param`), check the alias is exactly `rowVersion` and is the last column.

- [ ] **Step 7: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Add ISerginDispatcher.SendVersionedAsync"
git add src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All src/SharedKernel
git commit -m "Read the aggregate version beside DeviceManagement's GetOne responses"
```

---

### Task 6: Guard DeviceManagement's writes

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Devices/Commands/Delete/DeleteDeviceCommand.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/Commands/Delete/DeleteManufacturerCommand.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/Manufacturers/DeviceModels/Commands/Add/AddDeviceModelCommand.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/VersionedDispatch.cs`
- Create: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.Writes.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/SoftDelete/DeviceManagementSoftDeleteTests.cs` (lines 50, 56, 77, 94, 114, 117, 192)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Manufacturers/DeviceModels/DeviceModelTests.cs` (lines 42, 75, 91, 96, 122, 123, 229)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceReadModelTests.cs` (line 112)
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Validation/RepositoryRuleTests.cs` (line 227)

**Interfaces:**
- Consumes: `SendVersionedAsync`, `VersionedResult` (Task 5); `RequiresExpectedVersionAttribute`, `VersionErrors` (Task 1).
- Produces: test helpers `SendAtManufacturerVersionAsync<TResponse>(this ISerginDispatcher, Guid manufacturerId, IRequest<ErrorOr<TResponse>> command)` and `SendAtDeviceVersionAsync<TResponse>(this ISerginDispatcher, Guid deviceId, IRequest<ErrorOr<TResponse>> command)`.

- [ ] **Step 1: Write the failing write tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/DeviceManagementConcurrencyTests.Writes.cs`:
```csharp
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

public sealed partial class DeviceManagementConcurrencyTests
{
    [Fact]
    public async Task AddDeviceModel_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.Equal(VersionErrors.RequiredType, (int)added.FirstError.Type);
    }

    [Fact]
    public async Task AddDeviceModel_AtTheCurrentVersion_ReturnsANewOne_AndTheOldOneIsThenStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        VersionedResult<AddDeviceModelCommandResponse> first = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loaded.Version);
        VersionedResult<AddDeviceModelCommandResponse> replay = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loaded.Version);

        Assert.False(first.Result.IsError, first.Result.IsError ? first.Result.FirstError.Description : string.Empty);
        Assert.NotNull(first.Version);
        Assert.NotEqual(loaded.Version, first.Version);
        Assert.True(VersionErrors.IsStale(replay.Result.FirstError));
        Assert.Equal(1, await CountModelsAsync(dispatcher, manufacturerId));
    }

    [Fact]
    public async Task AddDeviceModel_WithAnotherManufacturersVersion_IsStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId a = await CreateManufacturerAsync(dispatcher);
        ManufacturerId b = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> loadedA =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(a.Value));

        VersionedResult<AddDeviceModelCommandResponse> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(b, new DeviceModelName($"model-{Guid.CreateVersion7()}")), loadedA.Version);

        Assert.True(VersionErrors.IsStale(added.Result.FirstError));
    }

    [Fact]
    public async Task DeleteDevice_AtAStaleVersion_IsRefused_AndTheDeviceStaysLive()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeleteDeviceCommandResponse> deleted =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), Sergin.SharedKernel.Domain.RowVersion.Create());

        Assert.True(VersionErrors.IsStale(deleted.Result.FirstError));
        Assert.False((await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(deviceId))).IsError);
    }

    [Fact]
    public async Task DeleteDevice_AlreadyDeleted_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid deviceId = await CreateDeviceAsync(dispatcher);

        VersionedResult<DeviceQueryResponse> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));
        Assert.False((await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), loaded.Version)).Result.IsError);

        VersionedResult<DeleteDeviceCommandResponse> again =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(deviceId), loaded.Version);

        Assert.Equal(ErrorType.NotFound, again.Result.FirstError.Type);
    }

    [Fact]
    public async Task DeleteManufacturer_AfterAModelWasAdded_IsStale()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        VersionedResult<ManufacturerQueryResponse> pageLoad =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));
        await AddDeviceModelAsync(dispatcher, manufacturerId);

        VersionedResult<DeleteManufacturerCommandResponse> deleted =
            await dispatcher.SendVersionedAsync(new DeleteManufacturerCommand(manufacturerId.Value), pageLoad.Version);

        Assert.True(VersionErrors.IsStale(deleted.Result.FirstError));
        Assert.False((await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value))).IsError);
    }

    private static async Task<int> CountModelsAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId)
    {
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> models = await dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(manufacturerId, Paggination.Create(1000, 1)));
        Assert.False(models.IsError);

        return models.Value.Total;
    }
}
```
- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceManagementConcurrencyTests"`
Expected: `AddDeviceModel_WithoutAVersion_IsRefusedWith428` FAILS (the model is added); the stale tests FAIL only where a version is required (`AddDeviceModel_WithoutAVersion...`); `AddDeviceModel_AtTheCurrentVersion...`, `...AnotherManufacturersVersion...`, `DeleteDevice_AtAStaleVersion...` and `DeleteManufacturer_AfterAModelWasAdded...` already PASS, because the interceptors check whatever version is sent. That is expected: this task's change is the attribute.

- [ ] **Step 3: Mark the three commands**

`DeleteDeviceCommand.cs`:
```csharp
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;

[RequiredPermissions("permission.dm.devices.delete")]
[RequiresExpectedVersion]
public sealed record DeleteDeviceCommand(Guid Id) : ICommand<DeleteDeviceCommandResponse>;
```
`DeleteManufacturerCommand.cs`: add the same `using` and `[RequiresExpectedVersion]` under its `[RequiredPermissions]`.
`AddDeviceModelCommand.cs`:
```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;

// Guarded by the manufacturer's version: adding a model changes the Manufacturer aggregate.
[RequiresExpectedVersion]
public sealed record AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<AddDeviceModelCommandResponse>;
```

- [ ] **Step 4: Add the test helper**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/VersionedDispatch.cs`:
```csharp
using ErrorOr;
using MediatR;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// Sends a guarded command the way a detail page does: read the aggregate's current version first, then send
/// the command with it. When the aggregate cannot be read (it does not exist, or is deleted), a fresh version is
/// sent instead, so what comes back is the handler's not-found rather than the pipeline's missing-version error.
/// For tests whose subject is not concurrency.
/// </summary>
internal static class VersionedDispatch
{
    public static async Task<ErrorOr<TResponse>> SendAtManufacturerVersionAsync<TResponse>(
        this ISerginDispatcher dispatcher, Guid manufacturerId, IRequest<ErrorOr<TResponse>> command)
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId));

        return (await dispatcher.SendVersionedAsync(command, loaded.Version ?? RowVersion.Create())).Result;
    }

    public static async Task<ErrorOr<TResponse>> SendAtDeviceVersionAsync<TResponse>(
        this ISerginDispatcher dispatcher, Guid deviceId, IRequest<ErrorOr<TResponse>> command)
    {
        VersionedResult<DeviceQueryResponse> loaded =
            await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));

        return (await dispatcher.SendVersionedAsync(command, loaded.Version ?? RowVersion.Create())).Result;
    }
}
```

- [ ] **Step 5: Move existing callers onto the helper**

Mechanical, one pattern per command. Add `using Sergin.MeterMinder.IntegrationTests.All.Concurrency;` to each file touched.
- `await dispatcher.SendAsync(new AddDeviceModelCommand(X, name))` → `await dispatcher.SendAtManufacturerVersionAsync(X.Value, new AddDeviceModelCommand(X, name))` where `X` is the `ManufacturerId` passed to the command (`DeviceModelTests.cs` lines 42, 75, 91, 96, 122, 123, 229; `DeviceReadModelTests.cs` line 112; `RepositoryRuleTests.cs` line 227; `DeviceManagementSoftDeleteTests.cs` line 192). Line 75 of `DeviceModelTests` passes an inline `new ManufacturerId(Guid.CreateVersion7())`: hoist it into a local first so the same id goes to both arguments.
- `await dispatcher.SendAsync(new DeleteDeviceCommand(id))` → `await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(id))` (`DeviceManagementSoftDeleteTests.cs` lines 50, 56, 77, 114).
- `await dispatcher.SendAsync(new DeleteManufacturerCommand(X))` → `await dispatcher.SendAtManufacturerVersionAsync(X, new DeleteManufacturerCommand(X))` (`DeviceManagementSoftDeleteTests.cs` lines 94, 117).
- Leave `DeviceManagementSoftDeleteTests.cs` lines 148 and 150 as they are: that test sends as a caller without permissions, and the permission check runs before the version check, so it still answers Forbidden.

Then find any caller the line list missed:
Run: `rg "SendAsync\(\s*new (AddDeviceModelCommand|DeleteDeviceCommand|DeleteManufacturerCommand)" tests`
Expected: only the two lines in the Forbidden test (148, 150) and the deliberate 428 test in `DeviceManagementConcurrencyTests.Writes.cs`.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All
git commit -m "Require an expected version on DeviceManagement's delete and add-model commands"
```

---

### Task 7: Blazor pages send the version

**Files:**
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Devices/Pages/DeviceDetailPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Manufacturers/Pages/ManufacturerDetailPage.razor.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Presentation.Blazor/Manufacturers/DeviceModels/Pages/AddDeviceModelPage.razor.cs`

**Interfaces:**
- Consumes: `SendVersionedAsync`, `VersionedResult` (Task 5); `VersionErrors.IsStale` (Task 1).

No `.razor` markup changes.

- [ ] **Step 1: `DeviceDetailPage`**

Add `using Sergin.SharedKernel.Application.Concurrency;` and `using Sergin.SharedKernel.Domain;`, a field `private RowVersion? version;`, and replace `OnParametersSetAsync` and the dispatch part of `DeleteAsync`:
```csharp
    protected override Task OnParametersSetAsync() => LoadAsync();

    // Keeps the version the device was read at, so the delete can say which device it means.
    private async Task LoadAsync()
    {
        VersionedResult<DeviceQueryResponse> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(Id));

        if (loaded.Result.IsError)
        {
            device = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.Result.FirstError);

            return;
        }

        problem = null;
        device = loaded.Result.Value;
        version = loaded.Version;
    }
```
In `DeleteAsync`, replace from `deleting = true;` to the end:
```csharp
        deleting = true;

        VersionedResult<DeleteDeviceCommandResponse> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(Id), version);

        deleting = false;

        if (result.Result.IsError)
        {
            ErrorPresenter.Notify(result.Result.Errors);

            // Someone changed the device after this page loaded it: show what is there now, with its version.
            if (result.Result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo("/dm/devices");
```

- [ ] **Step 2: `ManufacturerDetailPage`**

Same change as Step 1 with `ManufacturerQueryResponse`, `GetManufacturerByIdQueryCommand`, field `manufacturer`, `DeleteManufacturerCommand`, `DeleteManufacturerCommandResponse`, and navigation to `/dm/manufacturers`:
```csharp
    protected override Task OnParametersSetAsync() => LoadAsync();

    // Keeps the version the manufacturer was read at, so the delete can say which manufacturer it means.
    private async Task LoadAsync()
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Id));

        if (loaded.Result.IsError)
        {
            manufacturer = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.Result.FirstError);

            return;
        }

        problem = null;
        manufacturer = loaded.Result.Value;
        version = loaded.Version;
    }
```
```csharp
        deleting = true;

        VersionedResult<DeleteManufacturerCommandResponse> result =
            await Dispatcher.SendVersionedAsync(new DeleteManufacturerCommand(Id), version);

        deleting = false;

        if (result.Result.IsError)
        {
            ErrorPresenter.Notify(result.Result.Errors);

            // Someone changed the manufacturer (added a model, say) after this page loaded it.
            if (result.Result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo("/dm/manufacturers");
```

- [ ] **Step 3: `AddDeviceModelPage`**

Add the two `using`s, a field `private RowVersion? manufacturerVersion;`, and replace `OnParametersSetAsync` and `SubmitAsync`'s dispatch:
```csharp
    /// <summary>
    /// Loads the manufacturer to name it in the trail and to keep the version the new model is added against. A
    /// failure is deliberately silent: the step keeps its placeholder, and an unknown manufacturer is reported
    /// by the submit as not-found — a breadcrumb label is not worth a second snackbar.
    /// </summary>
    protected override Task OnParametersSetAsync() => LoadManufacturerAsync();

    private async Task LoadManufacturerAsync()
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));

        manufacturerName = loaded.Result.IsError ? null : loaded.Result.Value.Name;
        manufacturerVersion = loaded.Version;
    }
```
```csharp
        submitting = true;

        // A manufacturer that could not be read has no version: send a fresh one, so the submit reports the
        // handler's not-found instead of a missing version.
        VersionedResult<AddDeviceModelCommandResponse> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), manufacturerVersion ?? RowVersion.Create());

        submitting = false;

        if (result.Result.IsError)
        {
            // Every error, not the first: a duplicate name arrives as one validation error from the aggregate,
            // an unknown manufacturer as not-found from the handler, a stale manufacturer as a version error.
            ErrorPresenter.Notify(result.Result.Errors);

            if (result.Result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadManufacturerAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}/models/{result.Result.Value.Id}");
```

- [ ] **Step 4: Build and run the shell tests**

Run: `dotnet build Sergin.MeterMinder.slnx` then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~Shell"`
Expected: build succeeds with 0 warnings; all PASS (detail pages still prerender, breadcrumbs unchanged).

- [ ] **Step 5: Check the stale path in a browser**

Run the host (`dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`, http://localhost:5002). Open one manufacturer in two tabs. In tab 1, add a device model. In tab 2, click Delete and confirm. Expected: tab 2 shows the `General.VersionStale` snackbar, stays on the page, and a second Delete succeeds. (The headless-browser memory note describes a Playwright driver for this if a manual run is not possible.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/DeviceManagement
git commit -m "Send the loaded version with deletes and new device models, and reload on a stale answer"
```

---

### Task 8: WebApi `If-Match`/`ETag` filter

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.WebApi/Concurrency/ExpectedVersionEndpointFilter.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/Sergin.SharedKernel.Hosts.WebApi.csproj` (add a `ProjectReference` to `..\Sergin.SharedKernel.Presentation.WebApi\Sergin.SharedKernel.Presentation.WebApi.csproj`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs` (`UseSerginWebApiAsync` endpoint mapping)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/ExpectedVersionEndpointFilterTests.cs`

**Interfaces:**
- Consumes: `ConcurrencyContext` (Task 1).
- Produces: `public sealed class ExpectedVersionEndpointFilter : IEndpointFilter`.

- [ ] **Step 1: Write the failing filter tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Concurrency/ExpectedVersionEndpointFilterTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.WebApi.Concurrency;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// The filter on a one-endpoint test server: a strong If-Match becomes the expected version, a version the
/// endpoint leaves in Current becomes the ETag, a malformed tag is a 400, and * or a weak tag counts as absent
/// (so a guarded command then answers 428 from the pipeline).
/// </summary>
public sealed class ExpectedVersionEndpointFilterTests : IAsyncLifetime
{
    private static readonly Guid Written = Guid.CreateVersion7();

    private WebApplication app = default!;
    private HttpClient client = default!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ConcurrencyContext>();

        app = builder.Build();

        RouteGroupBuilder group = app.MapGroup("/t").AddEndpointFilter<ExpectedVersionEndpointFilter>();
        group.MapPost("/echo", (ConcurrencyContext concurrency) =>
        {
            Guid? seen = concurrency.Expected?.Value;
            concurrency.Current = RowVersion.Create(Written);
            return Results.Ok(seen);
        });

        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task StrongIfMatch_BecomesTheExpectedVersion_AndCurrentBecomesTheETag()
    {
        var sent = Guid.CreateVersion7();

        using HttpResponseMessage response = await PostAsync($"\"{sent}\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sent, await response.Content.ReadFromJsonAsync<Guid?>());
        Assert.Equal($"\"{Written}\"", response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"0192a000-0000-7000-8000-000000000001\"")]
    public async Task AbsentWildcardOrWeakTag_LeavesNoExpectedVersion(string? ifMatch)
    {
        using HttpResponseMessage response = await PostAsync(ifMatch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await response.Content.ReadFromJsonAsync<Guid?>());
    }

    [Theory]
    [InlineData("not-a-tag")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    public async Task MalformedTag_IsA400(string ifMatch)
    {
        using HttpResponseMessage response = await PostAsync(ifMatch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostAsync(string? ifMatch)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/t/echo");

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }
}
```
If `Microsoft.AspNetCore.TestHost` does not resolve, add `<PackageReference Include="Microsoft.AspNetCore.TestHost" />` to the test csproj and a matching `<PackageVersion>` (same version as `Microsoft.AspNetCore.Mvc.Testing`) to `Directory.Packages.props`, alphabetically.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ExpectedVersionEndpointFilterTests"`
Expected: build FAILS with `CS0234` (namespace `Sergin.SharedKernel.Presentation.WebApi.Concurrency` not found).

- [ ] **Step 3: Add the filter**

`Concurrency/ExpectedVersionEndpointFilter.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Presentation.WebApi.Concurrency;

/// <summary>
/// Maps HTTP's conditional-request headers onto <see cref="ConcurrencyContext"/> for every endpoint in a module
/// group: a strong <c>If-Match: "&lt;version&gt;"</c> becomes the expected version, and a version the send
/// read or wrote comes back as <c>ETag</c>. <c>*</c> and weak tags carry no version this platform can check,
/// so they count as absent, and a command marked [RequiresExpectedVersion] then answers 428. Anything else in
/// If-Match is a 400. Endpoints need no change: the version never appears on a request or response body.
/// </summary>
public sealed class ExpectedVersionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;
        ConcurrencyContext concurrency = http.RequestServices.GetRequiredService<ConcurrencyContext>();
        string ifMatch = http.Request.Headers.IfMatch.ToString();

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            if (!TryParse(ifMatch.Trim(), out RowVersion? expected))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid If-Match header",
                    detail: "If-Match must be one strong entity tag: the quoted version an ETag returned.");
            }

            concurrency.Expected = expected;
        }

        object? result = await next(context);

        // The result has not been written yet, so the header still reaches the response.
        if (concurrency.Current is { } current)
        {
            http.Response.Headers.ETag = $"\"{current.Value}\"";
        }

        return result;
    }

    /// <summary>True for a usable header; <paramref name="version"/> is null for * and weak tags.</summary>
    private static bool TryParse(string value, out RowVersion? version)
    {
        version = null;

        if (value == "*" || value.StartsWith("W/", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Length > 2
            && value[0] == '"'
            && value[^1] == '"'
            && Guid.TryParse(value.AsSpan(1, value.Length - 2), out Guid guid)
            && guid != Guid.Empty)
        {
            version = RowVersion.Create(guid);
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Attach it to every module group**

Add the `ProjectReference` listed under Files, then in `UseSerginWebApiAsync` replace the mapping loop body:
```csharp
        foreach (ISerginWebApiModule webModule in modules.OfType<ISerginWebApiModule>())
        {
            webModule.MapEndpoints(app.MapGroup(webModule.Schema).AddEndpointFilter<ExpectedVersionEndpointFilter>());
        }
```
Add `using Sergin.SharedKernel.Presentation.WebApi.Concurrency;`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet build Sergin.MeterMinder.slnx` then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ExpectedVersionEndpointFilterTests"`
Expected: build 0 warnings; all PASS.

- [ ] **Step 6: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Map If-Match and ETag onto ConcurrencyContext for WebApi module groups"
git add tests/Sergin.MeterMinder.IntegrationTests.All src/SharedKernel Directory.Packages.props
git commit -m "Test the expected-version endpoint filter"
```

---

### Task 9: gRPC carries the version

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Sergin.SharedKernel.Presentation.Grpc.csproj` (add `<ProjectReference Include="..\Sergin.SharedKernel.Application\Sergin.SharedKernel.Application.csproj" />`)
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Concurrency/ConcurrencyMetadata.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Concurrency/ConcurrencyClientInterceptor.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Grpc/Concurrency/ConcurrencyServerInterceptor.cs`
- Modify: `tests/Sergin.MeterMinder.IntegrationTests.All/Devices/DeviceGrpcRoundTripTests.cs`

**Interfaces:**
- Consumes: `ConcurrencyContext` (Task 1); `Versioned<T>`, the `ConcurrencyContext` handler dependency and the stub (Task 5).
- Produces: `ConcurrencyMetadata.ExpectedVersionKey = "sergin-expected-version"`, `ConcurrencyMetadata.VersionKey = "sergin-version"`; `ConcurrencyClientInterceptor(ConcurrencyContext)`; `ConcurrencyServerInterceptor(ConcurrencyContext)`.

Design note: the spec put the client side in `RemoteForwardingHandler`. `IRemoteInvoker<,>.InvokeAsync` has no way to take call metadata, and widening it breaks every invoker. A client `Interceptor` on the invoker's channel does the same job with no signature change, so that is what this task builds; Task 10 updates the spec.

- [ ] **Step 1: Write the failing round-trip test**

In `DeviceGrpcRoundTripTests`:
1. Add a recorder and a behavior that captures what the server's pipeline saw, as nested types at the bottom of the class:
```csharp
    private sealed class SeenExpected
    {
        public RowVersion? Value { get; set; }
    }

    private sealed class CaptureExpectedBehavior<TRequest, TResponse>(ConcurrencyContext concurrency, SeenExpected seen)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            seen.Value = concurrency.Expected;
            return next(cancellationToken);
        }
    }
```
2. In `InitializeAsync`, change `builder.Services.AddGrpc();` to `builder.Services.AddGrpc(options => options.Interceptors.Add<ConcurrencyServerInterceptor>());`, add `builder.Services.AddSingleton(seenExpected);` (a new field `private readonly SeenExpected seenExpected = new();`), and add the behavior to the server's `AddMediatR`:
```csharp
        builder.Services.AddMediatR(o =>
        {
            o.RegisterServicesFromAssembly(DeviceManagementApplicationAssemblyReference.Assembly);
            o.AddOpenBehavior(typeof(CaptureExpectedBehavior<,>));
        });
```
3. Rename `BuildRemoteSender` to `BuildRemoteProvider`, returning the `ServiceProvider` instead of the `ISender`, and give the client the interceptor:
```csharp
        services.AddScoped<ConcurrencyContext>();
        services.AddScoped(provider => new DeviceService.DeviceServiceClient(
            channel.Intercept(new ConcurrencyClientInterceptor(provider.GetRequiredService<ConcurrencyContext>()))));
```
(replacing `services.AddSingleton(new DeviceService.DeviceServiceClient(channel));`). The three existing tests call `BuildRemoteProvider(...).GetRequiredService<ISender>()`.
4. Add:
```csharp
    [Fact]
    public async Task RemoteDispatch_CarriesTheVersion_BothWays()
    {
        var deviceGuid = Guid.CreateVersion7();
        RowVersion stored = RowVersion.Create();
        repository.Add(new DeviceIntenralId(deviceGuid), new DeviceQueryResponse(deviceGuid, "DEV-7", Guid.CreateVersion7(), "XYZ-7"), stored);
        RowVersion sent = RowVersion.Create();

        using ServiceProvider provider = BuildRemoteProvider([DevicesReadPermission]);
        using IServiceScope scope = provider.CreateScope();
        ConcurrencyContext concurrency = scope.ServiceProvider.GetRequiredService<ConcurrencyContext>();
        concurrency.Expected = sent;

        ErrorOr<DeviceQueryResponse> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetDeviceByIdQueryCommand(deviceGuid));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        Assert.Equal(sent, seenExpected.Value);
        Assert.Equal(stored, concurrency.Current);
    }
```
Add `using Grpc.Core.Interceptors;` and `using Sergin.SharedKernel.Presentation.Grpc.Concurrency;`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceGrpcRoundTripTests"`
Expected: build FAILS with `CS0246` on `ConcurrencyClientInterceptor`/`ConcurrencyServerInterceptor`.

- [ ] **Step 3: Add the metadata keys and both interceptors**

`Concurrency/ConcurrencyMetadata.cs`:
```csharp
namespace Sergin.SharedKernel.Presentation.Grpc.Concurrency;

/// <summary>
/// The gRPC metadata keys carrying <see cref="Application.Concurrency.ConcurrencyContext"/> across a Remote
/// call: the expected version as a request header, the version the call read or wrote as a response trailer (a
/// trailer, because the server only knows it once the handler has run).
/// </summary>
public static class ConcurrencyMetadata
{
    public const string ExpectedVersionKey = "sergin-expected-version";
    public const string VersionKey = "sergin-version";
}
```

`Concurrency/ConcurrencyClientInterceptor.cs`:
```csharp
using Grpc.Core;
using Grpc.Core.Interceptors;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Presentation.Grpc.Concurrency;

/// <summary>
/// The caller's half of carrying a version over a Remote call. Put on the channel a module's invoker calls
/// through (<c>channel.Intercept(new ConcurrencyClientInterceptor(concurrency))</c>, with the caller scope's
/// context), so no <see cref="Dispatching.IRemoteInvoker{TRequest, TResponse}"/> changes: it sends
/// <see cref="ConcurrencyContext.Expected"/> as request metadata and copies the version trailer back into
/// <see cref="ConcurrencyContext.Current"/>.
/// </summary>
public sealed class ConcurrencyClientInterceptor(ConcurrencyContext concurrency) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        Metadata headers = context.Options.Headers ?? [];

        if (concurrency.Expected is { } expected)
        {
            headers.Add(ConcurrencyMetadata.ExpectedVersionKey, expected.Value.ToString());
        }

        AsyncUnaryCall<TResponse> call = continuation(
            request,
            new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithHeaders(headers)));

        return new AsyncUnaryCall<TResponse>(
            ReadVersionAsync(call),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> ReadVersionAsync<TResponse>(AsyncUnaryCall<TResponse> call)
    {
        TResponse response = await call.ResponseAsync.ConfigureAwait(false);

        if (Guid.TryParse(call.GetTrailers().GetValue(ConcurrencyMetadata.VersionKey), out Guid version) && version != Guid.Empty)
        {
            concurrency.Current = RowVersion.Create(version);
        }

        return response;
    }
}
```

`Concurrency/ConcurrencyServerInterceptor.cs`:
```csharp
using Grpc.Core;
using Grpc.Core.Interceptors;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Presentation.Grpc.Concurrency;

/// <summary>
/// The serving host's half: seeds the call scope's <see cref="ConcurrencyContext.Expected"/> from request
/// metadata before the service sends into its pipeline, and returns <see cref="ConcurrencyContext.Current"/>
/// as a trailer. Register with <c>AddGrpc(o =&gt; o.Interceptors.Add&lt;ConcurrencyServerInterceptor&gt;())</c>;
/// Grpc.AspNetCore builds it from the call's scope, so it shares the context the service's ISender uses.
/// </summary>
public sealed class ConcurrencyServerInterceptor(ConcurrencyContext concurrency) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        if (context.RequestHeaders.GetValue(ConcurrencyMetadata.ExpectedVersionKey) is { } sent)
        {
            concurrency.Expected = Guid.TryParse(sent, out Guid expected) && expected != Guid.Empty
                ? RowVersion.Create(expected)
                : throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    $"{ConcurrencyMetadata.ExpectedVersionKey} must be a non-empty GUID."));
        }

        TResponse response = await continuation(request, context).ConfigureAwait(false);

        if (concurrency.Current is { } current)
        {
            context.ResponseTrailers.Add(ConcurrencyMetadata.VersionKey, current.Value.ToString());
        }

        return response;
    }
}
```
If `Metadata` has no collection-expression support (CS9174 on `[]`), use `new Metadata()`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~DeviceGrpcRoundTripTests"`
Expected: all four PASS.

- [ ] **Step 5: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Carry the expected and current version over gRPC with client and server interceptors"
git add tests/Sergin.MeterMinder.IntegrationTests.All src/SharedKernel
git commit -m "Test the version round trip over gRPC"
```

---

### Task 10: Documentation, full verification, PRs

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`
- Modify: `.claude/CLAUDE.md`
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-29-optimistic-concurrency-design.md`

- [ ] **Step 1: Update the spec to what was built**

Set **Status** to `Implemented on feat/optimistic-concurrency (host) and feat/optimistic-concurrency (Sergin.SharedKernel)`. In section 3's **gRPC** paragraph, replace the `RemoteForwardingHandler` sentence with: the client side is `ConcurrencyClientInterceptor`, put on the invoker's channel, because `IRemoteInvoker<,>` cannot take metadata without a breaking change; the version returns as the `sergin-version` response **trailer**. In section 1, replace "the localisation resources gain both codes' `.title` and detail entries" with "there are no resource files: `DefaultLocalizer` answers the key".

- [ ] **Step 2: Update the three CLAUDE.md files**

Root `.claude/CLAUDE.md`:
- In the "Domain events" bullet, delete "and `RowVersion` exists for optimistic concurrency, but no aggregate carries one today" (end the sentence after the guard-clause clause).
- In the "Aggregate configuration" bullet, replace "Concurrency and tenant scoping are not built on this mechanism yet; the builder grows one method each when they are — don't add a placeholder now." with "Tenant scoping is not built on this mechanism yet; the builder grows one method when it is — don't add a placeholder now."
- Add a new Cross-cutting bullet after "Aggregate configuration":
  "- **Optimistic concurrency**: `builder.Versioned()` gives an aggregate root a `row_version uuid` concurrency token (children none; a child-only change still moves the root's version). The version never appears on a command or response record: it travels in the scoped `ConcurrencyContext` (`Expected` in, `Current` out). `[RequiresExpectedVersion]` on a command makes a missing version a 428 (`VersionErrors.Required`); a mismatch is a 412 (`VersionErrors.Stale`) and nothing is saved. `ExpectedVersionInterceptor` (first in the chain, so a domain-event handler's changes are bumped but not checked) and `RowVersionBumpInterceptor` (last) do the work; `ExpectedVersionPipelineBehavior` sits between the permission and validation behaviors. A GetOne handler returns the version by setting `ConcurrencyContext.Current` from its repository's `Versioned<T>`. Blazor pages load and write through `ISerginDispatcher.SendVersionedAsync` and reload on a stale answer; WebApi groups carry `If-Match`/`ETag` through `ExpectedVersionEndpointFilter`; gRPC through `ConcurrencyClientInterceptor`/`ConcurrencyServerInterceptor`. Turning it on means a migration in the same PR (`AddRowVersionColumns` is the reference: add nullable, backfill with `gen_random_uuid()`, then `NOT NULL`). Versioned today: `Device`, `Manufacturer` (with `DeviceModel`); guarded: `DeleteDevice`, `DeleteManufacturer`, `AddDeviceModel`. A test that sends a guarded command for another reason uses `VersionedDispatch.SendAt…VersionAsync`. Design: `docs/superpowers/specs/2026-09-29-optimistic-concurrency-design.md`."
- In "MediatR pipeline behaviors", insert `ExpectedVersionPipelineBehavior` as item 2 and renumber Validation to 3.
- In "Testing" list, add a `Concurrency/` line naming the test classes from Tasks 1–6 and 8.
- In the "Blazor UI conventions" first bullet's list of sends, add: "Versioned send (detail pages that write): `await Dispatcher.SendVersionedAsync(request, version)` → `VersionedResult<TResponse>` (`Result`, `Version`)."

`src/Modules/DeviceManagement/CLAUDE.md`: note that `Device` and `Manufacturer` are `Versioned()`, the three guarded commands, and that `GetDeviceById`/`GetManufacturerById` read `row_version` through a Dapper split on `rowVersion`.

`src/SharedKernel/.claude/CLAUDE.md`: document `ConcurrencyContext`, `RequiresExpectedVersionAttribute`, `VersionErrors`, `ExpectedVersionPipelineBehavior`, `Versioned()`, `RowVersionColumns`, both interceptors and their place in the chain, `SendVersionedAsync`, `ExpectedVersionEndpointFilter`, the gRPC interceptors, and the two new `ProtoErrorType` values, each next to its existing sibling (audit/soft delete, dispatcher, WebApi, gRPC sections).

- [ ] **Step 3: Full verification**

Run:
```bash
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
```
Expected: build 0 warnings, 0 errors; every test PASS. Then, from `src/SharedKernel`, `dotnet build Sergin.SharedKernel.slnx` must also succeed on its own.

- [ ] **Step 4: Refresh the graph**

Run: `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py` (only if `graphify-out/graph.json` exists in the worktree).

- [ ] **Step 5: Commit, push, open PRs**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "Document optimistic concurrency"
git -C src/SharedKernel push -u origin feat/optimistic-concurrency
git add -A
git commit -m "Document optimistic concurrency and bump SharedKernel"
git push -u origin feat/optimistic-concurrency
```
Open the Sergin.SharedKernel PR first (`gh pr create -R poursh/Sergin.SharedKernel`), then the host PR, whose description links it and says it must merge after it. After the SharedKernel PR merges, point the submodule at its merge commit on `main` and push that bump to the host branch before merging the host PR.
