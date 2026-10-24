namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;

/// <param name="Devices">Live devices in the current subtree, within the caller's reach.</param>
/// <param name="ModelsInUse">Distinct models those devices use.</param>
/// <param name="ModelsTotal">Live models in the catalog, which belongs to no scope.</param>
/// <param name="Manufacturers">Live manufacturers in the catalog.</param>
/// <param name="AddedLast30Days">Of <paramref name="Devices"/>, those created in the last 30 days.</param>
public sealed record DeviceOverview(int Devices, int ModelsInUse, int ModelsTotal, int Manufacturers, int AddedLast30Days);
