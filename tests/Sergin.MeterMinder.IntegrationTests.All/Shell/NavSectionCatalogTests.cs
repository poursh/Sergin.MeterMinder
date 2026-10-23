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
            new SerginNavGroup("Zeta", string.Empty, Order: 10),
            [new("Second", "/zz/second", string.Empty, Order: 2), new("First", "/zz/first", string.Empty, Order: 1)]),
        new NavModule(
            "bb",
            new SerginNavGroup("Beta", string.Empty, Order: 5),
            [new("Same order, later label", "/bb/b", string.Empty), new("Same order, earlier label", "/bb/a", string.Empty)]),
        new NavModule(
            "aa",
            new SerginNavGroup("Alpha", string.Empty, Order: 10),
            [new("Only", "/aa/only", string.Empty)]),
    ]);

    [Fact]
    public void Sections_AreOrderedByGroupOrder_ThenTitle()
        => Assert.Equal(["Beta", "Alpha", "Zeta"], Catalog.NavSections.Select(section => section.Group.Title));

    [Fact]
    public void ItemsWithinASection_AreOrderedByOrder_ThenLabel()
    {
        Assert.Equal(
            ["Same order, earlier label", "Same order, later label"],
            Catalog.NavSections.Single(section => section.Group.Title == "Beta").Items.Select(item => item.Label));
        Assert.Equal(
            ["First", "Second"],
            Catalog.NavSections.Single(section => section.Group.Title == "Zeta").Items.Select(item => item.Label));
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
        => Assert.Equal(title, Catalog.GroupOf(href)?.Title);

    [Theory]
    [InlineData("/zz/second/details")]
    [InlineData("/")]
    [InlineData("/nowhere")]
    public void GroupOf_AnyOtherHref_IsNull(string href) => Assert.Null(Catalog.GroupOf(href));

    private sealed class NavModule(string schema, SerginNavGroup group, IReadOnlyCollection<SerginNavItem> items)
        : ISerginWebUiModule
    {
        private static Assembly NoComponents => typeof(SerginNavItem).Assembly;

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
