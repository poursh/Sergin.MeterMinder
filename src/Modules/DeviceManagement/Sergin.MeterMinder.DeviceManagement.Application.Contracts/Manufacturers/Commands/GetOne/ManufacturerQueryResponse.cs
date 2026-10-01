namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;

public sealed record ManufacturerQueryResponse(Guid Id, string Name, string? Address);
