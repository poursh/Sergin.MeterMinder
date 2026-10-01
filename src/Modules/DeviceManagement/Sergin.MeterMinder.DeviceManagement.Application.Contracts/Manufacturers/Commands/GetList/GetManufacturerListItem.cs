namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;

public sealed record GetManufacturerListItem(Guid Id, string Name, string? Address);
