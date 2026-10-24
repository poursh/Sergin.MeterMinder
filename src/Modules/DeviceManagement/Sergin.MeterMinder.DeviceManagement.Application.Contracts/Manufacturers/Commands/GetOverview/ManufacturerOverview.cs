namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;

/// <param name="Models">The manufacturer's live models.</param>
/// <param name="Devices">
/// Live devices using any of them, in every scope: the catalog belongs to no scope, and this is the number that
/// decides whether a model can be removed, which the remove rule also counts in every scope.
/// </param>
public sealed record ManufacturerOverview(int Models, int Devices);
