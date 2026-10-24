using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;

/// <summary>
/// What the device list may be sorted and filtered by — the ids a <c>Sorting</c> or <c>Filtering</c> names. The
/// validator refuses anything else and the query repository maps each to its column.
/// </summary>
public static class DeviceListFields
{
    public const string DeviceId = "deviceId";
    public const string DeviceModelName = "deviceModelName";
    public const string ManufacturerName = "manufacturerName";
    public const string ManufacturerId = "manufacturerId";
    public const string DeviceModelId = "deviceModelId";
    public const string ScopeId = "scopeId";
    public const string CreatedAtUtc = "createdAtUtc";

    public static ListFieldSet Set { get; } = ListFieldSet.Create(
        ListField.SortOnly(DeviceId),
        ListField.SortOnly(DeviceModelName),
        ListField.SortOnly(ManufacturerName),
        ListField.FilterOnly(ManufacturerId, ListFieldKind.Identifier, FilteringType.equals),
        ListField.FilterOnly(DeviceModelId, ListFieldKind.Identifier, FilteringType.equals),
        ListField.FilterOnly(ScopeId, ListFieldKind.Identifier, FilteringType.equals),
        ListField.FilterOnly(CreatedAtUtc, ListFieldKind.Instant, FilteringType.between));
}
