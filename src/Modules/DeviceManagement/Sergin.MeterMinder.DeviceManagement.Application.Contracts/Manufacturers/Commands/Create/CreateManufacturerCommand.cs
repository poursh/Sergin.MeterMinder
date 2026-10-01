using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;

public sealed record CreateManufacturerCommand(ManufacturerName Name, ManufacturerAddress? Address) : ICommand<CreateManufacturerCommandResponse>;
