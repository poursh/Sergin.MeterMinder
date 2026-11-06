using System.Globalization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

public sealed class TextDeclarationTests
{
    [Fact]
    public void Category_KeysAreKebabUnderModule() =>
        Assert.Equal(
            "device-management.devices.new",
            TextCategory.Create("DeviceManagement", "Devices").Text("New", "New device").Key.Value);

    [Fact]
    public void ChildCategory_Nests() =>
        Assert.Equal(
            "device-management.manufacturers.device-models.add",
            TextCategory.Create("DeviceManagement", "Manufacturers").Child("DeviceModels").Text("Add", "Add model").Key.Value);

    [Fact]
    public void ModuleOnlyCategory() =>
        Assert.Equal(
            "device-management.menu-title",
            TextCategory.Create("DeviceManagement").Text("MenuTitle", "Device Management").Key.Value);

    [Fact]
    public void Field_DerivesThreeKeys()
    {
        FieldText field = TextCategory.Create("DeviceManagement", "Devices")
            .Field("DeviceId", "Device ID", hint: "Unique meter serial.");

        Assert.Equal("device-management.devices.fields.device-id.label", field.Label.Key.Value);
        Assert.Equal("device-management.devices.fields.device-id.hint", field.Hint!.Key.Value);
        Assert.Null(field.Placeholder);
    }

    [Fact]
    public void Placeholders_AreNamed() =>
        Assert.Equal(
            new HashSet<string> { "DeviceId" },
            TextPlaceholders.In("Delete device {DeviceId}? {{literal}}").ToHashSet());

    [Fact]
    public void Format_ReplacesNamed_LeavesUnknown() =>
        Assert.Equal(
            "x and {B}",
            TextPlaceholders.Format("{A} and {B}", new Dictionary<string, object?> { ["A"] = "x" }, CultureInfo.GetCultureInfo("en")));

    [Fact]
    public void TextKey_RejectsSingleSegment() =>
        Assert.Throws<ArgumentException>(() => TextKey.Create("devices"));

    [Fact]
    public void ErrorText_Of_RejectsMalformedKey() =>
        Assert.Throws<ArgumentException>(() => ErrorText.Of("Not A Key"));
}
