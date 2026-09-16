using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Application.Users.Commands.ProvisionExternalUser;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Validation;

/// <summary>
/// The repository-backed validation rules end to end. <c>MustExistIn</c> and <c>MustBeUniqueIn</c>
/// (<c>Sergin.SharedKernel.Application.Validations</c>) run inside ValidationPipelineBehavior, in the
/// request's scope, so a reference to a missing aggregate or entity or a taken alternate key comes back as an
/// ErrorOr validation error naming the command property — where it used to be a raw Postgres exception
/// from the foreign key, or a silent duplicate. The rules are advisory: the last two tests pin the two
/// things underneath them, that <c>ExistsAsync</c> answers without loading anything and that the unique
/// index still refuses a duplicate when the validator is bypassed, which is what a race between the
/// validator's check and SaveChangesAsync amounts to. Dispatched through ISerginDispatcher from a scope,
/// exactly as a Blazor page does.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RepositoryRuleTests(SerginWebApiFactory<Program> factory)
{
    private const string DeviceIdTakenMessage = "'Device Id' is already in use.";
    private const string DeviceModelMissingMessage = "'Device Model Id' must refer to an existing DeviceModel.";

    [Fact]
    public async Task CreateDevice_UnknownDeviceModel_IsRefusedNotThrown()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceId deviceId = new($"device-{Guid.CreateVersion7()}");

        // Well-formed, so the shape rule passes and the existence rule is what runs. Without the rule this
        // send would throw DbUpdateException out of the handler's SaveChangesAsync (FK 23503).
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(deviceId, new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.True(created.IsError, "A device-model id that matches no row must be refused.");
        Error error = Assert.Single(created.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateDeviceCommand.DeviceModelId), error.Code);
        Assert.Equal(DeviceModelMissingMessage, error.Description);

        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        Assert.DoesNotContain(list.Value.Data, item => item.DeviceId == deviceId.Value);
    }

    [Fact]
    public async Task CreateDevice_DuplicateDeviceId_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceModelInternalId modelId = await CreateDeviceModelAsync(dispatcher);
        DeviceId deviceId = new($"device-{Guid.CreateVersion7()}");

        ErrorOr<CreateDeviceCommandResponse> first =
            await dispatcher.SendAsync(new CreateDeviceCommand(deviceId, modelId));

        Assert.False(first.IsError, first.IsError ? first.FirstError.Description : string.Empty);

        ErrorOr<CreateDeviceCommandResponse> second =
            await dispatcher.SendAsync(new CreateDeviceCommand(deviceId, modelId));

        Assert.True(second.IsError, "A device id already carried by a row must be refused.");
        Error error = Assert.Single(second.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateDeviceCommand.DeviceId), error.Code);
        Assert.Equal(DeviceIdTakenMessage, error.Description);
    }

    /// <summary>
    /// Each repository rule sits behind a <c>When</c> on its own shape rule, and the rules are
    /// independent of each other: an empty device id reports its shape error and no uniqueness error,
    /// while the device-model rule — whose shape rule passed — still runs and reports. What this pins
    /// is the errors a caller sees; it cannot observe that the uniqueness query was never sent, since
    /// <c>IsTakenAsync("")</c> would answer false either way.
    /// </summary>
    [Fact]
    public async Task CreateDevice_EmptyDeviceIdAndUnknownDeviceModel_ReportsShapeAndExistenceNotUniqueness()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(string.Empty), new DeviceModelInternalId(Guid.CreateVersion7())));

        Assert.True(created.IsError, "An empty device id and an unknown device model must both be refused.");
        Assert.All(created.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));

        Assert.Contains(created.Errors, error =>
            error.Code == nameof(CreateDeviceCommand.DeviceId) && error.Description != DeviceIdTakenMessage);
        Assert.DoesNotContain(created.Errors, error => error.Description == DeviceIdTakenMessage);
        Assert.Contains(created.Errors, error =>
            error.Code == nameof(CreateDeviceCommand.DeviceModelId) && error.Description == DeviceModelMissingMessage);
    }

    [Fact]
    public async Task CreateUser_DuplicateUserName_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        UserName userName = new($"user-{Guid.CreateVersion7()}");

        ErrorOr<CreateUserCommandResponse> first = await dispatcher.SendAsync(new CreateUserCommand(userName));

        Assert.False(first.IsError, first.IsError ? first.FirstError.Description : string.Empty);

        ErrorOr<CreateUserCommandResponse> second = await dispatcher.SendAsync(new CreateUserCommand(userName));

        Assert.True(second.IsError, "A username already carried by a row must be refused.");
        Error error = Assert.Single(second.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateUserCommand.UserName), error.Code);
        Assert.Equal("'User Name' is already in use.", error.Description);
    }

    /// <summary>
    /// Provisioning is find-or-create inside the OIDC callback: a local user who predates external
    /// sign-in is matched on the username and linked to the provider subject, so the username is
    /// "taken" by the very row the handler is about to find. That is why
    /// ProvisionExternalUserCommandValidator carries no uniqueness rule, and this is the test that fails
    /// first if one is ever added.
    /// </summary>
    [Fact]
    public async Task ProvisionExternalUser_ExistingUserName_IsNotRefusedAndLinksTheUser()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        UserName userName = new($"local-{Guid.CreateVersion7()}");

        ErrorOr<CreateUserCommandResponse> created = await dispatcher.SendAsync(new CreateUserCommand(userName));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        ErrorOr<ProvisionExternalUserCommandResponse> provisioned = await dispatcher.SendAsync(
            new ProvisionExternalUserCommand(
                new ExternalUserId(Guid.CreateVersion7().ToString()), userName, Email: null, "Test", "User"));

        Assert.False(provisioned.IsError, provisioned.IsError ? provisioned.FirstError.Description : string.Empty);
        Assert.Equal(created.Value.Id, provisioned.Value.UserId);
    }

    [Fact]
    public async Task ExistsAsync_AnswersWithoutTracking()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        Assert.False(await ManufacturerExistsAsync(new ManufacturerId(Guid.CreateVersion7())));

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        Assert.True(await ManufacturerExistsAsync(manufacturerId));
    }

    /// <summary>
    /// The validator's check is advisory. Inserting straight through the repository — no pipeline, no
    /// validator — is what a concurrent duplicate that slipped between the check and the save looks like,
    /// and the unique index on dm.device.device_id refuses it. It still surfaces as DbUpdateException
    /// over a 23505, not as ErrorOr: translating Postgres SqlStates is a separate, cross-cutting slice.
    /// </summary>
    [Fact]
    public async Task UniqueIndex_IsTheGuaranteeWhenTheValidatorIsBypassed()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        DeviceModelInternalId modelId = await CreateDeviceModelAsync(dispatcher);
        DeviceId deviceId = new($"device-{Guid.CreateVersion7()}");

        ErrorOr<CreateDeviceCommandResponse> created =
            await dispatcher.SendAsync(new CreateDeviceCommand(deviceId, modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        // The dispatcher's sends each ran in a scope of their own, so this scope's DbContext tracks only
        // the duplicate inserted here.
        IDeviceRepository devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        IDeviceManagementUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IDeviceManagementUnitOfWork>();

        devices.Insert(Device.Create(deviceId, modelId));

        DbUpdateException exception =
            await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());

        PostgresException postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> CreateDeviceModelAsync(ISerginDispatcher dispatcher)
    {
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);

        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Id);
    }

    /// <summary>
    /// Resolves the repository and the DbContext it works on from one fresh scope, so the change tracker
    /// inspected afterwards has seen nothing but this one call. The point over
    /// <c>await GetAsync(id) is not null</c>: the answer arrives with nothing loaded.
    /// </summary>
    private async Task<bool> ManufacturerExistsAsync(ManufacturerId manufacturerId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IManufacturerRepository manufacturers = scope.ServiceProvider.GetRequiredService<IManufacturerRepository>();
        DbContext context = Assert.IsAssignableFrom<DbContext>(
            scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

        bool exists = await manufacturers.ExistsAsync(manufacturerId);

        Assert.Empty(context.ChangeTracker.Entries());

        return exists;
    }
}
