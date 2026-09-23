# Aggregate Configuration and Audit Stamps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-aggregate `IAggregateConfiguration<T>` classes that switch platform features on for an entity type, with audit stamps (`created_at_utc`/`created_by`/`modified_at_utc`/`modified_by`) as the first feature, enabled for `Device`, `Manufacturer` and `DeviceModel`.

**Architecture:** An `AggregateFeatureRegistry` (SharedKernel.Application) is built by scanning for configuration classes. Each module `DbContext` names its registry through a virtual `AggregateFeatures` property on `SerginDbContext`, and an EF model-finalizing convention turns it into shadow properties. A `SaveChangesInterceptor` stamps them from `IUserContext` and `IDateTimeProvider`. A startup guard checks the registry built from every module's `ApplicationAssembly` against the EF models.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit + Testcontainers (integration only).

**Spec:** `docs/superpowers/specs/2026-09-23-aggregate-configuration-audit-design.md`

## Global Constraints

- Work in the worktree `C:\@factory\Sergin\Sergin.MeterMinder.wt-aggregate-audit` (branch `feature/aggregate-configuration-audit`). Submodules are already initialised there.
- SharedKernel code lives in the submodule `src/SharedKernel` (its own repo). Commit SharedKernel changes **inside the submodule** on a branch `feature/aggregate-configuration-audit`; commit MeterMinder changes in the worktree root. Never commit SharedKernel files from the root.
- `TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer: any analyzer warning fails the build. Public methods null-check reference parameters (`ArgumentNullException.ThrowIfNull`); `switch` statements carry a `default`. Fix any analyzer finding in the code you wrote rather than suppressing it.
- File-scoped namespaces everywhere (IDE0161 is an error), including hand-edited migrations.
- Central Package Management: no `Version` on `PackageReference`. No new packages are needed by this plan.
- Column names, verbatim: `created_at_utc` (`timestamptz NOT NULL`), `created_by` (`uuid NOT NULL`), `modified_at_utc` (`timestamptz NULL`), `modified_by` (`uuid NULL`).
- Backfill actor id, verbatim: `01920000-0000-7000-8000-00000000000f` (the outbox relay identity, `RelayUserContext.Id`).
- Dev user id in the test host (`Sergin:DevUser:Id`): `01920000-0000-7000-8000-000000000001`.
- Tests need Docker running (Testcontainers). If `docker info` fails, launch Docker Desktop and poll `docker info` until it answers.
- Commit messages: plain English, no Claude co-author trailer (repo rule in CLAUDE.md overrides).
- Build: `dotnet build Sergin.MeterMinder.slnx` from the worktree root. Tests: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "<filter>"`.

---

### Task 0: Branch the SharedKernel submodule

**Files:** none.

- [ ] **Step 1: Create the submodule branch**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder.wt-aggregate-audit/src/SharedKernel
git checkout -b feature/aggregate-configuration-audit
git status -sb
```
Expected: `## feature/aggregate-configuration-audit`.

- [ ] **Step 2: Confirm the baseline builds**

Run: `dotnet build Sergin.MeterMinder.slnx` (worktree root)
Expected: `Build succeeded.` with 0 warnings.

---

### Task 1: Configuration contract and registry

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/IAggregateConfiguration.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatures.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureBuilder.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureRegistry.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (register the registry)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureRegistryTests.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateConfigurationTestTypes.cs`

**Interfaces:**
- Produces:
  - `interface IAggregateConfiguration<TEntity> where TEntity : class, IEntity { void Configure(AggregateFeatureBuilder<TEntity> builder); }`
  - `sealed record AggregateFeatures(bool Audited) { static AggregateFeatures None }`
  - `abstract class AggregateFeatureBuilder` (non-generic base, internal `Features`), `sealed class AggregateFeatureBuilder<TEntity> : AggregateFeatureBuilder` with `AggregateFeatureBuilder<TEntity> Audited()`
  - `sealed class AggregateFeatureRegistry` with `static Empty`, `static FromAssemblies(IEnumerable<Assembly>)`, `static FromConfigurationTypes(IEnumerable<Type>)`, `AggregateFeatures For(Type)`, `IReadOnlyCollection<Type> ConfiguredTypes`
  - A singleton `AggregateFeatureRegistry` in DI, built from all local modules' `ApplicationAssembly`.
  - Namespace for all: `Sergin.SharedKernel.Application.Aggregates`.

- [ ] **Step 1: Write the test-only types**

`tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateConfigurationTestTypes.cs`:

```csharp
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// An entity no DbContext maps, used by the registry and guard tests. Never scanned by a host: the
/// registry is only ever built from a module's ApplicationAssembly, not from this test assembly, so
/// the deliberately broken configurations below cannot reach the real host.
/// </summary>
internal sealed class UnmappedEntity : Entity<Guid>;

internal sealed class UnmappedEntityAggregateConfiguration : IAggregateConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder) => builder.Audited();
}

internal sealed class DuplicateUnmappedEntityAggregateConfiguration : IAggregateConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder) => builder.Audited();
}

internal sealed class NeedsServiceAggregateConfiguration(IServiceProvider services) : IAggregateConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        builder.Audited();
    }
}

internal sealed class NoFeaturesAggregateConfiguration : IAggregateConfiguration<UnmappedEntity>
{
    public void Configure(AggregateFeatureBuilder<UnmappedEntity> builder)
    {
    }
}
```

- [ ] **Step 2: Write the failing registry tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureRegistryTests.cs`:

```csharp
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The registry on its own: what a configuration class declares is what For() answers, an unconfigured
/// type answers None, and the two declaration mistakes the scan can see — two configurations for one type,
/// and a configuration that wants constructor arguments — fail with the offending type names.
/// </summary>
public sealed class AggregateFeatureRegistryTests
{
    [Fact]
    public void For_ConfiguredType_ReturnsDeclaredFeatures()
    {
        AggregateFeatureRegistry registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(UnmappedEntityAggregateConfiguration)]);

        Assert.True(registry.For(typeof(UnmappedEntity)).Audited);
        Assert.Equal(typeof(UnmappedEntity), Assert.Single(registry.ConfiguredTypes));
    }

    [Fact]
    public void For_UnconfiguredType_ReturnsNone()
    {
        Assert.Equal(AggregateFeatures.None, AggregateFeatureRegistry.Empty.For(typeof(UnmappedEntity)));
    }

    [Fact]
    public void Configure_DeclaringNothing_StillRegistersTheTypeWithNoFeatures()
    {
        AggregateFeatureRegistry registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(NoFeaturesAggregateConfiguration)]);

        Assert.Equal(AggregateFeatures.None, registry.For(typeof(UnmappedEntity)));
    }

    [Fact]
    public void TwoConfigurationsForOneType_Throw_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromConfigurationTypes(
                [typeof(UnmappedEntityAggregateConfiguration), typeof(DuplicateUnmappedEntityAggregateConfiguration)]));

        Assert.Contains(typeof(UnmappedEntity).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UnmappedEntityAggregateConfiguration), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateUnmappedEntityAggregateConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationWithConstructorArguments_Throws_NamingIt()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(NeedsServiceAggregateConfiguration)]));

        Assert.Contains(nameof(NeedsServiceAggregateConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromAssemblies_FindsConfigurationsInTheAssembly()
    {
        // The test assembly holds a duplicate on purpose, so scanning it must refuse — which proves the
        // scan found both classes without the test depending on their exact count.
        Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureRegistry.FromAssemblies([typeof(AggregateFeatureRegistryTests).Assembly]));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL to compile. `CS0234`/`CS0246`: the namespace `Sergin.SharedKernel.Application.Aggregates` does not exist.

- [ ] **Step 4: Write the contract**

`src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/IAggregateConfiguration.cs`:

```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Application.Aggregates;

/// <summary>
/// Declares which platform features apply to one entity type, the way an EF
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> declares its mapping. A module writes one class per configured
/// type in its <c>.Application</c> project, named <c>&lt;Type&gt;AggregateConfiguration</c> so it cannot collide
/// with the EF <c>&lt;Type&gt;Configuration</c>. Constrained to <see cref="IEntity"/>, not
/// <see cref="IAggregateRoot"/>, so a child entity can be configured on its own.
/// <para>
/// A configuration is a declaration, not a service: it is created with <c>Activator</c>, never through
/// DI, so it must have a parameterless constructor.
/// </para>
/// </summary>
public interface IAggregateConfiguration<TEntity>
    where TEntity : class, IEntity
{
    void Configure(AggregateFeatureBuilder<TEntity> builder);
}
```

`src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatures.cs`:

```csharp
namespace Sergin.SharedKernel.Application.Aggregates;

/// <summary>The features one entity type has switched on. Grows a flag per feature.</summary>
public sealed record AggregateFeatures(bool Audited)
{
    public static AggregateFeatures None { get; } = new(Audited: false);
}
```

`src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureBuilder.cs`:

```csharp
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Application.Aggregates;

/// <summary>
/// The non-generic half of the builder, so the registry can read what a configuration declared without
/// reflecting over the generic type.
/// </summary>
public abstract class AggregateFeatureBuilder
{
    private protected AggregateFeatureBuilder()
    {
    }

    internal AggregateFeatures Features { get; private protected set; } = AggregateFeatures.None;
}

public sealed class AggregateFeatureBuilder<TEntity> : AggregateFeatureBuilder
    where TEntity : class, IEntity
{
    internal AggregateFeatureBuilder()
    {
    }

    /// <summary>Adds created/modified stamps to the entity's table. Calling it twice is harmless.</summary>
    public AggregateFeatureBuilder<TEntity> Audited()
    {
        Features = Features with { Audited = true };
        return this;
    }
}
```

- [ ] **Step 5: Write the registry**

`src/SharedKernel/Sergin.SharedKernel.Application/Aggregates/AggregateFeatureRegistry.cs`:

```csharp
using System.Reflection;
using Sergin.SharedKernel.Domain;

namespace Sergin.SharedKernel.Application.Aggregates;

/// <summary>
/// What every <see cref="IAggregateConfiguration{TEntity}"/> in a set of assemblies declared, keyed by
/// entity type. Built twice, for two readers: each module <c>DbContext</c> builds its own from its module's
/// <c>.Application</c> assembly (through <c>SerginDbContext.AggregateFeatures</c>, which also works at
/// design time, where there is no DI container), and <c>AddSerginCore</c> registers one built from every
/// local module for the startup guard. Both builds refuse two configurations for one type and a
/// configuration without a parameterless constructor.
/// </summary>
public sealed class AggregateFeatureRegistry
{
    private readonly IReadOnlyDictionary<Type, AggregateFeatures> features;

    private AggregateFeatureRegistry(IReadOnlyDictionary<Type, AggregateFeatures> features)
    {
        this.features = features;
    }

    public static AggregateFeatureRegistry Empty { get; } = new(new Dictionary<Type, AggregateFeatures>());

    public IReadOnlyCollection<Type> ConfiguredTypes => [.. features.Keys];

    public AggregateFeatures For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return features.GetValueOrDefault(entityType, AggregateFeatures.None);
    }

    public static AggregateFeatureRegistry FromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromConfigurationTypes(
            assemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsConfigurationType));
    }

    public static AggregateFeatureRegistry FromConfigurationTypes(IEnumerable<Type> configurationTypes)
    {
        ArgumentNullException.ThrowIfNull(configurationTypes);

        List<(Type EntityType, Type ConfigurationType, AggregateFeatures Features)> declared = [];

        foreach (Type configurationType in configurationTypes)
        {
            foreach (Type closedInterface in configurationType.GetInterfaces().Where(IsClosedConfigurationInterface))
            {
                Type entityType = closedInterface.GetGenericArguments()[0];
                declared.Add((entityType, configurationType, Run(configurationType, closedInterface, entityType)));
            }
        }

        string[] duplicates =
        [
            .. declared
                .GroupBy(item => item.EntityType)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key.FullName} ({string.Join(", ", group.Select(item => item.ConfigurationType.FullName))})")
        ];

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"More than one aggregate configuration is declared for: {string.Join("; ", duplicates)}. "
                + "Declare each type's features in exactly one IAggregateConfiguration<T>.");
        }

        return new AggregateFeatureRegistry(declared.ToDictionary(item => item.EntityType, item => item.Features));
    }

    private static AggregateFeatures Run(Type configurationType, Type closedInterface, Type entityType)
    {
        object configuration;

        try
        {
            configuration = Activator.CreateInstance(configurationType, nonPublic: true)!;
        }
        catch (MissingMethodException exception)
        {
            throw new InvalidOperationException(
                $"Aggregate configuration {configurationType.FullName} must have a parameterless constructor: "
                + "aggregate configurations are declarations, created without dependency injection.",
                exception);
        }

        var builder = (AggregateFeatureBuilder)Activator.CreateInstance(
            typeof(AggregateFeatureBuilder<>).MakeGenericType(entityType), nonPublic: true)!;

        closedInterface
            .GetMethod(nameof(IAggregateConfiguration<IEntity>.Configure))!
            .Invoke(configuration, [builder]);

        return builder.Features;
    }

    private static bool IsConfigurationType(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && type.GetInterfaces().Any(IsClosedConfigurationInterface);

    private static bool IsClosedConfigurationInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAggregateConfiguration<>);
}
```

- [ ] **Step 6: Register the startup registry in `AddSerginCore`**

In `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`, add `using Sergin.SharedKernel.Application.Aggregates;` and insert this right after the `foreach` that registers translators and validators (before `string connectionString = ...`):

```csharp
        // Every local module's aggregate configurations, for the startup guard that checks them against the
        // EF models (AggregateFeatureGuard). Building it here also runs the registry's own checks — two
        // configurations for one type, a configuration without a parameterless constructor — at composition,
        // in every environment. Each module DbContext builds its own copy for the EF model; see
        // SerginDbContext.AggregateFeatures for why that one cannot come from DI.
        builder.Services.AddSingleton(
            AggregateFeatureRegistry.FromAssemblies(localModules.Select(module => module.ApplicationAssembly)));
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~AggregateFeatureRegistryTests"`
Expected: 6 passed.

If `ConfigurationWithConstructorArguments_Throws_NamingIt` fails because `Activator.CreateInstance(..., nonPublic: true)` surfaces a different exception type for a missing constructor on your runtime, catch that type too. The message must still name the configuration type.

- [ ] **Step 8: Commit (submodule, then root)**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Application/Aggregates Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs
git commit -m "Add IAggregateConfiguration<T> and the aggregate feature registry"
cd ../..
git add tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates src/SharedKernel
git commit -m "Test the aggregate feature registry"
```

---

### Task 2: Audit convention and stamping in SharedKernel

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AuditColumns.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AggregateFeatureConvention.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/SerginDbContext.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Interceptors/AuditStampInterceptor.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ModuleDbContextExtensions.cs:29` (attach the interceptor)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (register it)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Audit/TestAuditDbContext.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Audit/AuditStampTests.cs`

No real module opts in yet, so the whole existing suite keeps passing. The feature is proven on a test-only aggregate, which is also the only way to exercise the Modified path, since DeviceManagement has no update slice.

**Interfaces:**
- Consumes: `AggregateFeatureRegistry`, `AggregateFeatureRegistry.FromConfigurationTypes` (Task 1); `UserContextAccessor` (existing, `Sergin.SharedKernel.Application.Securities.Users`); `IDateTimeProvider` (existing, `DateTime UtcNow`).
- Produces:
  - `public static class AuditColumns` (namespace `Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates`) with consts `AuditedAnnotation = "Sergin:Audited"`, `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy` (property names), `CreatedAtUtcColumn = "created_at_utc"`, `CreatedByColumn = "created_by"`, `ModifiedAtUtcColumn = "modified_at_utc"`, `ModifiedByColumn = "modified_by"`, and `static bool IsAudited(IReadOnlyEntityType entityType)`.
  - `protected virtual AggregateFeatureRegistry AggregateFeatures` on `SerginDbContext` (default `AggregateFeatureRegistry.Empty`).
  - `internal sealed class AuditStampInterceptor(IUserContext user, IDateTimeProvider clock) : SaveChangesInterceptor`, registered scoped. It is attached to every module context after `EventDispatcherInterceptor`.
  - Test-side: `AuditStampTests.AuditRow` record and `AuditStampTests.AssertWithin` (both `internal`), which Task 3 reuses.

- [ ] **Step 1: Write the test module**

`tests/Sergin.MeterMinder.IntegrationTests.All/Audit/TestAuditDbContext.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// A test-only module with one audited aggregate that can be renamed, because DeviceManagement has no
/// update slice yet and the Modified path needs one. Mapped into its own test_audit schema through the real
/// AddModuleDbContext, so the interceptors under test are the host's own. Its registry comes from an
/// explicit type list rather than an assembly scan: this test assembly also holds deliberately broken
/// configurations for the registry tests.
/// </summary>
internal interface ITestAuditDbContext : IDbContext;

internal interface ITestAuditUnitOfWork : IUnitOfWork;

internal sealed class TestAuditDbContext(DbContextOptions<TestAuditDbContext> options)
    : SerginDbContext(options), ITestAuditDbContext, ITestAuditUnitOfWork
{
    public const string Schema = "test_audit";

    public DbSet<AuditedThing> Things => Set<AuditedThing>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(AuditedThingAggregateConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditedThing>(thing =>
        {
            thing.ToTable("things");
            thing.HasKey(x => x.Id);
            thing.Property(x => x.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class AuditedThing : AggregateRoot<Guid>
{
    private AuditedThing()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <param name="spawnSibling">Raise an event whose handler adds a second thing on the same save.</param>
    public static AuditedThing Create(string name, bool spawnSibling = false)
    {
        AuditedThing thing = new() { Id = Guid.CreateVersion7(), Name = name };

        if (spawnSibling)
        {
            thing.Raise(new AuditedThingCreated(Guid.CreateVersion7(), DateTime.UtcNow, name));
        }

        return thing;
    }

    public void Rename(string name) => Name = name;
}

internal sealed class AuditedThingAggregateConfiguration : IAggregateConfiguration<AuditedThing>
{
    public void Configure(AggregateFeatureBuilder<AuditedThing> builder) => builder.Audited();
}

internal sealed record AuditedThingCreated(Guid Id, DateTime OccurredOnUtc, string Name) : IDomainEvent;

/// <summary>Adds a sibling on the originating save, so the test can check handler-added entities get stamped.</summary>
internal sealed class SiblingSpawningHandler(ITestAuditDbContext context)
    : INotificationHandler<DomainEventNotification<AuditedThingCreated>>
{
    public Task Handle(DomainEventNotification<AuditedThingCreated> notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        context.Set<AuditedThing>().Add(AuditedThing.Create($"{notification.Event.Name}-sibling"));
        return Task.CompletedTask;
    }
}

internal sealed record TestUserContext(UserId Id) : IUserContext
{
    public string UserName => "audit-test";

    public string FirstName => "Audit";

    public string LastName => "Test";

    public string Email => "audit-test@sergin.local";

    public HashSet<Permission> Permissions { get; } = [];
}
```

Check `DomainEventNotification<T>`'s property name against `src/SharedKernel/Sergin.SharedKernel.Application/Events/`. `IDomainEventHandler` reads `notification.Event`, so it should be `Event`.

- [ ] **Step 2: Write the failing stamp tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Audit/AuditStampTests.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// AuditStampInterceptor end to end: an insert stamps created_* and leaves modified_* NULL, an update stamps
/// modified_* and leaves created_* alone, an entity a domain-event handler adds on the same save is stamped,
/// and the actor is whatever IUserContext the scope resolves — here one seeded through UserContextAccessor,
/// the way the Blazor dispatcher and the outbox relay hand their identity into a scope. Values are read back
/// through EF.Property in a fresh scope, so they come from Postgres, not the change tracker.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditStampTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS test_audit CASCADE; CREATE SCHEMA test_audit;";

    private WebApplicationFactory<Program> auditFactory = default!;

    public async Task InitializeAsync()
    {
        auditFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
        {
            services.AddModuleDbContext<TestAuditDbContext, ITestAuditDbContext, ITestAuditUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestAuditDbContext.Schema);

            services.AddTransient<INotificationHandler<DomainEventNotification<AuditedThingCreated>>, SiblingSpawningHandler>();
        }));

        using IServiceScope scope = auditFactory.Services.CreateScope();
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        // Not EnsureCreatedAsync: it no-ops once the database holds any table. CreateTablesAsync builds this
        // model's tables, audit columns included, regardless.
        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        await auditFactory.DisposeAsync();
    }

    [Fact]
    public async Task Insert_StampsCreated_AndLeavesModifiedNull()
    {
        Guid actor = Guid.CreateVersion7();
        DateTime before = DateTime.UtcNow;

        Guid thingId = await SaveNewThingAsync(actor, "created");

        DateTime after = DateTime.UtcNow;
        AuditRow row = await ReadThingAsync(thingId);

        Assert.Equal(actor, row.CreatedBy);
        AssertWithin(before, after, row.CreatedAtUtc);
        Assert.Null(row.ModifiedAtUtc);
        Assert.Null(row.ModifiedBy);
    }

    [Fact]
    public async Task Update_StampsModified_AndLeavesCreatedAlone()
    {
        Guid creator = Guid.CreateVersion7();
        Guid editor = Guid.CreateVersion7();

        Guid thingId = await SaveNewThingAsync(creator, "original");
        AuditRow afterCreate = await ReadThingAsync(thingId);

        DateTime before = DateTime.UtcNow;

        using (IServiceScope scope = ScopeAs(editor))
        {
            TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            AuditedThing thing = await context.Things.SingleAsync(x => x.Id == thingId);
            thing.Rename("renamed");
            await context.SaveChangesAsync();
        }

        DateTime after = DateTime.UtcNow;
        AuditRow afterUpdate = await ReadThingAsync(thingId);

        Assert.Equal(creator, afterUpdate.CreatedBy);
        Assert.Equal(afterCreate.CreatedAtUtc, afterUpdate.CreatedAtUtc);
        Assert.Equal(editor, afterUpdate.ModifiedBy);
        AssertWithin(before, after, afterUpdate.ModifiedAtUtc!.Value);
    }

    [Fact]
    public async Task EntityAddedByDomainEventHandler_IsStamped()
    {
        Guid actor = Guid.CreateVersion7();
        string name = $"parent-{Guid.CreateVersion7()}";

        using (IServiceScope scope = ScopeAs(actor))
        {
            TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            context.Things.Add(AuditedThing.Create(name, spawnSibling: true));
            await context.SaveChangesAsync();
        }

        using IServiceScope readScope = auditFactory.Services.CreateScope();
        TestAuditDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        Guid siblingCreatedBy = await readContext.Things
            .Where(thing => thing.Name == $"{name}-sibling")
            .Select(thing => EF.Property<Guid>(thing, AuditColumns.CreatedBy))
            .SingleAsync();

        Assert.Equal(actor, siblingCreatedBy);
    }

    // Postgres keeps microseconds and .NET ticks are 100 ns, so allow a millisecond either side.
    internal static void AssertWithin(DateTime before, DateTime after, DateTime actual) =>
        Assert.InRange(actual, before.AddMilliseconds(-1), after.AddMilliseconds(1));

    private IServiceScope ScopeAs(Guid userId)
    {
        IServiceScope scope = auditFactory.Services.CreateScope();

        // Seed before anything resolves the DbContext: the interceptor takes IUserContext when the context
        // is built, and the scoped IUserContext registration prefers a seeded value.
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestUserContext(new UserId(userId));
        return scope;
    }

    private async Task<Guid> SaveNewThingAsync(Guid actor, string name)
    {
        using IServiceScope scope = ScopeAs(actor);
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        AuditedThing thing = AuditedThing.Create(name);
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        return thing.Id;
    }

    private async Task<AuditRow> ReadThingAsync(Guid thingId)
    {
        using IServiceScope scope = auditFactory.Services.CreateScope();
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        return await context.Things
            .Where(thing => thing.Id == thingId)
            .Select(thing => new AuditRow(
                EF.Property<DateTime>(thing, AuditColumns.CreatedAtUtc),
                EF.Property<Guid>(thing, AuditColumns.CreatedBy),
                EF.Property<DateTime?>(thing, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(thing, AuditColumns.ModifiedBy)))
            .SingleAsync();
    }

    internal sealed record AuditRow(DateTime CreatedAtUtc, Guid CreatedBy, DateTime? ModifiedAtUtc, Guid? ModifiedBy);
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL to compile. `Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates` does not exist, and `SerginDbContext` has no `AggregateFeatures` to override.

- [ ] **Step 4: Write `AuditColumns`**

`src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AuditColumns.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// The one spelling of the audit shadow properties and their columns, shared by the convention that adds
/// them, the interceptor that stamps them, and any raw-SQL read that selects them.
/// <c>modified_*</c> is nullable on purpose: a row that was only inserted was not modified.
/// </summary>
public static class AuditColumns
{
    public const string AuditedAnnotation = "Sergin:Audited";

    public const string CreatedAtUtc = "CreatedAtUtc";
    public const string CreatedBy = "CreatedBy";
    public const string ModifiedAtUtc = "ModifiedAtUtc";
    public const string ModifiedBy = "ModifiedBy";

    public const string CreatedAtUtcColumn = "created_at_utc";
    public const string CreatedByColumn = "created_by";
    public const string ModifiedAtUtcColumn = "modified_at_utc";
    public const string ModifiedByColumn = "modified_by";

    public static bool IsAudited(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(AuditedAnnotation) is not null;
    }
}
```

- [ ] **Step 5: Write the convention**

`src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AggregateFeatureConvention.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// Turns a context's <see cref="AggregateFeatureRegistry"/> into model shape. A model-finalizing convention
/// runs once, after OnModelCreating, every IEntityTypeConfiguration and the other conventions, on the
/// complete model, so a module writes no mapping for the audit columns. Column names are set explicitly
/// rather than left to UseSnakeCaseNamingConvention, which is not guaranteed to react to properties added
/// this late; the names are the same either way.
/// </summary>
internal sealed class AggregateFeatureConvention(AggregateFeatureRegistry registry) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!registry.For(entityType.ClrType).Audited)
            {
                continue;
            }

            IConventionEntityTypeBuilder builder = entityType.Builder;

            AddAuditProperty(builder, typeof(DateTime), AuditColumns.CreatedAtUtc, AuditColumns.CreatedAtUtcColumn, required: true);
            AddAuditProperty(builder, typeof(Guid), AuditColumns.CreatedBy, AuditColumns.CreatedByColumn, required: true);
            AddAuditProperty(builder, typeof(DateTime?), AuditColumns.ModifiedAtUtc, AuditColumns.ModifiedAtUtcColumn, required: false);
            AddAuditProperty(builder, typeof(Guid?), AuditColumns.ModifiedBy, AuditColumns.ModifiedByColumn, required: false);

            builder.HasAnnotation(AuditColumns.AuditedAnnotation, true);
        }
    }

    private static void AddAuditProperty(
        IConventionEntityTypeBuilder builder, Type clrType, string name, string column, bool required)
    {
        IConventionPropertyBuilder property = builder.Property(clrType, name)
            ?? throw new InvalidOperationException(
                $"Could not add the audit property {name} to {builder.Metadata.DisplayName()}: "
                + "an explicit mapping already configures a conflicting member of that name.");

        property.IsRequired(required);
        property.HasColumnName(column);
    }
}
```

- [ ] **Step 6: Give `SerginDbContext` the hook**

Replace `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/SerginDbContext.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore;

public abstract class SerginDbContext(DbContextOptions options) : DbContext(options), IDbContext
{
    /// <summary>
    /// The aggregate configurations this context's model applies. A module that opts in overrides it with
    /// <c>AggregateFeatureRegistry.FromAssemblies([&lt;Module&gt;ApplicationAssemblyReference.Assembly])</c>,
    /// expression-bodied so the scan runs only when EF builds the model, once per context type.
    /// <para>
    /// It is a property of the context, not a DI lookup, on purpose: an IDesignTimeDbContextFactory builds
    /// the context with no container, and a registry that came back empty there would make the next
    /// <c>dotnet ef migrations add</c> scaffold DropColumn for every audit column.
    /// </para>
    /// </summary>
    protected virtual AggregateFeatureRegistry AggregateFeatures => AggregateFeatureRegistry.Empty;

    /// <summary>A subclass that overrides this must call base, or its aggregate features are silently dropped.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        AggregateFeatureRegistry features = AggregateFeatures;
        configurationBuilder.Conventions.Add(_ => new AggregateFeatureConvention(features));
    }
}
```

- [ ] **Step 7: Write the interceptor**

`src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Interceptors/AuditStampInterceptor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Times;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Interceptors;

/// <summary>
/// Stamps the audit shadow properties (<see cref="AuditColumns"/>) on every entity type whose model carries
/// them. Added: created_*, with modified_* left NULL. Modified: modified_*, with created_* marked
/// unmodified so nothing can overwrite it. Runs after EventDispatcherInterceptor, so entities a domain-event
/// handler adds or changes on the same save are stamped too. The actor is the scope's IUserContext: the
/// signed-in user through the Blazor dispatcher, the relay identity during outbox delivery.
/// <para>
/// Row-level only: a root is not stamped when only its child entity changed, and raw-SQL writes bypass this.
/// The synchronous path stamps the same way — unlike event dispatch there is nothing asynchronous to refuse.
/// </para>
/// </summary>
internal sealed class AuditStampInterceptor(IUserContext user, IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTime now = clock.UtcNow;
        Guid actor = user.Id.Value;

        // Entries() runs DetectChanges, so a property changed since the last detection is already Modified.
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (!AuditColumns.IsAudited(entry.Metadata))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(AuditColumns.CreatedAtUtc).CurrentValue = now;
                    entry.Property(AuditColumns.CreatedBy).CurrentValue = actor;
                    break;

                case EntityState.Modified:
                    entry.Property(AuditColumns.ModifiedAtUtc).CurrentValue = now;
                    entry.Property(AuditColumns.ModifiedBy).CurrentValue = actor;
                    entry.Property(AuditColumns.CreatedAtUtc).IsModified = false;
                    entry.Property(AuditColumns.CreatedBy).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
```

- [ ] **Step 8: Register and attach it**

In `SerginCoreExtensions.cs`, directly under `builder.Services.AddScoped<EventDispatcherInterceptor>();`:

```csharp
        builder.Services.AddScoped<AuditStampInterceptor>();
```

In `ModuleDbContextExtensions.cs`, replace the `.AddInterceptors(...)` line with:

```csharp
            // Order matters: EF runs interceptors in registration order, and stamping must see whatever the
            // domain-event handlers added or changed during dispatch.
            .AddInterceptors(
                sp.GetRequiredService<EventDispatcherInterceptor>(),
                sp.GetRequiredService<AuditStampInterceptor>()));
```

- [ ] **Step 9: Run the stamp tests, then the whole suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~AuditStampTests"`
Expected: 3 passed.

**If the inserts fail with `null value in column "created_at_utc"`**, the interceptor isn't seeing the annotation: EF dropped it when converting to its read-optimised runtime model. Change `AuditColumns.IsAudited` to `entityType.FindProperty(CreatedAtUtc) is { } property && property.IsShadowProperty()` (add `using Microsoft.EntityFrameworkCore;`), keep setting the annotation as documentation, and re-run.

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: all tests pass. No real module is audited yet, so nothing else changes.

- [ ] **Step 10: Commit (submodule, then root)**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Infrastructure.Data.EFCore Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs
git commit -m "Add audit shadow columns through an aggregate feature convention and stamp them on save"
cd ../..
git add tests/Sergin.MeterMinder.IntegrationTests.All/Audit src/SharedKernel
git commit -m "Test audit stamping on a test-only aggregate"
```

---

### Task 3: DeviceManagement opt-in and the `AddAuditColumns` migration

**Files:**
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/DeviceAggregateConfiguration.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/ManufacturerAggregateConfiguration.cs`
- Create: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/DeviceModels/DeviceModelAggregateConfiguration.cs`
- Modify: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/DeviceManagementDbContext.cs`
- Create (scaffolded, then edited): `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/<timestamp>_AddAuditColumns.cs` (+ `.Designer.cs`, snapshot update)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Audit/AuditColumnsTests.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Audit/DeviceManagementAuditTests.cs`

**Interfaces:**
- Consumes: `AuditColumns`, `SerginDbContext.AggregateFeatures`, `AuditStampTests.AuditRow`/`AssertWithin` (Task 2); `AggregateFeatureRegistry.FromAssemblies` (Task 1).
- Produces: `Device`, `Manufacturer` and `DeviceModel` audited in `dm`.

- [ ] **Step 1: Write the failing model test**

`tests/Sergin.MeterMinder.IntegrationTests.All/Audit/AuditColumnsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// The EF side of the audit feature: the three DeviceManagement types configured Audited() carry the four
/// shadow properties under their snake_case column names with the agreed nullability, and a type nothing
/// configured (the outbox table) carries none. Read off the runtime model, which is what the interceptor
/// and the startup guard see.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditColumnsTests(SerginWebApiFactory<Program> factory)
{
    public static TheoryData<Type, string> AuditedTypes => new()
    {
        { typeof(Device), "device" },
        { typeof(Manufacturer), "manufacturer" },
        { typeof(DeviceModel), "device_model" },
    };

    [Theory]
    [MemberData(nameof(AuditedTypes))]
    public void AuditedType_CarriesTheFourColumns(Type entityType, string table)
    {
        IEntityType mapped = FindEntityType(entityType);

        Assert.Equal(table, mapped.GetTableName());
        Assert.True(AuditColumns.IsAudited(mapped));

        AssertColumn(mapped, AuditColumns.CreatedAtUtc, AuditColumns.CreatedAtUtcColumn, typeof(DateTime), nullable: false);
        AssertColumn(mapped, AuditColumns.CreatedBy, AuditColumns.CreatedByColumn, typeof(Guid), nullable: false);
        AssertColumn(mapped, AuditColumns.ModifiedAtUtc, AuditColumns.ModifiedAtUtcColumn, typeof(DateTime?), nullable: true);
        AssertColumn(mapped, AuditColumns.ModifiedBy, AuditColumns.ModifiedByColumn, typeof(Guid?), nullable: true);
    }

    [Fact]
    public void UnconfiguredType_CarriesNoAuditColumns()
    {
        IEntityType outbox = FindEntityType(typeof(OutboxMessage));

        Assert.False(AuditColumns.IsAudited(outbox));
        Assert.Null(outbox.FindProperty(AuditColumns.CreatedAtUtc));
    }

    private IEntityType FindEntityType(Type entityType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        return Assert.IsAssignableFrom<IEntityType>(context.Model.FindEntityType(entityType));
    }

    private static void AssertColumn(IEntityType entityType, string property, string column, Type clrType, bool nullable)
    {
        IProperty mapped = Assert.IsAssignableFrom<IProperty>(entityType.FindProperty(property));

        Assert.True(mapped.IsShadowProperty(), $"{property} must be a shadow property, not a domain member.");
        Assert.Equal(clrType, mapped.ClrType);
        Assert.Equal(nullable, mapped.IsNullable);
        Assert.Equal(column, mapped.GetColumnName());
    }
}
```

- [ ] **Step 2: Write the failing module stamp test**

`tests/Sergin.MeterMinder.IntegrationTests.All/Audit/DeviceManagementAuditTests.cs`:

```csharp
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// The real module end to end: a manufacturer created through ISerginDispatcher, as a Blazor page does it,
/// is stamped with the dispatcher's user (the configured dev user) and has no modified stamp yet.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementAuditTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    [Fact]
    public async Task CreateManufacturer_IsStampedWithTheDispatchersUser()
    {
        DateTime before = DateTime.UtcNow;

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"audit-{Guid.CreateVersion7()}"), null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        DateTime after = DateTime.UtcNow;

        using IServiceScope readScope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = readScope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();
        ManufacturerId id = new(created.Value.Id);

        AuditStampTests.AuditRow row = await context.Set<Manufacturer>()
            .Where(manufacturer => manufacturer.Id == id)
            .Select(manufacturer => new AuditStampTests.AuditRow(
                EF.Property<DateTime>(manufacturer, AuditColumns.CreatedAtUtc),
                EF.Property<Guid>(manufacturer, AuditColumns.CreatedBy),
                EF.Property<DateTime?>(manufacturer, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(manufacturer, AuditColumns.ModifiedBy)))
            .SingleAsync();

        Assert.Equal(DevUserId, row.CreatedBy);
        AuditStampTests.AssertWithin(before, after, row.CreatedAtUtc);
        Assert.Null(row.ModifiedAtUtc);
        Assert.Null(row.ModifiedBy);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~AuditColumnsTests|FullyQualifiedName~DeviceManagementAuditTests"`
Expected: FAIL. The three `AuditedType_CarriesTheFourColumns` cases fail on `Assert.True(AuditColumns.IsAudited(mapped))`, and the manufacturer query fails because `CreatedAtUtc` isn't a property of `Manufacturer`. `UnconfiguredType_CarriesNoAuditColumns` passes already.

- [ ] **Step 4: Declare the three DeviceManagement configurations**

`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Devices/DeviceAggregateConfiguration.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices;

internal sealed class DeviceAggregateConfiguration : IAggregateConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) => builder.Audited();
}
```

`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/ManufacturerAggregateConfiguration.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;

internal sealed class ManufacturerAggregateConfiguration : IAggregateConfiguration<Manufacturer>
{
    public void Configure(AggregateFeatureBuilder<Manufacturer> builder) => builder.Audited();
}
```

`src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application/Manufacturers/DeviceModels/DeviceModelAggregateConfiguration.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels;

/// <summary>
/// A child entity, configured on its own: adding a model stamps the model's row, not the manufacturer's.
/// </summary>
internal sealed class DeviceModelAggregateConfiguration : IAggregateConfiguration<DeviceModel>
{
    public void Configure(AggregateFeatureBuilder<DeviceModel> builder) => builder.Audited();
}
```

If `using` lines duplicate a global using in the project's `GlobalUsings.cs` (IDE0005 fails the build), remove them.

- [ ] **Step 5: Point `DeviceManagementDbContext` at them**

In `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/DeviceManagementDbContext.cs`, add `using Sergin.SharedKernel.Application.Aggregates;` and, below `InboxMessages`:

```csharp
    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromAssemblies([DeviceManagementApplicationAssemblyReference.Assembly]);
```

- [ ] **Step 6: Build**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: `Build succeeded.`

- [ ] **Step 7: Scaffold the migration**

```bash
dotnet ef migrations add AddAuditColumns \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```
Expected: new `Migrations/<timestamp>_AddAuditColumns.cs` and `.Designer.cs`, updated `DeviceManagementDbContextModelSnapshot.cs`. The design-time factory builds `DeviceManagementDbContext`, so the override from Step 5 applies. Open the scaffolded `.cs` and confirm it adds 12 columns (4 × `device`, `device_model`, `manufacturer`) named in snake_case. **If it adds none, stop:** the override is not reaching the design-time model.

- [ ] **Step 8: Replace the scaffolded migration body**

The scaffold adds `created_*` as `NOT NULL` with `DateTime.MinValue`/`Guid.Empty` defaults. That would fill existing rows with meaningless values. Replace the whole `<timestamp>_AddAuditColumns.cs` with the version below (file-scoped namespace, IDE0161). Keep the class name and the `.Designer.cs` as scaffolded.

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <summary>
/// Adds the audit stamps for the three types DeviceManagement configures Audited(). created_* is added
/// nullable, backfilled, then made NOT NULL. Rows that predate auditing get a stand-in, not their real
/// history: the migration time and the platform's fixed system actor (the outbox relay identity's id).
/// modified_* stays NULL for them — nothing is known to have modified them.
/// </summary>
public partial class AddAuditColumns : Migration
{
    private const string Schema = "dm";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddColumns(migrationBuilder, "device");
        AddColumns(migrationBuilder, "device_model");
        AddColumns(migrationBuilder, "manufacturer");

        migrationBuilder.Sql("UPDATE dm.device SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");
        migrationBuilder.Sql("UPDATE dm.device_model SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");
        migrationBuilder.Sql("UPDATE dm.manufacturer SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");

        RequireCreated(migrationBuilder, "device");
        RequireCreated(migrationBuilder, "device_model");
        RequireCreated(migrationBuilder, "manufacturer");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropColumns(migrationBuilder, "device");
        DropColumns(migrationBuilder, "device_model");
        DropColumns(migrationBuilder, "manufacturer");
    }

    private static void AddColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AddColumn<DateTime>(name: "created_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "created_by", schema: Schema, table: table, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "modified_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "modified_by", schema: Schema, table: table, type: "uuid", nullable: true);
    }

    private static void RequireCreated(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AlterColumn<DateTime>(
            name: "created_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: false,
            oldClrType: typeof(DateTime), oldType: "timestamp with time zone", oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "created_by", schema: Schema, table: table, type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
    }

    private static void DropColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.DropColumn(name: "created_at_utc", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "created_by", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "modified_at_utc", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "modified_by", schema: Schema, table: table);
    }
}
```

- [ ] **Step 9: Verify no model drift remains**

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```
Expected: "No changes have been made to the model since the last migration." If the command needs a database connection and fails on one, skip it. The test run in the next step fails at host start (EF's pending-model-changes check on `Migrate`) if drift remains.

- [ ] **Step 10: Run the new tests, then the whole suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: every test passes, including 4 `AuditColumnsTests` cases and `DeviceManagementAuditTests`. The existing DeviceManagement write tests (`RepositoryRuleTests`, `CommandValidationTests`) now insert audited rows, so they double as regression cover for the interceptor.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/DeviceManagement tests/Sergin.MeterMinder.IntegrationTests.All/Audit
git commit -m "Audit Device, Manufacturer and DeviceModel; add the AddAuditColumns migration"
```

---

### Task 4: Startup guard, configured types versus EF models

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/ModuleDbContextRegistration.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AggregateFeatureGuard.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/ModuleDbContextExtensions.cs` (register the context type)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` (`UseSerginWebUiAsync`, after `ValidateRoutePrefixes`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs` (`UseSerginWebApiAsync`, first statement)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureGuardTests.cs`

**Interfaces:**
- Consumes: `AggregateFeatureRegistry` (Task 1), `AuditColumns.IsAudited` (Task 2).
- Produces: `public sealed record ModuleDbContextRegistration(Type ContextType)`; `public static class AggregateFeatureGuard` with `static void EnsureApplied(IServiceProvider services)` and `static void EnsureApplied(AggregateFeatureRegistry registry, IReadOnlyCollection<IModel> models)`.

- [ ] **Step 1: Write the failing guard tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates/AggregateFeatureGuardTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.UserAccess.Domain.Users;
using Sergin.UserAccess.Infrastructure.Data;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The startup guard that compares declared configurations with the EF models: the real host passes it
/// (the host starting at all proves that — this collection's factory ran it), a configuration for a type no
/// context maps is refused, and so is one whose context never applied it (UserAccess's context has no
/// AggregateFeatures override). Both refusals name the type.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AggregateFeatureGuardTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void RealHost_PassesTheGuard()
    {
        AggregateFeatureGuard.EnsureApplied(factory.Services);
    }

    [Fact]
    public void ConfiguredTypeNoContextMaps_IsRefused()
    {
        AggregateFeatureRegistry registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(UnmappedEntityAggregateConfiguration)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureGuard.EnsureApplied(registry, Models()));

        Assert.Contains(typeof(UnmappedEntity).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("not mapped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredTypeWhoseContextDoesNotApplyIt_IsRefused()
    {
        AggregateFeatureRegistry registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(UserAggregateConfiguration)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureGuard.EnsureApplied(registry, Models()));

        Assert.Contains(typeof(User).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("AggregateFeatures", error.Message, StringComparison.Ordinal);
    }

    private IReadOnlyCollection<IModel> Models()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return
        [
            Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>()).Model,
            Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IUserAccessDbContext>()).Model,
        ];
    }

    private sealed class UserAggregateConfiguration : IAggregateConfiguration<User>
    {
        public void Configure(AggregateFeatureBuilder<User> builder) => builder.Audited();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL to compile. `AggregateFeatureGuard` does not exist.

- [ ] **Step 3: Write the registration record and the guard**

`src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/ModuleDbContextRegistration.cs`:

```csharp
namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// One per context added through AddModuleDbContext, so startup code can enumerate every module's
/// DbContext without knowing their types.
/// </summary>
public sealed record ModuleDbContextRegistration(Type ContextType);
```

`src/SharedKernel/Sergin.SharedKernel.Infrastructure.Data.EFCore/Aggregates/AggregateFeatureGuard.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;

/// <summary>
/// Fails host start when a declared aggregate configuration does not reach an EF model: its type is mapped
/// by no module DbContext (a configuration stranded in the wrong module), or the context that maps it does
/// not apply it (a module context without an AggregateFeatures override). Called by both host bootstraps in
/// every environment, before the Development-only migrate step. Building each context's model needs no
/// database connection.
/// </summary>
public static class AggregateFeatureGuard
{
    public static void EnsureApplied(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AggregateFeatureRegistry registry = services.GetRequiredService<AggregateFeatureRegistry>();

        using IServiceScope scope = services.CreateScope();

        IReadOnlyCollection<IModel> models =
        [
            .. services.GetServices<ModuleDbContextRegistration>()
                .Select(registration => ((DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType)).Model)
        ];

        EnsureApplied(registry, models);
    }

    public static void EnsureApplied(AggregateFeatureRegistry registry, IReadOnlyCollection<IModel> models)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(models);

        List<string> problems = [];

        foreach (Type configured in registry.ConfiguredTypes)
        {
            IEntityType[] mappings = [.. models.Select(model => model.FindEntityType(configured)).OfType<IEntityType>()];

            if (mappings.Length == 0)
            {
                problems.Add($"{configured.FullName} is not mapped by any module DbContext");
                continue;
            }

            if (registry.For(configured).Audited && mappings.Any(mapping => !AuditColumns.IsAudited(mapping)))
            {
                problems.Add(
                    $"{configured.FullName} is configured Audited(), but the DbContext that maps it does not apply "
                    + "its module's aggregate configurations — override SerginDbContext.AggregateFeatures there");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Aggregate configuration does not match the EF model: {string.Join("; ", problems)}.");
        }
    }
}
```

- [ ] **Step 4: Register each context**

In `ModuleDbContextExtensions.AddModuleDbContext`, add `using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;` and, after `services.AddScoped<TIUnitOfWork>(...)`:

```csharp
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext)));
```

- [ ] **Step 5: Call the guard from both bootstraps**

`SerginWebUiExtensions.UseSerginWebUiAsync`: add `using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;` and insert after `ValidateRoutePrefixes(catalog);`:

```csharp
        AggregateFeatureGuard.EnsureApplied(app.Services);
```

`SerginWebApiExtensions.UseSerginWebApiAsync`: same `using`, and make this the first statement of the method:

```csharp
        AggregateFeatureGuard.EnsureApplied(app.Services);
```

If either Hosts project doesn't reach `Sergin.SharedKernel.Infrastructure.Data.EFCore` transitively (CS0246), add a `ProjectReference` to it in that project's `.csproj`.

- [ ] **Step 6: Run the guard tests, then the whole suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~AggregateFeatureGuardTests"`
Expected: 3 passed.

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: all tests pass. The test hosts built through `WithWebHostBuilder` (`TestEventsDbContext`, `TestAuditDbContext`) also pass the guard, because their configurations aren't in any module's `ApplicationAssembly`.

- [ ] **Step 7: Commit (submodule, then root)**

```bash
cd src/SharedKernel
git add Sergin.SharedKernel.Infrastructure.Data.EFCore Sergin.SharedKernel.Hosts.WebUi Sergin.SharedKernel.Hosts.WebApi
git commit -m "Refuse host start when an aggregate configuration does not reach an EF model"
cd ../..
git add tests/Sergin.MeterMinder.IntegrationTests.All/Aggregates src/SharedKernel
git commit -m "Test the aggregate feature startup guard"
```

---

### Task 5: Documentation and graph

**Files:**
- Modify: `src/SharedKernel/.claude/CLAUDE.md`
- Modify: `.claude/CLAUDE.md`
- Modify: `src/Modules/DeviceManagement/CLAUDE.md`

- [ ] **Step 1: SharedKernel `CLAUDE.md`**

Read the file. Where it describes `.Application` and `.Infrastructure.Data.EFCore`, add a paragraph in the file's own style covering:
- The contract, `IAggregateConfiguration<T>` in `Application/Aggregates/`.
- `AggregateFeatureRegistry`: its two builds and why the EF one is per context (design-time factories have no DI).
- `SerginDbContext.AggregateFeatures` and the rule that a `ConfigureConventions` override must call `base`.
- `AggregateFeatureConvention` adding the four shadow columns, with explicit names from `AuditColumns`.
- `AuditStampInterceptor`: registered after `EventDispatcherInterceptor`; Added/Modified semantics; row-level only; raw SQL bypasses it.
- `AggregateFeatureGuard` and `ModuleDbContextRegistration`.
- The three startup refusals.

Point to the spec path.

- [ ] **Step 2: MeterMinder `.claude/CLAUDE.md`**

Add a **"Aggregate configuration"** bullet under "Cross-cutting conventions", after "Validation", covering:
- One `internal sealed class <Type>AggregateConfiguration : IAggregateConfiguration<Type>` per configured type, in `.Application` at the type's root folder (nested for a child entity).
- The `DbContext` override line, copied from `DeviceManagementDbContext`.
- Turning on `Audited()` for a type means a migration in the same PR, hand-edited to add `created_*` nullable, backfill, then require. `AddAuditColumns` is the reference example.
- `modified_*` is `NULL` until the first update.
- Audited today: `Device`, `Manufacturer`, `DeviceModel`. UserAccess is not audited.
- Concurrency and tenant scoping are not built; the builder grows a method each when they are.

In the "Per-module project layering" `.Application` bullet, add `<Type>AggregateConfiguration.cs` to what lives there. In the `.Infrastructure.Data` bullet, add the `AggregateFeatures` override.

Also update the **"Test fixture pattern"/tests list** paragraph: add `Aggregates/` (`AggregateFeatureRegistryTests`, `AggregateFeatureGuardTests`) and `Audit/` (`AuditColumnsTests`, `AuditStampTests`) with one line each.

- [ ] **Step 3: DeviceManagement `CLAUDE.md`**

Add a short section. The three types are audited through `…AggregateConfiguration` classes, `DeviceManagementDbContext` overrides `AggregateFeatures`, and `AddAuditColumns` backfilled existing rows with the system actor `01920000-0000-7000-8000-00000000000f` and the migration time. Those values are stand-ins, not real history.

- [ ] **Step 4: Refresh the graph**

```bash
graphify update .
python .claude/skills/graphify/scripts/graphify_repair.py
```
Expected: both finish without error. `graphify-out/` is gitignored, so nothing to commit.

- [ ] **Step 5: Final full build and test**

Run: `dotnet build Sergin.MeterMinder.slnx` then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: `Build succeeded.` with 0 warnings; all tests pass.

- [ ] **Step 6: Commit (submodule, then root)**

```bash
cd src/SharedKernel
git add .claude/CLAUDE.md
git commit -m "Document aggregate configuration and audit stamps"
cd ../..
git add .claude/CLAUDE.md src/Modules/DeviceManagement/CLAUDE.md src/SharedKernel
git commit -m "Document aggregate configuration and audit stamps; bump SharedKernel"
```

- [ ] **Step 7: Hand off for integration**

Don't push or open PRs without asking. Report the two branches: SharedKernel `feature/aggregate-configuration-audit` and MeterMinder `feature/aggregate-configuration-audit`. The SharedKernel PR has to merge first, and the MeterMinder submodule pointer then needs moving to its merge commit.
