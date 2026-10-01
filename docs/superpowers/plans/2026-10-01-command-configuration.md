# Command Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `[RequiredPermissions]` and `[RequiresExpectedVersion]` attributes with one `ICommandConfiguration<TCommand>` declaration class per request, read from a startup-built registry.

**Architecture:** A new `Commands/Configuration/` folder in `Sergin.SharedKernel.Application` holds the interface, a fluent builder, a `CommandSettings` record and `CommandConfigurationRegistry` (a copy of `AggregateFeatureRegistry`'s shape). `AddSerginCore` registers one `CommandConfigurationSource` per local and remote `ContractsAssembly` and a registry singleton built from all sources; both `Use…Async` bootstraps resolve it so a bad declaration fails host start. The two pipeline behaviors look settings up in the registry instead of reflecting attributes. Every attributed record moves to a configuration class in its `.Application.Contracts` folder.

**Tech Stack:** .NET 10, C# 14, MediatR, xUnit, Testcontainers (Postgres 17). Analyzers gate the build (`TreatWarningsAsErrors`, `AnalysisMode=All`, SonarAnalyzer).

**Spec:** `docs/superpowers/specs/2026-10-01-command-configuration-design.md`

## Global Constraints

- Work in worktree `C:\@factory\Sergin\Sergin.MeterMinder-cmdcfg`, branch `feature/command-configuration`. Submodules are already initialised there.
- SharedKernel and UserAccess are git submodules. Before the first commit inside each, run `git -C src/SharedKernel switch -c feature/command-configuration` and `git -C src/Modules/UserAccess switch -c feature/command-configuration`. Commit submodule changes inside the submodule, then commit the pointer bump in the host repo.
- Never add a `Co-Authored-By: Claude` trailer (project CLAUDE.md overrides the harness default).
- Warnings are errors. File-scoped namespaces. No `@code` blocks. Configuration classes are `internal sealed class` (CA1812 is off in `.editorconfig`).
- Configuration class name: `<RecordName>Configuration`, same folder and namespace as its record, in `.Application.Contracts`.
- v1 builder methods are `RequirePermissions(Permission permission, params Permission[] more)` and `RequireExpectedVersion()` only. No other settings.
- Unconfigured request ⇒ `CommandSettings.None` ⇒ no permission and no version required.
- Pipeline order stays: permission, expected version, validation.

## Review Focus

1. **`AddDeviceModelCommand` has a version requirement but no permission today.** Expected: its configuration calls only `RequireExpectedVersion()`. Do not "fix" it by adding a permission — that would break `AddDeviceModelPage` for the dev user. Task 3 pins it with a registry assertion.
2. **`ProvisionExternalUserCommand` must stay unconfigured** (it runs inside the OIDC callback before any permission exists). Expected: no configuration class; `For()` answers `None`. Task 3 pins it.
3. **A list query is an `IQuery<>` through `ListQuery<T>` → `IListQuery<T>`.** Expected: `RequirePermissions` works on it and `RequireExpectedVersion` on it is refused. Task 1 pins the refusal with a query type; Task 3 pins a list query's permission.
4. **A test-only broken configuration must never reach a real host.** Expected: hosts scan only module `ContractsAssembly`s; tests add explicit types via `CommandConfigurationSource.FromTypes`. Task 2 pins that a bad source fails host start naming the type.
5. **Permission string in non-canonical case** (`"permission.dm.devices.Read"`). Expected: `Permission.Create` kebab-normalises it, same as the attribute did — no behavior change. Not separately tested; noted so a reviewer does not add a stricter check.

---

### Task 1: Configuration mechanism and registry

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/ICommandConfiguration.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationBuilder.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandSettings.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationRegistry.cs`
- Create: `src/SharedKernel/Sergin.SharedKernel.Application/Commands/Configuration/CommandConfigurationSource.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationTestTypes.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationRegistryTests.cs`

**Interfaces:**
- Produces:
  - `ICommandConfiguration<TCommand> where TCommand : IBaseCommand { void Configure(CommandConfigurationBuilder<TCommand> builder); }`
  - `CommandConfigurationBuilder<TCommand>.RequirePermissions(Permission, params Permission[]) → CommandConfigurationBuilder<TCommand>`, `.RequireExpectedVersion() → CommandConfigurationBuilder<TCommand>`
  - `sealed record CommandSettings(IReadOnlyCollection<Permission> RequiredPermissions, bool RequiresExpectedVersion)` with `static CommandSettings None`
  - `CommandConfigurationRegistry.FromConfigurationTypes(IEnumerable<Type>)`, `.FromSources(IEnumerable<CommandConfigurationSource>)`, `.Empty`, `.For(Type) → CommandSettings`, `.ConfiguredTypes`
  - `CommandConfigurationSource.FromAssembly(Assembly)`, `.FromTypes(params Type[])`, `.ConfigurationTypes`

- [ ] **Step 1: Create the submodule branch**

```bash
cd /c/@factory/Sergin/Sergin.MeterMinder-cmdcfg
git -C src/SharedKernel switch -c feature/command-configuration
```

- [ ] **Step 2: Write test types**

`tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationTestTypes.cs`:

```csharp
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// Requests and configurations for the registry tests. Never scanned by a host: hosts read module
/// ContractsAssemblies only, and a test host adds explicit types through CommandConfigurationSource.FromTypes,
/// so the deliberately broken configurations below cannot reach the real host.
/// </summary>
internal sealed record ConfiguredTestCommand : ICommand<Success>;

internal sealed record ConfiguredTestQuery : IQuery<Success>;

internal sealed record UnconfiguredTestCommand : ICommand<Success>;

internal sealed class ConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequirePermissions("permission.test.things.read")
            .RequirePermissions("permission.test.things.update", "permission.test.things.delete")
            .RequireExpectedVersion();
}

internal sealed class ConfiguredTestQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("permission.test.things.read");
}

internal sealed class DuplicateConfiguredTestCommandConfiguration : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder) =>
        builder.RequireExpectedVersion();
}

internal sealed class NeedsServiceCommandConfiguration(IServiceProvider services) : ICommandConfiguration<ConfiguredTestCommand>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestCommand> builder)
    {
        ArgumentNullException.ThrowIfNull(services);
        builder.RequireExpectedVersion();
    }
}

internal sealed class VersionedQueryConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequireExpectedVersion();
}

internal sealed class MalformedPermissionConfiguration : ICommandConfiguration<ConfiguredTestQuery>
{
    public void Configure(CommandConfigurationBuilder<ConfiguredTestQuery> builder) =>
        builder.RequirePermissions("not a permission");
}
```

- [ ] **Step 3: Write failing tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationRegistryTests.cs`:

```csharp
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The registry on its own: what a configuration declares is what For() answers, an unconfigured request
/// answers None, and each declaration mistake the scan can see fails naming the offending type.
/// </summary>
public sealed class CommandConfigurationRegistryTests
{
    [Fact]
    public void For_ConfiguredCommand_ReturnsDeclaredSettings_WithPermissionsAppended()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestCommand));

        string[] expected = ["permission.test.things.read", "permission.test.things.update", "permission.test.things.delete"];

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal(expected, settings.RequiredPermissions.Select(permission => permission.Value));
        Assert.Equal(typeof(ConfiguredTestCommand), Assert.Single(registry.ConfiguredTypes));
    }

    [Fact]
    public void For_ConfiguredQuery_ReturnsItsPermission_AndNoVersion()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestQueryConfiguration)]);

        CommandSettings settings = registry.For(typeof(ConfiguredTestQuery));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal((Permission)"permission.test.things.read", Assert.Single(settings.RequiredPermissions));
    }

    [Fact]
    public void For_UnconfiguredRequest_ReturnsNone()
    {
        var registry = CommandConfigurationRegistry.FromConfigurationTypes([typeof(ConfiguredTestCommandConfiguration)]);

        Assert.Same(CommandSettings.None, registry.For(typeof(UnconfiguredTestCommand)));
        Assert.Same(CommandSettings.None, CommandConfigurationRegistry.Empty.For(typeof(ConfiguredTestCommand)));
    }

    [Fact]
    public void TwoConfigurationsForOneRequest_Throw_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes(
                [typeof(ConfiguredTestCommandConfiguration), typeof(DuplicateConfiguredTestCommandConfiguration)]));

        Assert.Contains(typeof(ConfiguredTestCommand).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ConfiguredTestCommandConfiguration), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DuplicateConfiguredTestCommandConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationWithConstructorArguments_Throws_NamingIt()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(NeedsServiceCommandConfiguration)]));

        Assert.Contains(nameof(NeedsServiceCommandConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedVersionOnAQuery_Throws_NamingTheQueryAndConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(VersionedQueryConfiguration)]));

        Assert.Contains(typeof(ConfiguredTestQuery).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(VersionedQueryConfiguration), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPermission_Throws_NamingTheConfiguration_WithTheCauseInside()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CommandConfigurationRegistry.FromConfigurationTypes([typeof(MalformedPermissionConfiguration)]));

        Assert.Contains(nameof(MalformedPermissionConfiguration), error.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
    }

    [Fact]
    public void FromSources_MergesEverySource()
    {
        var registry = CommandConfigurationRegistry.FromSources(
        [
            CommandConfigurationSource.FromTypes(typeof(ConfiguredTestCommandConfiguration)),
            CommandConfigurationSource.FromTypes(typeof(ConfiguredTestQueryConfiguration)),
        ]);

        Assert.Equal(2, registry.ConfiguredTypes.Count);
    }

    [Fact]
    public void FromAssembly_FindsConfigurationsInTheAssembly()
    {
        CommandConfigurationSource source = CommandConfigurationSource.FromAssembly(typeof(CommandConfigurationRegistryTests).Assembly);

        Assert.Contains(typeof(ConfiguredTestCommandConfiguration), source.ConfigurationTypes);
        Assert.Contains(typeof(MalformedPermissionConfiguration), source.ConfigurationTypes);
        Assert.DoesNotContain(typeof(ConfiguredTestCommand), source.ConfigurationTypes);
    }
}
```

- [ ] **Step 4: Run tests, verify they fail**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: FAIL, `CS0234`/`CS0246` — namespace `Sergin.SharedKernel.Application.Commands.Configuration` not found.

- [ ] **Step 5: Implement the interface**

`ICommandConfiguration.cs`:

```csharp
namespace Sergin.SharedKernel.Application.Commands.Configuration;

/// <summary>
/// Declares the platform policy of one request — which permissions its caller needs, whether it must carry
/// an expected version — the way <see cref="Aggregates.IAggregateFeatureConfiguration{TAggregateRoot}"/>
/// declares an aggregate's features. A module writes one <c>internal sealed class &lt;RecordName&gt;Configuration</c>
/// next to the record, in its <c>.Application.Contracts</c> project, so a gateway hosting the module Remote sees
/// it too. A request without a configuration has no policy.
/// <para>
/// A configuration is a declaration, not a service: it is created with <c>Activator</c>, never through DI,
/// so it must have a parameterless constructor. <see cref="CommandConfigurationRegistry"/> runs it once.
/// </para>
/// </summary>
public interface ICommandConfiguration<TCommand>
    where TCommand : IBaseCommand
{
    void Configure(CommandConfigurationBuilder<TCommand> builder);
}
```

- [ ] **Step 6: Implement settings and builder**

`CommandSettings.cs`:

```csharp
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.SharedKernel.Application.Commands.Configuration;

/// <summary>
/// What a request's configuration declared. <see cref="None"/> is the answer for an unconfigured request: no
/// permission and no expected version required.
/// </summary>
public sealed record CommandSettings(IReadOnlyCollection<Permission> RequiredPermissions, bool RequiresExpectedVersion)
{
    public static CommandSettings None { get; } = new([], false);
}
```

`CommandConfigurationBuilder.cs`:

```csharp
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.SharedKernel.Application.Commands.Configuration;

/// <summary>The non-generic half, so the registry can read what any closed builder collected.</summary>
public abstract class CommandConfigurationBuilder
{
    private protected List<Permission> Permissions { get; } = [];

    private protected bool ExpectedVersionRequired { get; set; }

    internal CommandSettings Settings =>
        Permissions.Count == 0 && !ExpectedVersionRequired
            ? CommandSettings.None
            : new CommandSettings([.. Permissions.Distinct()], ExpectedVersionRequired);
}

public sealed class CommandConfigurationBuilder<TCommand> : CommandConfigurationBuilder
    where TCommand : IBaseCommand
{
    internal CommandConfigurationBuilder()
    {
    }

    /// <summary>
    /// The caller must hold every listed permission. Calling it again appends; a repeated permission counts once.
    /// </summary>
    public CommandConfigurationBuilder<TCommand> RequirePermissions(Permission permission, params Permission[] more)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(more);

        Permissions.Add(permission);
        Permissions.AddRange(more);
        return this;
    }

    /// <summary>
    /// A send without an expected version answers <c>VersionErrors.Required</c> (428) before the handler runs.
    /// Commands only: the registry refuses it on a query.
    /// </summary>
    public CommandConfigurationBuilder<TCommand> RequireExpectedVersion()
    {
        ExpectedVersionRequired = true;
        return this;
    }
}
```

Note: `Permission` is a record, so `Distinct()` compares by `Value`. `"not a permission"` throws in the implicit `string → Permission` conversion (`Permission.Create` → `Guard.Against.InvalidFormat`, an `ArgumentException`) inside `Configure`, which the registry wraps.

- [ ] **Step 7: Implement the source**

`CommandConfigurationSource.cs`:

```csharp
using System.Reflection;

namespace Sergin.SharedKernel.Application.Commands.Configuration;

/// <summary>
/// A set of configuration types to put in the registry, registered as a singleton per assembly the way
/// <c>AssemblyIntegrationEventSource</c> is. <c>AddSerginCore</c> adds one per module ContractsAssembly; a test
/// host adds <see cref="FromTypes"/> for its own requests, so it never scans a test assembly whole.
/// </summary>
public sealed class CommandConfigurationSource
{
    private CommandConfigurationSource(IReadOnlyCollection<Type> configurationTypes)
    {
        ConfigurationTypes = configurationTypes;
    }

    public IReadOnlyCollection<Type> ConfigurationTypes { get; }

    public static CommandConfigurationSource FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return new([.. assembly.GetTypes().Where(CommandConfigurationRegistry.IsConfigurationType)]);
    }

    public static CommandConfigurationSource FromTypes(params Type[] configurationTypes)
    {
        ArgumentNullException.ThrowIfNull(configurationTypes);

        return new([.. configurationTypes]);
    }
}
```

- [ ] **Step 8: Implement the registry**

`CommandConfigurationRegistry.cs`:

```csharp
using System.Reflection;

namespace Sergin.SharedKernel.Application.Commands.Configuration;

/// <summary>
/// What every <see cref="ICommandConfiguration{TCommand}"/> declared, keyed by request type. Built once per
/// host from every registered <see cref="CommandConfigurationSource"/> and read by the permission and
/// expected-version pipeline behaviors on each send. Refuses two configurations for one request, a
/// configuration without a parameterless constructor, an expected version on a request that is not an
/// <see cref="ICommand{TResponse}"/>, and a configuration whose Configure throws — each naming the type.
/// </summary>
public sealed class CommandConfigurationRegistry
{
    private readonly IReadOnlyDictionary<Type, CommandSettings> settings;

    private CommandConfigurationRegistry(IReadOnlyDictionary<Type, CommandSettings> settings)
    {
        this.settings = settings;
    }

    public static CommandConfigurationRegistry Empty { get; } = new(new Dictionary<Type, CommandSettings>());

    /// <summary>The configured request types.</summary>
    public IReadOnlyCollection<Type> ConfiguredTypes => [.. settings.Keys];

    /// <summary>The settings <paramref name="requestType"/> declared; None for an unconfigured request.</summary>
    public CommandSettings For(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return settings.TryGetValue(requestType, out CommandSettings? declared) ? declared : CommandSettings.None;
    }

    public static CommandConfigurationRegistry FromSources(IEnumerable<CommandConfigurationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return FromConfigurationTypes(sources.SelectMany(source => source.ConfigurationTypes).Distinct());
    }

    public static CommandConfigurationRegistry FromConfigurationTypes(IEnumerable<Type> configurationTypes)
    {
        ArgumentNullException.ThrowIfNull(configurationTypes);

        List<(Type RequestType, Type ConfigurationType, CommandSettings Settings)> declared = [];

        foreach (Type configurationType in configurationTypes)
        {
            foreach (Type closedInterface in configurationType.GetInterfaces().Where(IsClosedConfigurationInterface))
            {
                Type requestType = closedInterface.GetGenericArguments()[0];
                declared.Add((requestType, configurationType, Run(configurationType, closedInterface, requestType)));
            }
        }

        string[] duplicates =
        [
            .. declared
                .GroupBy(item => item.RequestType)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key.FullName} ({string.Join(", ", group.Select(item => item.ConfigurationType.FullName))})")
        ];

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"More than one command configuration is declared for: {string.Join("; ", duplicates)}. "
                + "Declare each request's policy in exactly one ICommandConfiguration<T>.");
        }

        return new CommandConfigurationRegistry(declared.ToDictionary(item => item.RequestType, item => item.Settings));
    }

    internal static bool IsConfigurationType(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && type.GetInterfaces().Any(IsClosedConfigurationInterface);

    private static CommandSettings Run(Type configurationType, Type closedInterface, Type requestType)
    {
        object configuration;

        try
        {
            configuration = Activator.CreateInstance(configurationType, nonPublic: true)!;
        }
        catch (MissingMethodException exception)
        {
            throw new InvalidOperationException(
                $"Command configuration {configurationType.FullName} must have a parameterless constructor: "
                + "command configurations are declarations, created without dependency injection.",
                exception);
        }

        var builder = (CommandConfigurationBuilder)Activator.CreateInstance(
            typeof(CommandConfigurationBuilder<>).MakeGenericType(requestType), nonPublic: true)!;

        try
        {
            closedInterface
                .GetMethod(nameof(ICommandConfiguration<>.Configure))!
                .Invoke(configuration, BindingFlags.DoNotWrapExceptions, binder: null, [builder], culture: null);
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Command configuration {configurationType.FullName} failed while configuring {requestType.FullName}: {exception.Message}",
                exception);
        }

        CommandSettings declared = builder.Settings;

        if (declared.RequiresExpectedVersion && !IsCommand(requestType))
        {
            throw new InvalidOperationException(
                $"Command configuration {configurationType.FullName} requires an expected version for {requestType.FullName}, "
                + "which is not an ICommand<T>. Only a command writes, so only a command can carry an expected version.");
        }

        return declared;
    }

    private static bool IsCommand(Type requestType) =>
        requestType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static bool IsClosedConfigurationInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommandConfiguration<>);
}
```

If Sonar/CA flags the general `catch (Exception)` (CA1031 / S2221), keep it: it rethrows wrapped, which both rules accept for a `when`-filtered rethrow; if the build still refuses, narrow to `catch (ArgumentException exception)` and drop the `when` — the malformed-permission test still passes.

- [ ] **Step 9: Run tests, verify they pass**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~CommandConfigurationRegistryTests"`
Expected: 9 passed. No Docker needed (the class has no `[Collection]`).

- [ ] **Step 10: Commit**

```bash
git -C src/SharedKernel add Sergin.SharedKernel.Application/Commands/Configuration
git -C src/SharedKernel commit -m "feat: add ICommandConfiguration and CommandConfigurationRegistry"
git add tests/Sergin.MeterMinder.IntegrationTests.All/Commands src/SharedKernel
git commit -m "test: command configuration registry"
```

---

### Task 2: Register the registry in every host and fail start on a bad declaration

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs` (next to the `AssemblyIntegrationEventSource` loops, ~line 108-117)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebUi/SerginWebUiExtensions.cs` (`UseSerginWebUiAsync`, beside `AggregateFeatureGuard.EnsureApplied`)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Hosts.WebApi/SerginWebApiExtensions.cs` (`UseSerginWebApiAsync`, same place)
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationHostTests.cs`

**Interfaces:**
- Consumes: `CommandConfigurationSource.FromAssembly/FromTypes`, `CommandConfigurationRegistry.FromSources` (Task 1).
- Produces: `CommandConfigurationRegistry` resolvable as a singleton from every `AddSerginCore` host.

- [ ] **Step 1: Write failing tests**

`tests/Sergin.MeterMinder.IntegrationTests.All/Commands/CommandConfigurationHostTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The registry as the real host builds it: one singleton holding every module's declarations, and a bad
/// declaration a test adds stops the host from starting, naming the offending configuration.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CommandConfigurationHostTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Host_RegistersOneRegistry_HoldingModuleDeclarations()
    {
        CommandConfigurationRegistry registry = factory.Services.GetRequiredService<CommandConfigurationRegistry>();

        Assert.Same(registry, factory.Services.GetRequiredService<CommandConfigurationRegistry>());
        Assert.Contains(typeof(DeleteDeviceCommand), registry.ConfiguredTypes);
    }

    [Fact]
    public void BadDeclaration_StopsTheHost_NamingTheConfiguration()
    {
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(CommandConfigurationSource.FromTypes(typeof(VersionedQueryConfiguration)))));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());

        Assert.Contains(nameof(VersionedQueryConfiguration), error.Message, StringComparison.Ordinal);
    }
}
```

`Host_RegistersOneRegistry_HoldingModuleDeclarations` stays red until Task 3 adds `DeleteDeviceCommandConfiguration`; that is expected and is Task 3's first green signal.

- [ ] **Step 2: Run, verify the bad-declaration test fails**

Start Docker Desktop first (`"/c/Program Files/Docker/Docker/Docker Desktop.exe" &`, then poll `docker info` until it answers).
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~CommandConfigurationHostTests"`
Expected: both FAIL — `No service for type 'CommandConfigurationRegistry'` / no exception thrown.

- [ ] **Step 3: Register sources and registry in `AddSerginCore`**

Add `using Sergin.SharedKernel.Application.Commands.Configuration;` and, directly after the remote-module `AssemblyIntegrationEventSource` loop:

```csharp
        // Every request's declared policy (ICommandConfiguration<T>), read by the permission and expected-version
        // behaviors. ContractsAssembly, not ApplicationAssembly, and for remote modules too: a gateway must refuse
        // a forbidden remote call before the gRPC hop, and a remote module ships nothing but its contracts. Built
        // from sources rather than here, so a test host can add its own request types; the Use…Async bootstraps
        // resolve it so a bad declaration fails host start, not the first send.
        foreach (ISerginModule module in localModules)
        {
            builder.Services.AddSingleton(CommandConfigurationSource.FromAssembly(module.ContractsAssembly));
        }

        foreach (ISerginRemoteModule remoteModule in remoteModules)
        {
            builder.Services.AddSingleton(CommandConfigurationSource.FromAssembly(remoteModule.ContractsAssembly));
        }

        builder.Services.AddSingleton(provider =>
            CommandConfigurationRegistry.FromSources(provider.GetServices<CommandConfigurationSource>()));
```

- [ ] **Step 4: Resolve it at start in both bootstraps**

In `UseSerginWebUiAsync` and `UseSerginWebApiAsync`, directly after `AggregateFeatureGuard.EnsureApplied(app.Services);`, add (plus `using Sergin.SharedKernel.Application.Commands.Configuration;`):

```csharp
        // Builds the registry now, so a bad ICommandConfiguration fails start rather than the first send.
        app.Services.GetRequiredService<CommandConfigurationRegistry>();
```

If IDE0058/CA1806 complains about the discarded value, write `_ = app.Services.GetRequiredService<CommandConfigurationRegistry>();`.

- [ ] **Step 5: Run, verify**

Run the Step 2 command.
Expected: `BadDeclaration_StopsTheHost_NamingTheConfiguration` PASS; `Host_RegistersOneRegistry_HoldingModuleDeclarations` still FAIL on `Assert.Contains` only (no DM configurations yet).

- [ ] **Step 6: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "feat: register CommandConfigurationRegistry in AddSerginCore and resolve it at start"
git add tests/Sergin.MeterMinder.IntegrationTests.All/Commands src/SharedKernel
git commit -m "test: command configuration host wiring"
```

---

### Task 3: Behaviors read the registry; every request migrates

Behaviors and records change together: switching a behavior before its records migrate turns every permission and version check off.

**Files:**
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/PermissionCheckPipelineBehavior.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/ExpectedVersionPipelineBehavior.cs`
- Modify (remove attribute + now-unused usings) and Create a `<Record>Configuration.cs` beside each, under `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Application.Contracts/`:

| Record (folder) | Configuration body |
|---|---|
| `Devices/Commands/GetOne/GetDeviceByIdQueryCommand` | `RequirePermissions("permission.dm.devices.read")` |
| `Devices/Commands/GetList/GetDeviceListQueryCommand` | `RequirePermissions("permission.dm.devices.read")` |
| `Devices/Commands/Update/UpdateDeviceCommand` | `RequirePermissions("permission.dm.devices.update").RequireExpectedVersion()` |
| `Devices/Commands/Delete/DeleteDeviceCommand` | `RequirePermissions("permission.dm.devices.delete").RequireExpectedVersion()` |
| `Manufacturers/Commands/GetOne/GetManufacturerByIdQueryCommand` | `RequirePermissions("permission.dm.manufacturers.read")` |
| `Manufacturers/Commands/GetList/GetManufacturerListQueryCommand` | `RequirePermissions("permission.dm.manufacturers.read")` |
| `Manufacturers/Commands/Update/UpdateManufacturerCommand` | `RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion()` |
| `Manufacturers/Commands/Delete/DeleteManufacturerCommand` | `RequirePermissions("permission.dm.manufacturers.delete").RequireExpectedVersion()` |
| `Manufacturers/DeviceModels/Commands/GetOne/GetDeviceModelByIdQueryCommand` | `RequirePermissions("permission.dm.manufacturers.read")` |
| `Manufacturers/DeviceModels/Commands/GetList/GetDeviceModelListQueryCommand` | `RequirePermissions("permission.dm.manufacturers.read")` |
| `Manufacturers/DeviceModels/Commands/Add/AddDeviceModelCommand` | `RequireExpectedVersion()` **only** (no permission today — keep it so) |
| `Manufacturers/DeviceModels/Commands/Rename/RenameDeviceModelCommand` | `RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion()` |
| `Manufacturers/DeviceModels/Commands/Remove/RemoveDeviceModelCommand` | `RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion()` |

- Same in `src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/`: `GetOne/GetUserByIdQueryCommand` and `GetList/GetUserListQueryCommand`, both `RequirePermissions("permission.ua.users.read")`.
- `ProvisionExternalUserCommand`: **no configuration**. Only reword its comment (line 11) from "Carries no `[RequiredPermissions]`" to "Has no `ICommandConfiguration`".
- Test: `tests/.../Events/OutboxTestTypes.cs` (`CreateChildAggregateCommand`), `tests/.../Events/OutboxTestHost.cs`, `tests/.../Concurrency/ExpectedVersionPipelineTests.cs`
- Test: `tests/.../Commands/ModuleCommandConfigurationTests.cs`

**Interfaces:**
- Consumes: Task 1 types, Task 2 registration.

- [ ] **Step 1: Create the UserAccess submodule branch**

```bash
git -C src/Modules/UserAccess switch -c feature/command-configuration
```

- [ ] **Step 2: Write the failing module-declaration test**

`tests/Sergin.MeterMinder.IntegrationTests.All/Commands/ModuleCommandConfigurationTests.cs`:

```csharp
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.UserAccess.Application.Contracts.Users.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Users.Commands.ProvisionExternalUser;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands;

/// <summary>
/// The modules' declarations, read straight from their contracts assemblies: the attribute-era policy, one
/// request per shape, with the two that look like mistakes pinned on purpose.
/// </summary>
public sealed class ModuleCommandConfigurationTests
{
    private static CommandConfigurationRegistry Registry { get; } = CommandConfigurationRegistry.FromSources(
    [
        CommandConfigurationSource.FromAssembly(typeof(DeleteDeviceCommand).Assembly),
        CommandConfigurationSource.FromAssembly(typeof(GetUserByIdQueryCommand).Assembly),
    ]);

    [Fact]
    public void DeleteDevice_NeedsDeletePermission_AndAVersion()
    {
        CommandSettings settings = Registry.For(typeof(DeleteDeviceCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Equal("permission.dm.devices.delete", Assert.Single(settings.RequiredPermissions).Value);
    }

    [Fact]
    public void DeviceListQuery_NeedsReadPermission()
    {
        Assert.Equal("permission.dm.devices.read", Assert.Single(Registry.For(typeof(GetDeviceListQueryCommand)).RequiredPermissions).Value);
    }

    [Fact]
    public void AddDeviceModel_NeedsAVersion_ButNoPermission()
    {
        CommandSettings settings = Registry.For(typeof(AddDeviceModelCommand));

        Assert.True(settings.RequiresExpectedVersion);
        Assert.Empty(settings.RequiredPermissions);
    }

    [Fact]
    public void ProvisionExternalUser_IsUnconfigured()
    {
        Assert.Same(CommandSettings.None, Registry.For(typeof(ProvisionExternalUserCommand)));
    }

    [Fact]
    public void ModulesDeclareFifteenRequests()
    {
        Assert.Equal(15, Registry.ConfiguredTypes.Count);
    }
}
```

Check the exact namespace of `ProvisionExternalUserCommand` with `grep -n namespace src/Modules/UserAccess/Sergin.UserAccess.Application.Contracts/Users/Commands/ProvisionExternalUser/ProvisionExternalUserCommand.cs` and fix the `using` if it differs.

- [ ] **Step 3: Run, verify it fails**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj --filter "FullyQualifiedName~ModuleCommandConfigurationTests"`
Expected: FAIL — `Assert.Single()` on empty collection, count 0 ≠ 15.

- [ ] **Step 4: Switch `PermissionCheckPipelineBehavior`**

Replace the file body with:

```csharp
using MediatR;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Users;

namespace Sergin.SharedKernel.Application.Securities.Authorization;

internal sealed class PermissionCheckPipelineBehavior<TRequest, TResponse>(
    IUserContext userContext,
    CommandConfigurationRegistry configurations) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Permission> required = configurations.For(request.GetType()).RequiredPermissions;

        if (required.Count == 0 || userContext.HasPermission([.. required]))
        {
            return await next(cancellationToken);
        }

        if (ErrorOrResponse.TryFrom(Error.Forbidden(), out TResponse forbidden))
        {
            return forbidden;
        }

        throw new ForbiddenException();
    }
}
```

If `Permission` is not already in scope via global usings, add `using Sergin.SharedKernel.Domain.Securities;`.

- [ ] **Step 5: Switch `ExpectedVersionPipelineBehavior`**

Change the constructor to `(ConcurrencyContext concurrency, CommandConfigurationRegistry configurations)`, replace `using System.Reflection;` with `using Sergin.SharedKernel.Application.Commands.Configuration;`, and the condition with:

```csharp
        if (concurrency.Expected is null
            && configurations.For(request.GetType()).RequiresExpectedVersion)
```

Update its `<summary>`: "Refuses a command whose configuration calls `RequireExpectedVersion()` that arrives without an expected version, …".

- [ ] **Step 6: Write the 15 module configurations**

One file per row in the table above, plus the two UserAccess rows. Shape (example for `UpdateDeviceCommand`, file `Devices/Commands/Update/UpdateDeviceCommandConfiguration.cs`):

```csharp
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;

internal sealed class UpdateDeviceCommandConfiguration : ICommandConfiguration<UpdateDeviceCommand>
{
    public void Configure(CommandConfigurationBuilder<UpdateDeviceCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.update").RequireExpectedVersion();
}
```

Copy each record's `namespace` line exactly. Move any explanatory comment that sat above an attribute (`AddDeviceModelCommand`, `RemoveDeviceModelCommand`, `RenameDeviceModelCommand`) onto the configuration class. In each record file, delete the attribute lines and the `using Sergin.SharedKernel.Application.Securities.Authorization;` / `using Sergin.SharedKernel.Application.Concurrency;` lines that are now unused (IDE0005 fails the build otherwise).

- [ ] **Step 7: Migrate the test-only commands**

`OutboxTestTypes.cs`: remove `[RequiredPermissions(...)]` from `CreateChildAggregateCommand`, update its summary to "a command whose configuration requires a permission no configured user holds", and add:

```csharp
internal sealed class CreateChildAggregateCommandConfiguration : ICommandConfiguration<CreateChildAggregateCommand>
{
    public void Configure(CommandConfigurationBuilder<CreateChildAggregateCommand> builder) =>
        builder.RequirePermissions("permission.test-events.aggregates.write");
}
```

`OutboxTestHost.Create`, after the `AddTransient<IRequestHandler<CreateChildAggregateCommand…` line:

```csharp
                services.AddSingleton(CommandConfigurationSource.FromTypes(typeof(CreateChildAggregateCommandConfiguration)));
```

`ExpectedVersionPipelineTests`: remove `[RequiresExpectedVersion]` from `GuardedCommand`, add

```csharp
    internal sealed class GuardedCommandConfiguration : ICommandConfiguration<GuardedCommand>
    {
        public void Configure(CommandConfigurationBuilder<GuardedCommand> builder) => builder.RequireExpectedVersion();
    }
```

and in `BuildProvider` add `services.AddSingleton(CommandConfigurationRegistry.FromConfigurationTypes([typeof(GuardedCommandConfiguration)]));`. Rename "marked"/"unmarked" in the class summary to "configured"/"unconfigured" (leave test method names as they are). Fix usings in all three files (`Sergin.SharedKernel.Application.Commands.Configuration` in; unused `…Securities.Authorization` / `…Concurrency` out only if nothing else uses them — `ExpectedVersionPipelineTests` still uses `ConcurrencyContext`).

- [ ] **Step 8: Build**

Run: `dotnet build Sergin.MeterMinder.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 9: Run the full suite**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
Expected: all green, including `ModuleCommandConfigurationTests`, `CommandConfigurationHostTests` (both), `ExpectedVersionPipelineTests`, `DispatcherUserContextTests`, `DeviceManagementConcurrencyTests`, `DeviceManagementSoftDeleteTests`, `DeviceListQueryTests`, `OutboxRelayTests`, `OutboxRelayServiceTests`.

- [ ] **Step 10: Commit (three repos)**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "feat: permission and expected-version behaviors read CommandConfigurationRegistry"
git -C src/Modules/UserAccess add -A
git -C src/Modules/UserAccess commit -m "feat: declare user query permissions with ICommandConfiguration"
git add -A
git commit -m "feat: declare DeviceManagement request policy with ICommandConfiguration"
```

---

### Task 4: Delete the attributes

**Files:**
- Delete: `src/SharedKernel/Sergin.SharedKernel.Application/Securities/Authorization/RequiredPermissionsAttribute.cs`
- Delete: `src/SharedKernel/Sergin.SharedKernel.Application/Concurrency/RequiresExpectedVersionAttribute.cs`
- Modify doc comments naming them: `ExpectedVersionEndpointFilter.cs`, `ScopedSerginDispatcher.cs`, `UserContextAccessor.cs`, `RelayUserContext.cs`, `IExternalIdentityResolver.cs`, `IListQueryHandler.cs`, `AggregateFeatureBuilder.cs`, `SerginNavItem.cs` (all under `src/SharedKernel`), and `tests/.../Devices/DeviceListQueryTests.cs:13`.

- [ ] **Step 1: Delete both files**

```bash
git -C src/SharedKernel rm Sergin.SharedKernel.Application/Securities/Authorization/RequiredPermissionsAttribute.cs Sergin.SharedKernel.Application/Concurrency/RequiresExpectedVersionAttribute.cs
```

- [ ] **Step 2: Reword every remaining mention**

Run: `grep -rn "RequiredPermissions\|RequiresExpectedVersion" src tests --include=*.cs`
For each hit, replace `[RequiredPermissions]` / `<c>[RequiredPermissions]</c>` with `a required permission (ICommandConfiguration)` and `[RequiresExpectedVersion]` with `RequireExpectedVersion()` in its configuration, keeping the sentence otherwise intact. A `<see cref>` to a deleted type must become plain `<c>` text or a `<see cref="ICommandConfiguration{TCommand}"/>` (add the using), otherwise CS1574 fails the build.
Expected after: the grep returns only `RequireExpectedVersion` (builder method) and `RequiredPermissions` (`CommandSettings` property) hits.

- [ ] **Step 3: Build and test**

Run: `dotnet build Sergin.MeterMinder.slnx` → 0 warnings, 0 errors.
Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` → all green.

- [ ] **Step 4: Commit**

```bash
git -C src/SharedKernel add -A
git -C src/SharedKernel commit -m "refactor: delete RequiredPermissions and RequiresExpectedVersion attributes"
git add -A
git commit -m "refactor: drop attribute references after command configuration migration"
```

---

### Task 5: Documentation

**Files:**
- Modify: `.claude/CLAUDE.md` (root), `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`, `src/Modules/DeviceManagement/CLAUDE.md`
- Modify: `src/SharedKernel/.claude/CLAUDE.md`, `src/SharedKernel/README.md`
- Modify: `src/Modules/UserAccess/.claude/CLAUDE.md`, `src/Modules/UserAccess/README.md`, `src/Modules/UserAccess/.claude/skills/add-feature/SKILL.md`
- Modify: `docs/superpowers/specs/2026-10-01-command-configuration-design.md` (status + two deviations)
- Leave untouched: older specs under `docs/superpowers/specs/` (they are history).

- [ ] **Step 1: Root CLAUDE.md**

Replace the **Permissions** bullet under "Cross-cutting conventions" with:

```markdown
- **Request policy (permissions, expected version)**: declared per request in an `internal sealed class <RecordName>Configuration : ICommandConfiguration<<RecordName>>` (`Sergin.SharedKernel.Application.Commands.Configuration`) next to the record in the module's `.Application.Contracts`, e.g. `builder.RequirePermissions("permission.dm.devices.delete").RequireExpectedVersion();`. Permission names are `permission.<schema>.<resource>.<action>`. `AddSerginCore` builds one `CommandConfigurationRegistry` from every local and remote `ContractsAssembly` (Contracts, so a Remote gateway still refuses before the gRPC hop); `PermissionCheckPipelineBehavior` and `ExpectedVersionPipelineBehavior` read it per send. An unconfigured request has no policy — `ProvisionExternalUserCommand` stays unconfigured on purpose, and `AddDeviceModelCommand` requires a version but no permission. Bad declarations (two for one request, constructor arguments, a version on a query, a malformed permission) fail host start naming the type. A test adds its own with `services.AddSingleton(CommandConfigurationSource.FromTypes(typeof(X)))`. The `[RequiredPermissions]`/`[RequiresExpectedVersion]` attributes no longer exist. Design: `docs/superpowers/specs/2026-10-01-command-configuration-design.md`.
```

Then run `grep -n "RequiredPermissions\|RequiresExpectedVersion" .claude/CLAUDE.md` and reword each remaining hit to name the configuration (e.g. "`GetManufacturerListQueryCommand`'s configuration requires `permission.dm.manufacturers.read`"; "`[RequiresExpectedVersion]` on a command" → "`RequireExpectedVersion()` in a command's configuration"). Add `Commands/` to the integration-test list: "`Commands/` covers request policy: `CommandConfigurationRegistryTests` (declared settings, `None`, the four refusals), `CommandConfigurationHostTests` (one registry per host; a bad declaration stops start), `ModuleCommandConfigurationTests` (the modules' declarations, including the two deliberate oddities)."

- [ ] **Step 2: Skills and module docs**

In each `add-feature`/`add-module` SKILL.md and module CLAUDE.md/README, replace every instruction to put `[RequiredPermissions(...)]` or `[RequiresExpectedVersion]` on a record with: "Add `<RecordName>Configuration.cs` next to the record (`internal sealed class … : ICommandConfiguration<<RecordName>>`, calling `RequirePermissions(...)` and, for a guarded write, `RequireExpectedVersion()`)." Include the `UpdateDeviceCommandConfiguration` code block from Task 3 Step 6 as the template in both `add-feature` skills. Verify: `grep -rn "\[RequiredPermissions\|\[RequiresExpectedVersion" .claude src/SharedKernel/.claude src/SharedKernel/README.md src/Modules/*/CLAUDE.md src/Modules/UserAccess/.claude src/Modules/UserAccess/README.md` → no hits.

- [ ] **Step 3: SharedKernel CLAUDE.md**

Add a "Command configuration" section listing the five types from Task 1 and their roles in one line each, the four refusals, and the registration in `AddSerginCore` / resolution in both `Use…Async` methods.

- [ ] **Step 4: Amend the spec**

In the spec: set **Status** to "Implemented on feature/command-configuration (host, Sergin.SharedKernel, Sergin.UserAccess)". Under "Sources and registration", replace `CommandConfigurationAssembly(Assembly Assembly)` with `CommandConfigurationSource` (`FromAssembly` / `FromTypes`) and add: "Sources carry types, not assemblies, so a test host adds explicit types and never scans the test assembly, which holds deliberately broken configurations." Replace the `CommandConfigurationGuard` hosted-service sentence with: "`UseSerginWebUiAsync` and `UseSerginWebApiAsync` resolve the registry beside `AggregateFeatureGuard.EnsureApplied`, the existing start-guard precedent." Under Migration, correct UserAccess to two records and note `ProvisionExternalUserCommand` had no attribute and stays unconfigured.

- [ ] **Step 5: Commit (three repos)**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "docs: command configuration"
git -C src/Modules/UserAccess add -A && git -C src/Modules/UserAccess commit -m "docs: command configuration"
git add -A && git commit -m "docs: command configuration"
```

---

### Task 6: End-to-end verification

- [ ] **Step 1: Clean build and full suite**

```bash
dotnet build Sergin.MeterMinder.slnx
dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj
```
Expected: 0 warnings; every test passes.

- [ ] **Step 2: Forbidden path in the running host**

Temporarily remove `"permission.dm.manufacturers.read"` from `Sergin:DevUser:Permissions` in `src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json`, run `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`, open `http://localhost:5002/dm/manufacturers`. Expected: forbidden snackbar / problem panel, as before the change. Restore the file (`git checkout -- src/Hosts/Sergin.MeterMinder.Hosts.All/appsettings.json`).

- [ ] **Step 3: Version path in the running host**

With the file restored, open a manufacturer's edit page in two tabs, save in one, then save in the other. Expected: stale-version message and reload, as before. Stop the host.

- [ ] **Step 4: Confirm nothing stray**

`git status` in the host repo and both submodules → clean. `git -C src/SharedKernel log --oneline -5` and `git -C src/Modules/UserAccess log --oneline -3` show this plan's commits on `feature/command-configuration`.
