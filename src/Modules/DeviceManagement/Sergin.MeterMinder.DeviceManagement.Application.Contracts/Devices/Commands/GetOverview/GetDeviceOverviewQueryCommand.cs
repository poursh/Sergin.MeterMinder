using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;

/// <summary>The device list's overview figures, for the devices the caller can see in the current scope.</summary>
public sealed record GetDeviceOverviewQueryCommand : IQuery<DeviceOverview>;
