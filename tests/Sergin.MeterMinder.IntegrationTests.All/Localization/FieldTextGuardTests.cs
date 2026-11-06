using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.Platform;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Hosts.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;
using Sergin.UserAccess;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The field-text guard (spec decision 16): every property a person sees on a configured request has a
/// <see cref="FieldText"/> in the request's category, unless its configuration marks it <c>NotShown</c>; the paging and
/// criteria records are excluded once. The reverse of decision 7 too: every list field id has a field text, so no list
/// column or filter chip is left without a label. The real host passes; test-only requests show each refusal.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class FieldTextGuardTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Guard_PassesForRealHost()
    {
        FieldTextGuard guard = factory.Services.GetRequiredService<FieldTextGuard>();

        Exception? refused = Record.Exception(() => guard.EnsureLabelled(
            factory.Services.GetRequiredService<CommandConfigurationRegistry>(),
            factory.Services.GetRequiredService<TextCatalog>()));

        Assert.Null(refused);
    }

    [Fact]
    public void Guard_ReadsEveryModulesContractsAssembly()
    {
        FieldTextGuard guard = factory.Services.GetRequiredService<FieldTextGuard>();
        ISerginModule[] modules = [new DeviceManagementModule(), new UserAccessModule(), new PlatformModule()];

        Assert.Equivalent(modules.Select(module => module.ContractsAssembly), guard.Assemblies);
    }

    [Fact]
    public void Guard_RunsAtHostStart_NamingTheUnlabelledProperty()
    {
        // The real guard reads the Contracts assemblies; this one reads the test assembly, so the test configuration's
        // request is in its reach. What is proven is that start runs the guard over the host's own registry and catalog.
        using WebApplicationFactory<Program> broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(ModuleConfigurationSource.FromTypes(typeof(UnlabelledCommandConfiguration)));
            services.AddSingleton(new FieldTextGuard([typeof(UnlabelledCommand).Assembly]));
        }));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());

        Assert.Contains($"{typeof(UnlabelledCommand).FullName}.{nameof(UnlabelledCommand.Extra)}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_RefusesUnlabelledProperty()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(UnlabelledCommand), typeof(UnlabelledCommandConfiguration)));

        Assert.Contains($"{typeof(UnlabelledCommand).FullName}.{nameof(UnlabelledCommand.Extra)}", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($".{nameof(UnlabelledCommand.Name)}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_HonoursNotShown()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(PartlyHiddenCommand), typeof(PartlyHiddenCommandConfiguration)));

        Assert.Contains($".{nameof(PartlyHiddenCommand.Other)}", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($".{nameof(PartlyHiddenCommand.Extra)}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_PassesAFullyLabelledListQuery_SkippingThePagingRecords()
    {
        Exception? refused = Record.Exception(() =>
            Check(typeof(LabelledListQuery), typeof(LabelledListQueryConfiguration), typeof(LabelledListFields)));

        Assert.Null(refused);
    }

    [Fact]
    public void Guard_RefusesARequestWithoutACategory()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(UncategorisedCommand), typeof(UncategorisedCommandConfiguration)));

        Assert.Contains(typeof(UncategorisedCommand).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains("no text category", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_RefusesAnOrphanListField()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(OrphanListQuery), typeof(OrphanListQueryConfiguration), typeof(OrphanListFields)));

        Assert.Contains($"'{OrphanListFields.Unlabelled}'", refused.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(OrphanListFields).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{OrphanListFields.Name}'", refused.Message, StringComparison.Ordinal);
    }

    private static void Check(Type requestType, Type configurationType, params Type[] more) =>
        FieldTextGuard.Check(
            [requestType, .. more],
            CommandConfigurationRegistry.FromConfigurationTypes([configurationType]),
            TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", typeof(GuardTestTexts))]));

    public static class GuardTestTexts
    {
        public static TextCategory Category { get; } = TextCategory.Create("DeviceManagement", "FieldTextGuardTests");

        public static FieldText Name { get; } = Category.Field(nameof(UnlabelledCommand.Name), "Name");
    }

    internal sealed record UnlabelledCommand(string Name, string Extra) : ICommand<Guid>;

    internal sealed class UnlabelledCommandConfiguration : ICommandConfiguration<UnlabelledCommand>
    {
        public void Configure(CommandConfigurationBuilder<UnlabelledCommand> builder) => builder.Texts(GuardTestTexts.Category);
    }

    internal sealed record PartlyHiddenCommand(string Name, string Extra, string Other) : ICommand<Guid>;

    internal sealed class PartlyHiddenCommandConfiguration : ICommandConfiguration<PartlyHiddenCommand>
    {
        public void Configure(CommandConfigurationBuilder<PartlyHiddenCommand> builder) =>
            builder.Texts(GuardTestTexts.Category).NotShown(command => command.Extra);
    }

    internal sealed record UncategorisedCommand(string Name) : ICommand<Guid>;

    internal sealed class UncategorisedCommandConfiguration : ICommandConfiguration<UncategorisedCommand>
    {
        public void Configure(CommandConfigurationBuilder<UncategorisedCommand> builder)
        {
            // Neither Texts(...) nor a bound root: nothing names where Name's label lives.
        }
    }

    internal sealed record LabelledListItem(string Name);

    internal sealed record LabelledListQuery : ListQuery<LabelledListItem>
    {
        public LabelledListQuery(Paggination paggination, Term? term = default, Filtering? filtering = default, Sorting? sorting = default)
            : base(paggination, term, filtering, sorting)
        {
        }
    }

    internal sealed class LabelledListQueryConfiguration : ICommandConfiguration<LabelledListQuery>
    {
        public void Configure(CommandConfigurationBuilder<LabelledListQuery> builder) => builder.Texts(GuardTestTexts.Category);
    }

    public static class LabelledListFields
    {
        public const string Name = "name";

        public static ListFieldSet Set { get; } = ListFieldSet.Create(ListField.SortOnly(Name));
    }

    internal sealed record OrphanListQuery : ListQuery<LabelledListItem>
    {
        public OrphanListQuery(Paggination paggination)
            : base(paggination)
        {
        }
    }

    internal sealed class OrphanListQueryConfiguration : ICommandConfiguration<OrphanListQuery>
    {
        public void Configure(CommandConfigurationBuilder<OrphanListQuery> builder) => builder.Texts(GuardTestTexts.Category);
    }

    public static class OrphanListFields
    {
        public const string Name = "name";

        public const string Unlabelled = "unlabelled";

        public static ListFieldSet Set { get; } = ListFieldSet.Create(ListField.SortOnly(Name), ListField.SortOnly(Unlabelled));
    }
}
