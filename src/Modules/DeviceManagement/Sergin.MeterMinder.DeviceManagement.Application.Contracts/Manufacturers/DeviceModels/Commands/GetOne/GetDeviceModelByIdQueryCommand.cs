using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;

// Keyed by both ids: a model addressed under the wrong manufacturer is NotFound, not served, so the nested
// route /manufacturers/{manufacturerId}/models/{modelId} carries no ignored segment.
[RequiredPermissions("permission.dm.manufacturers.read")]
public sealed record GetDeviceModelByIdQueryCommand(Guid ManufacturerId, Guid Id) : IQuery<DeviceModelQueryResponse>;
