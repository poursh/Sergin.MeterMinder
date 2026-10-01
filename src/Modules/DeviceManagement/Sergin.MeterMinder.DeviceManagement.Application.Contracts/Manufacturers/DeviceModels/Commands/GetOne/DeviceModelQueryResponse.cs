namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;

public sealed record DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name);
