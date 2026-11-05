using System.Reflection;
using System.Text.RegularExpressions;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Navigation;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// Spec decision 8: a literal cannot slip in. The five records a module labels its navigation, breadcrumbs and actions
/// with take a label only as a declared text; the one string a breadcrumb takes is user data, through
/// <see cref="SerginBreadcrumb.Data"/>, which is a factory and not a constructor.
/// </summary>
public sealed partial class LabelMemberTests
{
    public static TheoryData<Type> Records => new(
        typeof(SerginNavItem),
        typeof(SerginNavGroup),
        typeof(SerginPageAction),
        typeof(SerginBulkAction<object>),
        typeof(SerginBreadcrumb));

    [Theory]
    [MemberData(nameof(Records))]
    public void NoConstructor_TakesALabelAsAString(Type record)
    {
        string[] offending =
        [
            .. record.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => parameter.ParameterType == typeof(string) && LabelLike().IsMatch(parameter.Name!))
                .Select(parameter => parameter.Name!),
        ];

        Assert.Empty(offending);
    }

    [Theory]
    [MemberData(nameof(Records))]
    public void NoProperty_HoldsALabelAsAString(Type record)
    {
        string[] offending =
        [
            .. record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string) && LabelLike().IsMatch(property.Name))
                .Select(property => property.Name),
        ];

        Assert.Empty(offending);
    }

    [GeneratedRegex("label|title|text|caption|placeholder|message|name", RegexOptions.IgnoreCase)]
    private static partial Regex LabelLike();
}
