using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;

/// <summary>What the manufacturer list may be sorted by; it has no filters, and its search reads the name.</summary>
public static class ManufacturerListFields
{
    public const string Name = "name";

    public static ListFieldSet Set { get; } = ListFieldSet.Create(ListField.SortOnly(Name));
}
