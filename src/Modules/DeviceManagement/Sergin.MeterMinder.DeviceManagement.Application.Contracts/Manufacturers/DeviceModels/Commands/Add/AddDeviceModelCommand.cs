using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;

public sealed record AddDeviceModelCommand(ManufacturerId ManufacturerId, DeviceModelName Name) : ICommand<AddDeviceModelCommandResponse>;
