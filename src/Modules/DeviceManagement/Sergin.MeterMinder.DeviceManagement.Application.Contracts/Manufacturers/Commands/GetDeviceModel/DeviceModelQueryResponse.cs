namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;

public sealed record DeviceModelQueryResponse(Guid Id, Guid ManufacturerId, string ManufacturerName, string Name);
