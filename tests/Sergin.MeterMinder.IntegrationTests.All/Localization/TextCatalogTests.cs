using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.Administration.Settings.Application.Configurations.Languages;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

[Collection(nameof(IntegrationTestCollection))]
public sealed class TextCatalogTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Catalog_CollectsTextsAndFieldParts()
    {
        var catalog = TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(Devices))]);

        Assert.Equal(3, catalog.Entries.Count);
        Assert.True(catalog.TryGet("device-management.devices.new", out TextDefinition? definition));
        Assert.Equal("New device", definition.Default);
        Assert.Equal("Device ID", catalog.FindField(Devices.Category, "DeviceId")!.Label.Default);
        Assert.Single(catalog.FieldsOf(Devices.Category));
    }

    [Fact]
    public void Values_KeysAreCategoryValuesEnumMember()
    {
        TextDefinition draft = LanguageTexts.StatusValues.For(LanguageStatus.Draft);

        Assert.Equal("settings.languages.values.language-status.draft", draft.Key.Value);

        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        TextCatalogEntry entry = Assert.Single(catalog.Entries, entry => entry.Definition.Key == draft.Key);

        Assert.Equal(TextOrigin.Application, entry.Origin);
        Assert.Equal(draft, catalog.FindValue(LanguageTexts.Category, typeof(LanguageStatus), LanguageStatus.Draft));
        Assert.Equal(2, catalog.ValueTextsOf(LanguageTexts.Category, typeof(LanguageStatus)).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => LanguageTexts.StatusValues.For((LanguageStatus)99));
    }

    [Fact]
    public void Values_DuplicateMember_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            TextCategory.Create("DeviceManagement", "Devices").Values(
                (LanguageStatus.Draft, "Draft", null),
                (LanguageStatus.Draft, "Again", null)));

    [Fact]
    public void Catalog_RefusesDuplicateKey()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(Devices), typeof(DevicesAgain))]));

        Assert.Contains("device-management.devices.new", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Devices).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(DevicesAgain).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_RefusesSameKeyTwoDefaults()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(Devices), typeof(DevicesOtherDefault))]));

        Assert.Contains("device-management.devices.new", error.Message, StringComparison.Ordinal);
        Assert.Contains("'New device'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Another device'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_RefusesForeignModule()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            TextCatalog.FromSources([TextSource.FromTypes("UserAccess", TextOrigin.Application, typeof(Devices))]));

        Assert.Contains(typeof(Devices).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("user-access", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_AcceptsCommonFromSharedKernelOnly()
    {
        Assert.True(TextCatalog.FromSources([TextSource.Common]).TryGet("common.actions.save", out _));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(CommonInAModule))]));

        Assert.Contains(typeof(CommonInAModule).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("device-management", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_ResolvesCatalogAtStart()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<TextCatalog>().TryGet("common.actions.save", out _));
    }

    [Fact]
    public void Core_RefusesTwoModulesWithOneName()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ApplicationName"] = "Test",
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=names",
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            builder.AddSerginCore([new NamedModule("a"), new NamedModule("b")]));

        Assert.Contains(NamedModule.SharedName, error.Message, StringComparison.Ordinal);
    }

    private sealed class NamedModule(string schema) : ISerginModule
    {
        public const string SharedName = "X";

        public string Name => SharedName;

        public string Schema => schema;

        public Assembly ApplicationAssembly => typeof(NamedModule).Assembly;

        public Assembly ContractsAssembly => typeof(NamedModule).Assembly;

        public Assembly ConfigurationsAssembly => typeof(NamedModule).Assembly;

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }

    public static class Devices
    {
        public static readonly TextCategory Category = TextCategory.Create("DeviceManagement", "Devices");

        public static TextDefinition New { get; } = Category.Text("New", "New device");

        public static FieldText DeviceId { get; } = Category.Field(nameof(DeviceId), "Device ID", hint: "Unique meter serial.");
    }

    public static class DevicesAgain
    {
        public static TextDefinition New { get; } = Devices.Category.Text("New", "New device");
    }

    public static class DevicesOtherDefault
    {
        public static TextDefinition New { get; } = Devices.Category.Text("New", "Another device");
    }

    public static class CommonInAModule
    {
        public static TextDefinition Save { get; } = TextCategory.Create("Common").Text("Other", "Other");
    }
}
