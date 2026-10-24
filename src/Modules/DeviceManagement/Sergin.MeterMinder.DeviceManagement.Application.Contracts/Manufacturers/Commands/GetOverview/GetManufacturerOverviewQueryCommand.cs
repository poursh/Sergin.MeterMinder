using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;

/// <summary>One manufacturer's overview figures — the detail page's collapsed overview line.</summary>
public sealed record GetManufacturerOverviewQueryCommand(Guid Id) : IQuery<ManufacturerOverview>;
