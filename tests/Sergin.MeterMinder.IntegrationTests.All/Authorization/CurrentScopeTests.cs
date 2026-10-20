using ErrorOr;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// IAuthorizationScopeContext and the permission check on it, on the acceptance tree: every configured permission is
/// checked on the current scope, and one bound to an aggregate that is not scoped on any scope. Each send goes through
/// ISerginDispatcher from a "circuit" scope whose context the test sets, so the dispatcher's copy into the send's
/// own scope is exercised too. The test commands echo the scope their handler saw.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CurrentScopeTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Context_StartsAtTheRoot_AndSetAndResetMoveIt()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAuthorizationScopeContext context = scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>();

        Assert.Equal(AuthorizationScope.RootId, context.ScopeId);
        Assert.True(context.IsRoot);

        var picked = Guid.CreateVersion7();
        context.Set(picked);
        Assert.Equal(picked, context.ScopeId);
        Assert.False(context.IsRoot);

        Assert.Throws<ArgumentException>(() => context.Set(Guid.Empty));

        context.Reset();
        Assert.True(context.IsRoot);

        using IServiceScope other = factory.Services.CreateScope();
        Assert.True(other.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().IsRoot);
    }

    [Fact]
    public async Task ScopedCommand_FollowsTheCurrentScope()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);

        await using WebApplicationFactory<Program> host = CreateHost();
        TestCaller caller = new() { Id = new UserId(ali) };

        // The handler runs in the dispatcher's own scope and still sees the circuit's choice.
        Assert.Equal(tree.North, (await SendAsync(host, new ScopedCommand(), caller, tree.North)).Value);
        Assert.False((await SendAsync(host, new ScopedCommand(), caller, tree.Feeder1)).IsError);

        Assert.Equal(ErrorType.Forbidden, (await SendAsync(host, new ScopedCommand(), caller, null)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(host, new ScopedCommand(), caller, tree.Company)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(host, new ScopedCommand(), caller, tree.South)).FirstError.Type);

        // Bound to an unscoped aggregate, the same permission held on North passes in any scope.
        Assert.Equal(tree.South, (await SendAsync(host, new GlobalCommand(), caller, tree.South)).Value);
        Assert.False((await SendAsync(host, new GlobalCommand(), caller, null)).IsError);
    }

    [Fact]
    public async Task ScopedCommand_PassesARootHolderInAnyScope_AndIgnoresClaims()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller rootHolder = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, RbacScenario.Update);
        await using WebApplicationFactory<Program> host = CreateHost();

        Assert.Equal(AuthorizationScope.RootId, (await SendAsync(host, new ScopedCommand(), rootHolder, null)).Value);
        Assert.Equal(tree.South, (await SendAsync(host, new ScopedCommand(), rootHolder, tree.South)).Value);
        Assert.Equal(tree.South, (await SendAsync(host, new GlobalCommand(), rootHolder, tree.South)).Value);

        // No stored grant passes no check, on the current scope or on any scope.
        TestCaller ungranted = new();
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(host, new ScopedCommand(), ungranted, tree.South)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(host, new GlobalCommand(), ungranted, null)).FirstError.Type);
    }

    [Fact]
    public async Task ScopedCommand_IsDenied_WhenTheCheckFails()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);

        // Ali would pass on North; the check failing is what refuses him.
        await using WebApplicationFactory<Program> host = CreateHost(
            services => services.AddScoped<IScopedPermissionChecker, ThrowingChecker>());

        ErrorOr<Guid> result = await SendAsync(host, new ScopedCommand(), new TestCaller { Id = new UserId(ali) }, tree.North);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    private WebApplicationFactory<Program> CreateHost(Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(CommandConfigurationSource.FromTypes(
                typeof(ScopedCommandConfiguration), typeof(GlobalCommandConfiguration)));
            services.AddTransient<IRequestHandler<ScopedCommand, ErrorOr<Guid>>, EchoScopeHandler<ScopedCommand>>();
            services.AddTransient<IRequestHandler<GlobalCommand, ErrorOr<Guid>>, EchoScopeHandler<GlobalCommand>>();
            configure?.Invoke(services);
        }));

    /// <summary>Sends from a scope standing in for a Blazor circuit, with its current scope set when given.</summary>
    private static async Task<ErrorOr<Guid>> SendAsync(
        WebApplicationFactory<Program> host, ICommand<Guid> command, IUserContext caller, Guid? currentScope)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;

        if (currentScope is { } scopeId)
        {
            circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        }

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(command);
    }

    internal sealed record ScopedCommand : ICommand<Guid>;

    internal sealed record GlobalCommand : ICommand<Guid>;

    /// <summary>An aggregate with no feature configuration, so not scoped. No context maps it; only the binding reads it.</summary>
    internal sealed class GlobalRoot : AggregateRoot<Guid>;

    internal sealed class ScopedCommandConfiguration : ICommandConfiguration<ScopedCommand>
    {
        public void Configure(CommandConfigurationBuilder<ScopedCommand> builder) =>
            builder.RequirePermissions(DevicePermissions.Update);
    }

    internal sealed class GlobalCommandConfiguration : ICommandConfiguration<GlobalCommand, GlobalRoot>
    {
        public void Configure(CommandConfigurationBuilder<GlobalCommand> builder) =>
            builder.RequirePermissions(DevicePermissions.Update);
    }

    internal sealed class EchoScopeHandler<TCommand>(IAuthorizationScopeContext context) : IRequestHandler<TCommand, ErrorOr<Guid>>
        where TCommand : ICommand<Guid>
    {
        public Task<ErrorOr<Guid>> Handle(TCommand request, CancellationToken cancellationToken) =>
            Task.FromResult<ErrorOr<Guid>>(context.ScopeId);
    }

    private sealed class ThrowingChecker : IScopedPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, Permission permission, Guid scopeId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The check query failed.");

        public Task<ScopeReach> GetReachAsync(Guid userId, Permission permission, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The reach query failed.");
    }
}
