using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;

// Guarded by the manufacturer's version and permission: removing a model changes the Manufacturer aggregate.
[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record RemoveDeviceModelCommand(ManufacturerId ManufacturerId, Guid Id)
    : ICommand<RemoveDeviceModelCommandResponse>;
