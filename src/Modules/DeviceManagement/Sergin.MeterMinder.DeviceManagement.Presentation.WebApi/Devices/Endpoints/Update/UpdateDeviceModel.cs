namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Update;

// Like NewDeviceModel, "model of an updated device", not "device model".
public record UpdateDeviceModel(string DeviceId, Guid DeviceModelId);
