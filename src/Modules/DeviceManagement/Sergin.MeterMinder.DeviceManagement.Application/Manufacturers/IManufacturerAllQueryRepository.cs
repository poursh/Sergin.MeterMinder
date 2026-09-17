using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;

namespace Sergin.MeterMinder.DeviceManagement.Application.Manufacturers;
public interface IManufacturerAllQueryRepository :
    IGetManufacturerListQueryRepository,
    IGetManufacturerQueryRepository,
    IGetDeviceModelQueryRepository,
    IGetDeviceModelListQueryRepository;
