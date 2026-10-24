using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOverview;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices;
public interface IDeviceAllQueryRepositoriy :
    IGetDeviceListQueryRepository,
    IGetDeviceQueryRepository,
    IGetDeviceOverviewQueryRepository,
    IGetDeviceAuditQueryRepository;
