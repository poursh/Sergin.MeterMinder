using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Hosts.Commands;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// The shape guard (host start): a typed record must fit the model its interfaces claim. Each refusal names the type.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CommandTypeGuardTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void RealHost_PassesBothGuards()
    {
        IServiceProvider services = factory.Services;
        CommandConfigurationRegistry registry = services.GetRequiredService<CommandConfigurationRegistry>();

        Assert.Null(Record.Exception(() => services.GetRequiredService<CommandTypeGuard>()
            .EnsureShaped(registry, services.GetServices<ModuleConfigurationSource>())));
        Assert.Null(Record.Exception(() => CommandTargetMappingGuardTests.EnsureRealHostMapped(services)));
    }

    [Fact]
    public void Guard_RunsAtHostStart_NamingTheMisshapenRecord()
    {
        // The real guard reads the Contracts assemblies; this one reads the test assembly, which holds misshapen
        // records on purpose. What is proven is that start runs the shape guard.
        using WebApplicationFactory<Program> broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(new CommandTypeGuard([typeof(TwoEffectsCommand).Assembly]))));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());

        Assert.Contains(typeof(TwoEffectsCommand).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WellShapedRecords_Pass()
    {
        Exception? refused = Record.Exception(() => Check(
            [
                typeof(TestDeleteCommand), typeof(TestSelectableDeleteCommand), typeof(TestSetPropertyCommand),
                typeof(TestSetPropertyChildCommand), typeof(TestTransitionCommand), typeof(TestReverseTransitionCommand),
                typeof(TestMoveCommand), typeof(TestAddValueCommand), typeof(TestSystemCommand), typeof(TestUntypedCommand),
                typeof(SetPropertyWithHiddenCommand), typeof(SetPropertyWithHiddenConfiguration),
            ],
            requireTyped: false));

        Assert.Null(refused);
    }

    [Fact]
    public void TwoEffectInterfaces_AreRefused() => AssertRefused(typeof(TwoEffectsCommand));

    [Fact]
    public void TwoRoots_AreRefused() => AssertRefused(typeof(TestTwoRootsCommand));

    [Fact]
    public void SelectableOnCollectionTarget_IsRefused() => AssertRefused(typeof(SelectableCreateCommand));

    [Fact]
    public void SelectableOnRootTarget_IsRefused() => AssertRefused(typeof(SelectableAddChildCommand));

    [Fact]
    public void SetPropertyWithTwoShownProperties_IsRefused() => AssertRefused(typeof(SetTwoPropertiesCommand));

    [Fact]
    public void SetPropertyWithNoShownProperty_IsRefused() => AssertRefused(typeof(SetNoPropertyCommand));

    [Fact]
    public void TransitionWithShownProperty_IsRefused() => AssertRefused(typeof(TransitionWithPropertyCommand));

    [Fact]
    public void RestoreOnRootNotSoftDeletable_IsRefused() =>
        AssertRefused(typeof(TestRestoreCommand), typeof(UnversionedTypedRootConfiguration));

    [Fact]
    public void PurgeOnRootNotSoftDeletable_IsRefused() =>
        AssertRefused(typeof(TestPurgeCommand), typeof(UnversionedTypedRootConfiguration));

    [Fact]
    public void RestoreAndPurgeOnSoftDeletableRoot_Pass() =>
        Assert.Null(Record.Exception(() =>
            Check([typeof(TestRestoreCommand), typeof(TestPurgeCommand), typeof(SoftDeletableTypedRootConfiguration)], requireTyped: false)));

    [Fact]
    public void ReverseOfANonTransition_IsRefused() => AssertRefused(typeof(ReverseOfDeleteCommand));

    [Fact]
    public void ReverseOfATransitionOnAnotherRoot_IsRefused() => AssertRefused(typeof(ReverseOfOtherRootCommand));

    [Fact]
    public void UntypedCommand_IsRefused_OnlyWhenTypingIsRequired()
    {
        Assert.Null(Record.Exception(() => Check([typeof(TestUntypedCommand)], requireTyped: false)));

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => Check([typeof(TestUntypedCommand)], requireTyped: true));

        Assert.Contains(typeof(TestUntypedCommand).FullName!, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemCommandAndQueries_PassWhenTypingIsRequired() =>
        Assert.Null(Record.Exception(() =>
            Check([typeof(TestSystemCommand), typeof(TestGetOneQuery), typeof(TestDeleteCommand)], requireTyped: true)));

    [Fact]
    public void Refusal_ListsEveryOffendingType()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check([typeof(TwoEffectsCommand), typeof(SetNoPropertyCommand)], requireTyped: false));

        Assert.Contains(typeof(TwoEffectsCommand).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SetNoPropertyCommand).FullName!, refused.Message, StringComparison.Ordinal);
    }

    private static void AssertRefused(Type requestType, params Type[] configurations)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check([requestType, .. configurations], requireTyped: false));

        Assert.Contains(requestType.FullName!, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The types are the candidates; the configuration types among them also build the registries. Requests are left
    /// out of the registry's source on purpose, so a record the registry itself would refuse reaches the guard.
    /// </summary>
    private static void Check(Type[] types, bool requireTyped)
    {
        Type[] configurations = [.. types.Where(IsConfiguration)];

        CommandTypeGuard.Check(
            types,
            CommandConfigurationRegistry.FromConfigurationTypes(configurations),
            AggregateFeatureRegistry.FromConfigurationTypes(configurations.Where(type => type.GetInterfaces().Any(IsAggregateConfiguration))),
            requireTyped);
    }

    private static bool IsConfiguration(Type type) =>
        type.GetInterfaces().Any(contract => IsAggregateConfiguration(contract)
            || contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommandConfiguration<>));

    private static bool IsAggregateConfiguration(Type contract) =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IAggregateFeatureConfiguration<>);
}
