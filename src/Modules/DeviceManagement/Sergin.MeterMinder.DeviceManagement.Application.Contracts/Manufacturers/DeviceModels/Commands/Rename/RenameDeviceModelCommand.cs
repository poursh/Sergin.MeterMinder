using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;

public sealed record RenameDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id, DeviceModelName Name)
    : ICommand<RenameDeviceModelCommandResponse>;
