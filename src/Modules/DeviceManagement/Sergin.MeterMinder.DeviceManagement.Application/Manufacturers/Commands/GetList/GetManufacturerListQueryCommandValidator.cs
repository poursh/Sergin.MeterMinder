using Sergin.SharedKernel.Application.Validations;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;

/// <summary>Refuses a sort or filter the manufacturer list does not declare in <see cref="ManufacturerListFields"/>.</summary>
internal sealed class GetManufacturerListQueryCommandValidator() : ListQueryValidator<GetManufacturerListQueryCommand>(ManufacturerListFields.Set);
