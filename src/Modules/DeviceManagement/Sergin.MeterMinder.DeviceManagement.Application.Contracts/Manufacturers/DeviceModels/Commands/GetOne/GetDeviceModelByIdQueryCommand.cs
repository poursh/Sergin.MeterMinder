using Sergin.SharedKernel.Application.Commands.Queries;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;

// Keyed by both ids: a model addressed under the wrong manufacturer is NotFound, not served, so the nested
// route /manufacturers/{manufacturerId}/models/{modelId} carries no ignored segment.
public sealed record GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) : IQuery<DeviceModelQueryResponse>;
