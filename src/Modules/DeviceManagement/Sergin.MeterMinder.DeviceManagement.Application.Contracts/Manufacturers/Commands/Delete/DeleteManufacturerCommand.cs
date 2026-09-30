using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;

[RequiredPermissions("permission.dm.manufacturers.delete")]
[RequiresExpectedVersion]
public sealed record DeleteManufacturerCommand(Guid Id) : ICommand<DeleteManufacturerCommandResponse>;
