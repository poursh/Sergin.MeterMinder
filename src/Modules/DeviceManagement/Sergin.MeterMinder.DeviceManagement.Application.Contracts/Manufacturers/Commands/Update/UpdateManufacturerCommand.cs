using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

[RequiredPermissions("permission.dm.manufacturers.update")]
[RequiresExpectedVersion]
public sealed record UpdateManufacturerCommand(Guid Id, ManufacturerName Name, ManufacturerAddress? Address)
    : ICommand<UpdateManufacturerCommandResponse>;
