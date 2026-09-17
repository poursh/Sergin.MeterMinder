using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;

public sealed record AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<AddDeviceModelCommandResponse>;
