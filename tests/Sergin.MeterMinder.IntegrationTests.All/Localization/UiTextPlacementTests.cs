using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.Settings.Presentation.Blazor.Languages;
using Sergin.Administration.Settings.Presentation.Blazor.Translations;
using Sergin.Administration.UserAccess.Presentation.Blazor.Roles;
using Sergin.Administration.UserAccess.Presentation.Blazor.Scopes;
using Sergin.Administration.UserAccess.Presentation.Blazor.Users;
using Sergin.Administration.UserAccess.Presentation.Blazor.Users.Assignments;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Layout;
using Sergin.SharedKernel.Presentation.Blazor.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// Texts only the Blazor UI shows are declared in the presentation layer (spec 2026-11-07): each module's RCL and
/// SharedKernel's Blazor kit. Their keys are the keys they had in the Application layer, so stored translations
/// keep applying.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class UiTextPlacementTests(SerginWebApiFactory<Program> factory)
{
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>One moved key per moved class, with the type that declares it now.</summary>
    public static TheoryData<string, Type> MovedKeys => new()
    {
        { "common.navigation.home", typeof(ShellTexts.Navigation) },
        { "user-access.users.new", typeof(UserUiTexts) },
        { "user-access.users.assignments.revoke", typeof(AssignmentUiTexts) },
        { "user-access.roles.delete-confirm", typeof(RoleUiTexts) },
        { "user-access.scopes.tree-root", typeof(ScopeUiTexts) },
        { "settings.languages.publish", typeof(LanguageUiTexts) },
        { "settings.translations.statuses.orphaned", typeof(TranslationUiTexts) },
        { "device-management.menu-title", typeof(DeviceManagementUiTexts) },
        { "device-management.devices.delete-confirm", typeof(DeviceUiTexts) },
        { "device-management.manufacturers.overview.models", typeof(ManufacturerUiTexts) },
        { "device-management.manufacturers.device-models.remove-confirm", typeof(DeviceModelUiTexts) },
    };

    [Fact]
    public void ShellTexts_LivesInTheBlazorKit() =>
        Assert.Equal(typeof(SerginMainLayout).Assembly, typeof(ShellTexts).Assembly);

    [Fact]
    public void UiAssemblies_DeclareNoFieldOrErrorText()
    {
        string[] offenders =
        [
            .. DeclaringTypes()
                .SelectMany(type => Members(type)
                    .Where(member => member.Type == typeof(FieldText) || member.Type == typeof(ErrorTextPair))
                    .Select(member => $"{type.FullName}.{member.Name}")),
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryUiAssemblyText_IsInCatalog()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        string[] missing =
        [
            .. DeclaringTypes()
                .SelectMany(type => Members(type)
                    .Where(member => member.Type == typeof(TextDefinition))
                    .Select(member => (TextDefinition)member.Value()!))
                .Where(definition => !catalog.TryGet(definition.Key.Value, out _))
                .Select(definition => definition.Key.Value),
        ];

        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(MovedKeys))]
    public void UiText_KeepsItsKey(string key, Type declaringType)
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        TextCatalogEntry entry = Assert.Single(catalog.Entries, entry => entry.Definition.Key.Value == key);

        Assert.Equal(declaringType, entry.DeclaringType);
    }

    // Every public static class (nested ones included) of each module's UI assembly and SharedKernel's Blazor kit.
    private IEnumerable<Type> DeclaringTypes() =>
        factory.Services.GetRequiredService<SerginUiModuleCatalog>().Modules
            .Select(module => module.UiAssembly)
            .Append(typeof(SerginMainLayout).Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: true, IsSealed: true } && (type.IsPublic || type.IsNestedPublic) && !type.IsGenericTypeDefinition);

    private static IEnumerable<(string Name, Type Type, Func<object?> Value)> Members(Type type) =>
        type.GetFields(StaticMembers).Select(field => (field.Name, field.FieldType, (Func<object?>)(() => field.GetValue(null))))
            .Concat(type.GetProperties(StaticMembers)
                .Where(property => property.GetIndexParameters().Length == 0)
                .Select(property => (property.Name, property.PropertyType, (Func<object?>)(() => property.GetValue(null)))));
}
