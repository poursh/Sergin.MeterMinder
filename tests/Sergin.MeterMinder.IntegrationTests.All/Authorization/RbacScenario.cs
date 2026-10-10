using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.GetOne;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.UserAccess.Domain.Roles;
using Sergin.UserAccess.Domain.Scopes;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// The acceptance tree, built fresh per test so tests sharing the database never see each other's scopes:
/// Company &gt; North Region &gt; Feeder 1 and Feeder 2, plus a South Region beside North. Every write goes
/// through <see cref="ISerginDispatcher"/> as the host's development user, the way a Blazor page sends it.
/// </summary>
internal sealed class RbacScenario(SerginWebApiFactory<Program> factory)
{
    public static readonly Permission Read = "permission.dm.devices.read";
    public static readonly Permission Update = "permission.dm.devices.update";

    public Guid Company { get; private set; }
    public Guid North { get; private set; }
    public Guid South { get; private set; }
    public Guid Feeder1 { get; private set; }
    public Guid Feeder2 { get; private set; }

    public static async Task<RbacScenario> BuildAsync(SerginWebApiFactory<Program> factory)
    {
        RbacScenario scenario = new(factory);
        string tag = Guid.NewGuid().ToString("N")[..8];

        scenario.Company = await scenario.CreateScopeAsync(Scope.RootId.Value, $"Company {tag}");
        scenario.North = await scenario.CreateScopeAsync(scenario.Company, "North Region");
        scenario.South = await scenario.CreateScopeAsync(scenario.Company, "South Region");
        scenario.Feeder1 = await scenario.CreateScopeAsync(scenario.North, "Feeder 1");
        scenario.Feeder2 = await scenario.CreateScopeAsync(scenario.North, "Feeder 2");

        return scenario;
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<ErrorOr<TResponse>> request, IUserContext? caller = null)
    {
        ErrorOr<TResponse> result = await TrySendAsync(request, caller);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);

        return result.Value;
    }

    public async Task<ErrorOr<TResponse>> TrySendAsync<TResponse>(IRequest<ErrorOr<TResponse>> request, IUserContext? caller = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        return await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
    }

    /// <summary>Sends a version-guarded scope command at the scope's current version.</summary>
    public async Task<ErrorOr<TResponse>> SendAtScopeVersionAsync<TResponse>(Guid scopeId, IRequest<ErrorOr<TResponse>> command)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<ScopeQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetScopeByIdQueryCommand(scopeId));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    /// <summary>Sends a version-guarded role command at the role's current version.</summary>
    public async Task<ErrorOr<TResponse>> SendAtRoleVersionAsync<TResponse>(Guid roleId, IRequest<ErrorOr<TResponse>> command, IUserContext? caller = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<RoleQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetRoleByIdQueryCommand(roleId));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    public async Task<Guid> CreateScopeAsync(Guid parentId, string name) =>
        (await SendAsync(new CreateScopeCommand(parentId, new ScopeName(name)))).Id;

    public async Task<Guid> CreateRoleAsync(Guid ownerScopeId, string name, bool inheritable, params Permission[] permissions) =>
        (await SendAsync(new CreateRoleCommand(ownerScopeId, new RoleName(name), inheritable, permissions))).Id;

    public async Task<Guid> CreateUserAsync(string name) =>
        (await SendAsync(new CreateUserCommand(new UserName($"{name}-{Guid.NewGuid():N}")))).Id;

    public Task<ErrorOr<AssignRoleCommandResponse>> AssignAsync(
        Guid userId, Guid roleId, Guid scopeId, bool includeSubtree, DateTime? fromUtc = null, DateTime? untilUtc = null) =>
        TrySendAsync(new AssignRoleCommand(userId, roleId, scopeId, includeSubtree, fromUtc, untilUtc));

    public async Task<bool> HasAsync(Guid userId, Permission permission, Guid scopeId)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IScopedPermissionChecker>()
            .HasPermissionAsync(userId, permission, scopeId);
    }
}

/// <summary>A caller holding exactly the given global permissions.</summary>
internal sealed class TestCaller(params Permission[] permissions) : IUserContext
{
    public UserId Id { get; init; } = new(Guid.CreateVersion7());
    public string UserName => "test-caller";
    public string FirstName => "Test";
    public string LastName => "Caller";
    public string Email => "test-caller@sergin.local";
    public HashSet<Permission> Permissions { get; } = [.. permissions];
}
