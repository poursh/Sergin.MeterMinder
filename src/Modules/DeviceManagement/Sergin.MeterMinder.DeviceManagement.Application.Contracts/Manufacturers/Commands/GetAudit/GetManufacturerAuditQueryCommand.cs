using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetAudit;

/// <summary>A manufacturer's audit stamps — the detail page's audit popover.</summary>
public sealed record GetManufacturerAuditQueryCommand(Guid Id) : IQuery<AuditInfo>;
