using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Validation;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>The compiled send and the request construction a generated view runs on, against the real Device requests.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RequestRuntimeTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void ValueObjects_PrimitiveOf_ReadsWrappedType()
    {
        Assert.Equal(typeof(string), ValueObjects.PrimitiveOf(typeof(DeviceId)));
        Assert.Equal(typeof(Guid), ValueObjects.PrimitiveOf(typeof(DeviceIntenralId)));
        Assert.Equal(typeof(int), ValueObjects.PrimitiveOf(typeof(int)));
    }

    [Fact]
    public void ValueObjects_WrapUnwrap_RoundTrip()
    {
        object? wrapped = ValueObjects.Wrap(typeof(DeviceId), "MTR-1");

        Assert.Equal(new DeviceId("MTR-1"), wrapped);
        Assert.Equal("MTR-1", ValueObjects.Unwrap(wrapped));
    }

    [Fact]
    public void RequestFactory_ForId_BuildsIdOnlyCommand()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(
            new ReadDeviceProfileCommand(new DeviceIntenralId(id)),
            RequestFactory.ForId(typeof(ReadDeviceProfileCommand))(id));
    }

    [Fact]
    public void RequestFactory_ForForm_BuildsUpdateCommandFromValues()
    {
        var id = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();

        object built = RequestFactory.ForForm(typeof(UpdateDeviceCommand))(
            id,
            new Dictionary<string, object?> { ["DeviceId"] = "MTR-9", ["DeviceModelId"] = model });

        Assert.Equal(
            new UpdateDeviceCommand(new DeviceIntenralId(id), new DeviceId("MTR-9"), new DeviceModelInternalId(model)),
            built);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestFactory_ForForm_BlankValueOfNullableParameterBecomesNull(string blank)
    {
        var id = Guid.CreateVersion7();
        var values = new Dictionary<string, object?> { ["Name"] = "x", ["Address"] = blank };

        UpdateManufacturerCommand update = Assert.IsType<UpdateManufacturerCommand>(RequestFactory.ForForm(typeof(UpdateManufacturerCommand))(id, values));
        CreateManufacturerCommand create = Assert.IsType<CreateManufacturerCommand>(RequestFactory.ForForm(typeof(CreateManufacturerCommand))(Guid.Empty, values));

        Assert.Null(update.Address);
        Assert.Null(create.Address);
        Assert.Equal("x", create.Name.Value);
    }

    [Fact]
    public void RequestFactory_ForForm_AddressWithTextIsKept()
    {
        object built = RequestFactory.ForForm(typeof(CreateManufacturerCommand))(
            Guid.Empty, new Dictionary<string, object?> { ["Name"] = "x", ["Address"] = "Tabriz" });

        Assert.Equal("Tabriz", Assert.IsType<CreateManufacturerCommand>(built).Address!.Value);
    }

    [Fact]
    public void RequestFactory_ForForm_BlankValueOfNonNullableParameterIsNotTurnedIntoNull()
    {
        object built = RequestFactory.ForForm(typeof(CreateDeviceCommand))(
            Guid.Empty, new Dictionary<string, object?> { ["DeviceId"] = "  " });

        Assert.Equal("  ", Assert.IsType<CreateDeviceCommand>(built).DeviceId.Value);
    }

    [Fact]
    public void RequestFactory_ForForm_MissingValuesBecomeNonNullWrappers()
    {
        object built = RequestFactory.ForForm(typeof(CreateDeviceCommand))(Guid.Empty, new Dictionary<string, object?>());

        CreateDeviceCommand command = Assert.IsType<CreateDeviceCommand>(built);
        Assert.NotNull(command.DeviceId);
        Assert.Equal(string.Empty, command.DeviceId.Value);
        Assert.NotNull(command.DeviceModelId);
    }

    [Fact]
    public async Task FormValidator_OnAnEmptyFormBuiltCommand_ReturnsAMessageInsteadOfThrowing()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginFormValidator formValidator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();
        object built = RequestFactory.ForForm(typeof(CreateDeviceCommand))(Guid.Empty, new Dictionary<string, object?>());

        IReadOnlyCollection<string> messages = await formValidator.ValidateAsync(
            Assert.IsType<CreateDeviceCommand>(built), nameof(CreateDeviceCommand.DeviceId));

        Assert.NotEmpty(messages);
    }

    [Fact]
    public void RequestFactory_ForList_BuildsListQueryFromInput()
    {
        object built = RequestFactory.ForList(typeof(GetDeviceListQueryCommand))(
            new ListQueryInput(
                Paggination.Create(5, 2),
                "abc",
                [new FilterData { Id = "manufacturerId", Value = "x", Mode = "equals" }],
                [new SortingData("deviceId", true)]));

        GetDeviceListQueryCommand query = Assert.IsType<GetDeviceListQueryCommand>(built);
        Assert.Equal(5, (int)query.Paggination.Size);
        Assert.Equal(2, (int)query.Paggination.Index);
        Assert.Equal("abc", query.Term?.Value);
        Assert.Equal("manufacturerId", Assert.Single(query.Filtering!.Filters).Id);
        Assert.True(Assert.Single(query.Sorting!.Sorts).Desc);
    }

    [Fact]
    public async Task RequestInvoker_SendVersioned_ReturnsValueAndVersion()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid id = await CatalogSeed.DeviceAsync(dispatcher, $"R-{marker}", model);

        ViewSendResult result = await RequestInvoker.For(typeof(GetDeviceByIdQueryCommand))
            .SendVersionedAsync(dispatcher, RequestFactory.ForId(typeof(GetDeviceByIdQueryCommand))(id), expected: null);

        Assert.False(result.IsError);
        Assert.Equal($"R-{marker}", Assert.IsType<DeviceQueryResponse>(result.Value).DeviceId);
        Assert.NotNull(result.Version);
    }

    [Fact]
    public async Task RequestInvoker_NotFound_ReturnsErrors()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ViewSendResult result = await RequestInvoker.For(typeof(GetDeviceByIdQueryCommand))
            .SendAsync(dispatcher, RequestFactory.ForId(typeof(GetDeviceByIdQueryCommand))(Guid.CreateVersion7()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.Errors[0].Type);
    }

    [Fact]
    public void Runtime_ChildBuilders_BuildFromRootAndChildIds()
    {
        var manufacturer = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();

        Assert.Equal(
            new GetDeviceModelByIdQueryCommand(new ManufacturerId(manufacturer), new DeviceModelInternalId(model)),
            RequestFactory.ForChildId(typeof(GetDeviceModelByIdQueryCommand))(manufacturer, model));

        var rename = (RenameDeviceModelCommand)RequestFactory.ForChildForm(typeof(RenameDeviceModelCommand))(
            manufacturer, model, new Dictionary<string, object?> { ["Name"] = "N" });

        Assert.Equal("N", rename.Name.Value);
        Assert.Equal(manufacturer, rename.RootId.Value);
        Assert.Equal(model, rename.Id.Value);

        var add = (AddDeviceModelCommand)RequestFactory.ForRootForm(typeof(AddDeviceModelCommand))(
            manufacturer, new Dictionary<string, object?> { ["Name"] = "A" });

        Assert.Equal("A", add.Name.Value);
        Assert.Equal(manufacturer, add.RootId.Value);
    }

    [Fact]
    public void ViewModel_SetProperty_HasValueFieldAndBuild()
    {
        CommandView rename = AggregateViewTestRegistry.Build(typeof(EditProbeViewConfiguration)).For<EditProbe>().Commands
            .Single(command => command.Request == typeof(RenameProbeCommand));
        var id = Guid.CreateVersion7();

        Assert.Equal(nameof(RenameProbeCommand.Name), rename.ValueField!.Name);
        Assert.Equal(
            new RenameProbeCommand(new EditProbeId(id), new EditProbeName("x")),
            rename.BuildFromValues!(id, new Dictionary<string, object?> { [nameof(RenameProbeCommand.Name)] = "x" }));
    }

    [Fact]
    public void Runtime_ChildListBuilder_TakesTheRootsId()
    {
        var manufacturer = Guid.CreateVersion7();
        var query = (GetDeviceModelListQueryCommand)RequestFactory.ForChildList(typeof(GetDeviceModelListQueryCommand), typeof(ManufacturerId))(
            manufacturer, new ListQueryInput(Paggination.Create(10, 1), null, [], []));

        Assert.Equal(manufacturer, query.RootId.Value);
    }

    [Fact]
    public void ListBuilder_RequiredParameterOfItsOwn_IsRefused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => RequestFactory.ForList(typeof(ParentedLists.ListTaggedViewProbeNotesQuery)));

        Assert.Contains("is not a list query a view can build", refused.Message, StringComparison.Ordinal);
        Assert.Contains("String tag", refused.Message, StringComparison.Ordinal);
    }
}
