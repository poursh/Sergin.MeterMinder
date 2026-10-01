using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;

public sealed record RemoveDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id)
    : ICommand<RemoveDeviceModelCommandResponse>;
