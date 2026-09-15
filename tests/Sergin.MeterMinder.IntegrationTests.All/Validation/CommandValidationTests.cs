using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Application.Users.Commands.GetList;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Validation;

/// <summary>
/// The validation pipe end to end: a module's validator is found by AddSerginCore's scan, run by
/// ValidationPipelineBehavior ahead of the handler, and its failures come back as ErrorOr validation
/// errors naming the command property — with nothing written. Dispatched through ISerginDispatcher
/// from a scope, exactly as a Blazor page does.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CommandValidationTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task CreateUser_OverLongUserName_IsRefusedAndNotPersisted()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        // Unique, so the read-back can look for exactly this one; over the limit once the prefix is on.
        string userName = $"{Guid.CreateVersion7()}-{new string('x', UserName.MaxLength)}";

        ErrorOr<CreateUserCommandResponse> created =
            await dispatcher.SendAsync(new CreateUserCommand(new UserName(userName)));

        Assert.True(created.IsError, "A username over UserName.MaxLength must be refused.");
        Assert.All(created.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));
        Assert.Contains(created.Errors, error => error.Code == nameof(CreateUserCommand.UserName));

        ErrorOr<ListQueryResponse<GetUserListItem>> list =
            await dispatcher.SendAsync(new GetUserListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        Assert.DoesNotContain(list.Value.Data, item => item.UserName == userName);
    }

    [Fact]
    public async Task CreateUser_EmptyUserName_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateUserCommandResponse> created =
            await dispatcher.SendAsync(new CreateUserCommand(new UserName(string.Empty)));

        Assert.True(created.IsError, "An empty username must be refused.");
        Assert.Equal(ErrorType.Validation, created.FirstError.Type);
        Assert.Equal(nameof(CreateUserCommand.UserName), created.FirstError.Code);
        Assert.False(
            string.IsNullOrWhiteSpace(created.FirstError.Description),
            "The message must survive into Description.");
    }

    [Fact]
    public async Task CreateDevice_EmptyDeviceIdAndManufacturer_ReportsBothAtOnce()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(string.Empty), new ManufacturerId(Guid.Empty)));

        Assert.True(created.IsError, "An empty device id and an empty manufacturer must both be refused.");
        Assert.All(created.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));

        // Both rules fail in one pass, and each failure names the command property — not "DeviceId.Value".
        Assert.Contains(created.Errors, error => error.Code == nameof(CreateDeviceCommand.DeviceId));
        Assert.Contains(created.Errors, error => error.Code == nameof(CreateDeviceCommand.ManufacturerId));
    }

    [Fact]
    public void Validators_AreDiscoveredFromEveryLocalModule()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        // One from each module: the scan runs per localModules entry, so both assemblies must contribute.
        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<CreateUserCommand>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<CreateDeviceCommand>>());
    }
}
