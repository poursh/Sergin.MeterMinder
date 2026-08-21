using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices;
public interface IDeviceAllQueryRepositoriy : IGetDeviceListQueryRepository, IGetDeviceQueryRepository;
