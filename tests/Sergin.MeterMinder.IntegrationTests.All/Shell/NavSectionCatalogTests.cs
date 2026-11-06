using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// How <see cref="SerginUiModuleCatalog"/> groups nav entries by module. No host: the catalog is built from
/// modules declared here, so the ordering rules are tested apart from what the real modules happen to declare.
/// </summary>
public sealed class NavSectionCatalogTests
{
    private static readonly SerginUiModuleCatalog Catalog = new(
    [
        new NavModule(
            "zz",
            new SerginNavGroup(TestText.Of("Zeta"), string.Empty, Order: 10),
            [new(TestText.Of("Second"), "/zz/second", string.Empty, Order: 2), new(TestText.Of("First"), "/zz/first", string.Empty, Order: 1)]),
        new NavModule(
            "bb",
            new SerginNavGroup(TestText.Of("Beta"), string.Empty, Order: 5),
            [new(TestText.Of("Same order, later label"), "/bb/b", string.Empty), new(TestText.Of("Same order, earlier label"), "/bb/a", string.Empty)]),
        new NavModule(
            "aa",
            new SerginNavGroup(TestText.Of("Alpha"), string.Empty, Order: 10),
            [new(TestText.Of("Only"), "/aa/only", string.Empty)]),
    ]);

    private static readonly SerginNavGroup SharedGroup = new(TestText.Of("Shared"), string.Empty, Order: 20);

    private static readonly SerginUiModuleCatalog SharedCatalog = new(
    [
        new NavModule("cc", SharedGroup, [new(TestText.Of("B"), "/cc/b", string.Empty, Order: 210)]),
        new NavModule("dd", SharedGroup, [new(TestText.Of("A"), "/dd/a", string.Empty, Order: 200)]),
        new NavModule(
            "ee",
            new SerginNavGroup(TestText.Of("Own"), string.Empty, Order: 30),
            [new(TestText.Of("Only"), "/ee/only", string.Empty)]),
    ]);

    [Fact]
    public void ModulesSharingAGroup_FormOneSection_WithEveryModulesItemsInOrder()
    {
        SerginNavSection shared = SharedCatalog.NavSections.Single(section => section.Group == SharedGroup);
        Assert.Equal(["/dd/a", "/cc/b"], shared.Items.Select(item => item.Href));
        Assert.Equal(2, SharedCatalog.NavSections.Count);
    }

    [Theory]
    [InlineData("/cc/b")]
    [InlineData("/dd/a")]
    public void GroupOf_EitherModulesHref_IsTheSharedGroup(string href)
        => Assert.Same(SharedGroup, SharedCatalog.GroupOf(href));

    [Fact]
    public void Sections_AreOrderedByGroupOrder_ThenTitle()
        => Assert.Equal(["Beta", "Alpha", "Zeta"], Catalog.NavSections.Select(section => section.Group.Title.Default));

    [Fact]
    public void ItemsWithinASection_AreOrderedByOrder_ThenLabel()
    {
        Assert.Equal(
            ["Same order, earlier label", "Same order, later label"],
            Catalog.NavSections.Single(section => section.Group.Title.Default == "Beta").Items.Select(item => item.Label.Default));
        Assert.Equal(
            ["First", "Second"],
            Catalog.NavSections.Single(section => section.Group.Title.Default == "Zeta").Items.Select(item => item.Label.Default));
    }

    [Fact]
    public void NavItems_AreEverySectionsItems_InDrawerOrder()
        => Assert.Equal(
            ["/bb/a", "/bb/b", "/aa/only", "/zz/first", "/zz/second"],
            Catalog.NavItems.Select(item => item.Href));

    [Theory]
    [InlineData("/zz/second", "Zeta")]
    [InlineData("/aa/only", "Alpha")]
    public void GroupOf_AnEntrysHref_IsItsModulesGroup(string href, string title)
        => Assert.Equal(title, Catalog.GroupOf(href)?.Title.Default);

    [Theory]
    [InlineData("/zz/second/details")]
    [InlineData("/")]
    [InlineData("/nowhere")]
    public void GroupOf_AnyOtherHref_IsNull(string href) => Assert.Null(Catalog.GroupOf(href));

    private sealed class NavModule(string schema, SerginNavGroup group, IReadOnlyCollection<SerginNavItem> items)
        : ISerginWebUiModule
    {
        private static Assembly NoComponents => typeof(SerginNavItem).Assembly;

        public string Name => schema;

        public string Schema => schema;

        public Assembly ApplicationAssembly => NoComponents;

        public Assembly ContractsAssembly => NoComponents;

        public Assembly ConfigurationsAssembly => NoComponents;

        public Assembly UiAssembly => NoComponents;

        public SerginNavGroup NavGroup => group;

        public IReadOnlyCollection<SerginNavItem> NavItems => items;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }
}
