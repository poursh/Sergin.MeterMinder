using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;

// Guarded by the manufacturer's version and permission: renaming a model changes the Manufacturer aggregate.
[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record RenameDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id, DeviceModelName Name)
    : ICommand<RenameDeviceModelCommandResponse>;
