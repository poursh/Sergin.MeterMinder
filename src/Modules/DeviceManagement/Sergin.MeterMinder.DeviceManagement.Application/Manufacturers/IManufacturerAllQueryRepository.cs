using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
public interface IManufacturerAllQueryRepository :
    IGetManufacturerListQueryRepository,
    IGetManufacturerQueryRepository,
    IGetDeviceModelQueryRepository,
    IGetDeviceModelListQueryRepository;
