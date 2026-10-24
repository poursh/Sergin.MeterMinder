namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;

/// <param name="DeviceCount">Live devices using the model, in every scope — a model in use cannot be removed.</param>
public sealed record GetDeviceModelListItem(Guid Id, string Name, int DeviceCount);
