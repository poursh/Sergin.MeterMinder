using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;

/// <summary>A device's audit stamps — the detail page's information dialog. Not found under the same rules as GetOne.</summary>
public sealed record GetDeviceAuditQueryCommand(Guid Id) : IQuery<AuditInfo>;
