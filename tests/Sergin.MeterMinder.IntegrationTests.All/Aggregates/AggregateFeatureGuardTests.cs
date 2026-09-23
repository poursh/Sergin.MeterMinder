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
        Assert.Null(Record.Exception(() => AggregateFeatureGuard.EnsureApplied(factory.Services)));
    }

    [Fact]
    public void ConfiguredTypeNoContextMaps_IsRefused()
    {
        var registry =
            AggregateFeatureRegistry.FromConfigurationTypes([typeof(UnmappedEntityAggregateConfiguration)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AggregateFeatureGuard.EnsureApplied(registry, Models()));

        Assert.Contains(typeof(UnmappedEntity).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("not mapped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredTypeWhoseContextDoesNotApplyIt_IsRefused()
    {
        var registry =
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
