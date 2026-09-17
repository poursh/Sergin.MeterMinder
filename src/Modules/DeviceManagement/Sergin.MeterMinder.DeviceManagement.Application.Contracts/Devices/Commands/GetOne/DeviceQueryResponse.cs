namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

public sealed record DeviceQueryResponse(Guid Id, string DeviceId, Guid DeviceModelId, string DeviceModelName);
