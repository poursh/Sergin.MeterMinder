using Sergin.SharedKernel.Application.Validations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;

/// <summary>Refuses a sort or filter the device list does not declare in <see cref="DeviceListFields"/>.</summary>
internal sealed class GetDeviceListQueryCommandValidator() : ListQueryValidator<GetDeviceListQueryCommand>(DeviceListFields.Set);
