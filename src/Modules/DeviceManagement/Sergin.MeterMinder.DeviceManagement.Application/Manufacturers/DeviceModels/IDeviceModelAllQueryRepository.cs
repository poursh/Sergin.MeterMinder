using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels;
public interface IDeviceModelAllQueryRepository :
    IGetDeviceModelQueryRepository,
    IGetDeviceModelListQueryRepository;
