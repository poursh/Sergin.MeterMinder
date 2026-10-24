using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOverview;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
public interface IManufacturerAllQueryRepository :
    IGetManufacturerListQueryRepository,
    IGetManufacturerQueryRepository,
    IGetManufacturerOverviewQueryRepository,
    IGetManufacturerAuditQueryRepository;
