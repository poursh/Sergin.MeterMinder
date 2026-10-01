using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// ExpectedVersionPipelineBehavior on its own, over a bare MediatR container with test-only commands: a configured
/// command without a version is refused before its handler runs, a conflict thrown by the save becomes the
/// stale-version error, and a version sent with an unconfigured command is still honoured. No database.
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
        services.AddSingleton(CommandConfigurationRegistry.FromConfigurationTypes([typeof(GuardedCommandConfiguration)]));

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

    internal sealed record GuardedCommand(bool Conflict) : ICommand<Success>;

    internal sealed class GuardedCommandConfiguration : ICommandConfiguration<GuardedCommand>
    {
        public void Configure(CommandConfigurationBuilder<GuardedCommand> builder) => builder.RequireExpectedVersion();
    }

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
