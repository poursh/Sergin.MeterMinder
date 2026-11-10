using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Concurrency;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// The version rule the registry derives from a record's contract-level type, enforced by the real
/// ExpectedVersionPipelineBehavior: typed, unconfigured records on the versioned <see cref="Pallet"/> root.
/// A typed entity command needs a version, a typed query does not, and a waiver lifts the rule. No database.
/// </summary>
public sealed class TypedExpectedVersionPipelineTests
{
    [Fact]
    public async Task UnconfiguredTypedCommand_SentWithoutVersion_Is428()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        ErrorOr<Deleted> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new DeletePalletCommand(Guid.CreateVersion7()));

        Assert.True(result.IsError);
        Assert.Equal(VersionErrors.Required, result.FirstError);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task TypedQuery_SentWithoutVersion_Runs()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        ErrorOr<string> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetPalletQuery(Guid.CreateVersion7()));

        Assert.False(result.IsError);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task TypedCommand_WithWaiver_SentWithoutVersion_Runs()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        ErrorOr<Deleted> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new WaivedDeletePalletCommand(Guid.CreateVersion7()));

        Assert.False(result.IsError);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<HandlerCalls>().Count);
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();

        services.AddScoped<ConcurrencyContext>();
        services.AddScoped<HandlerCalls>();
        services.AddSingleton(CommandConfigurationRegistry.FromSources(
        [
            ModuleConfigurationSource.FromTypes(
                typeof(DeletePalletCommand),
                typeof(GetPalletQuery),
                typeof(WaivedDeletePalletCommand),
                typeof(WaivedDeletePalletConfiguration),
                typeof(PalletAggregateFeatureConfiguration)),
        ]));

        // Scanned assembly holds no handlers; the explicit registrations below are the only ones.
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblyContaining<GetDeviceByIdQueryCommand>();
            options.AddOpenBehavior(typeof(ExpectedVersionPipelineBehavior<,>));
        });

        services.AddTransient<IRequestHandler<DeletePalletCommand, ErrorOr<Deleted>>, DeleteHandler<DeletePalletCommand>>();
        services.AddTransient<IRequestHandler<WaivedDeletePalletCommand, ErrorOr<Deleted>>, DeleteHandler<WaivedDeletePalletCommand>>();
        services.AddTransient<IRequestHandler<GetPalletQuery, ErrorOr<string>>, GetHandler>();

        return services.BuildServiceProvider();
    }

    internal sealed record DeletePalletCommand(Guid Id) : IDeleteCommand<Pallet, Guid>, ICommand<Deleted>;

    internal sealed record WaivedDeletePalletCommand(Guid Id) : IDeleteCommand<Pallet, Guid>, ICommand<Deleted>;

    internal sealed record GetPalletQuery(Guid Id) : IGetOneQuery<Pallet, Guid>, IQuery<string>;

    internal sealed class WaivedDeletePalletConfiguration : ICommandConfiguration<WaivedDeletePalletCommand>
    {
        public void Configure(CommandConfigurationBuilder<WaivedDeletePalletCommand> builder) => builder.WithoutExpectedVersion();
    }

    internal sealed class HandlerCalls
    {
        public int Count { get; set; }
    }

    internal sealed class DeleteHandler<TCommand>(HandlerCalls calls) : IRequestHandler<TCommand, ErrorOr<Deleted>>
        where TCommand : IRequest<ErrorOr<Deleted>>
    {
        public Task<ErrorOr<Deleted>> Handle(TCommand request, CancellationToken cancellationToken)
        {
            calls.Count++;
            return Task.FromResult<ErrorOr<Deleted>>(Result.Deleted);
        }
    }

    internal sealed class GetHandler(HandlerCalls calls) : IRequestHandler<GetPalletQuery, ErrorOr<string>>
    {
        public Task<ErrorOr<string>> Handle(GetPalletQuery request, CancellationToken cancellationToken)
        {
            calls.Count++;
            return Task.FromResult<ErrorOr<string>>("ok");
        }
    }
}
