using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;

/// <summary>
/// The Device aggregate's texts: one field per command property and list field a person sees (a list column and a
/// filter chip read the field of their <c>DeviceListFields</c> id), and its name. The texts only the pages show are
/// <c>DeviceUiTexts</c>, in the Blazor RCL, on this category.
/// </summary>
public static class DeviceTexts
{
    public static readonly TextCategory Category = TextCategory.Create("DeviceManagement", "Devices");

    public static readonly FieldText DeviceId = Category.Field("DeviceId", "Device ID");

    public static readonly FieldText DeviceModelId = Category.Field("DeviceModelId", "Model", placeholder: "Select a model");

    public static readonly FieldText DeviceModelName = Category.Field("DeviceModelName", "Model");

    public static readonly FieldText ManufacturerId =
        Category.Field("ManufacturerId", "Manufacturer", placeholder: "Select a manufacturer");

    public static readonly FieldText ManufacturerName = Category.Field("ManufacturerName", "Manufacturer");

    public static readonly FieldText ScopeId = Category.Field("ScopeId", "Scope");

    public static readonly FieldText CreatedAtUtc = Category.Field("CreatedAtUtc", "Created");

    public static readonly TextDefinition Singular = Category.Text("Singular", "Device");

}
