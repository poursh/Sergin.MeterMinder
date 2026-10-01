namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;

public sealed record GetDeviceListItem(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName);
