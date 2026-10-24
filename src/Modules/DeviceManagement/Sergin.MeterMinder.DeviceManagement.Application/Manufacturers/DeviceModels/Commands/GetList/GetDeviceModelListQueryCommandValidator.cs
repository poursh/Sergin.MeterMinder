using Sergin.SharedKernel.Application.Validations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;

/// <summary>Refuses a sort or filter the model list does not declare in <see cref="DeviceModelListFields"/>.</summary>
internal sealed class GetDeviceModelListQueryCommandValidator() : ListQueryValidator<GetDeviceModelListQueryCommand>(DeviceModelListFields.Set);
