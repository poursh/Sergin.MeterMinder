using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;

public sealed record RemoveDeviceModelCommand(ManufacturerId RootId, DeviceModelInternalId Id)
    : ICommand<RemoveDeviceModelCommandResponse>,
        IRemoveChildCommand<Manufacturer, ManufacturerId, DeviceModel, DeviceModelInternalId>, ISelectable;
