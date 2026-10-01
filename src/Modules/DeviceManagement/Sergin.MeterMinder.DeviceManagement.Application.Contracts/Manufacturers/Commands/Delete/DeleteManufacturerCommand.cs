using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;

public sealed record DeleteManufacturerCommand(Guid Id) : ICommand<DeleteManufacturerCommandResponse>;
