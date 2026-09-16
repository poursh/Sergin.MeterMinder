namespace Sergin.MeterMinder.DeviceManagement.Presentation.WebApi.Devices.Endpoints.Create;

// The name predates the DeviceModel entity and means "model of a new device", not "device model". Left as is.
public record NewDeviceModel(string DeviceId, Guid DeviceModelId);
