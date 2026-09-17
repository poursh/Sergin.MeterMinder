using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Validation;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Application.Users.Commands.GetOne;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Validation;

/// <summary>
/// <c>ISerginFormValidator</c> end to end: the seam a <c>MudForm</c> binds its <c>Validation</c> to, which
/// runs the pipeline's own <c>IValidator&lt;TCommand&gt;</c> for one property in a fresh scope. What it pins:
/// the property filter honours the <c>OverridePropertyName</c> every validator sets on its <c>.Value</c>
/// rule (otherwise the field would go quiet while typing), a repository-backed rule reads committed rows
/// through that fresh scope, and a request without a validator yields nothing rather than throwing.
/// Resolved from a scope, as a Blazor page resolves it from its circuit.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class FormValidatorTests(SerginWebApiFactory<Program> factory)
{
    private const string UserNameTakenMessage = "'User Name' is already in use.";

    [Fact]
    public async Task ValidateAsync_RunsOnlyTheNamedPropertysRules()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginFormValidator formValidator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();

        // Both properties are invalid; only DeviceId is asked about. The DeviceId rules are written on
        // .Value with OverridePropertyName(nameof(DeviceId)), which is what the filter has to match.
        IReadOnlyCollection<string> messages = await formValidator.ValidateAsync(
            new CreateDeviceCommand(new DeviceId(string.Empty), new DeviceModelInternalId(Guid.Empty)),
            nameof(CreateDeviceCommand.DeviceId));

        string message = Assert.Single(messages);
        Assert.Contains("Device Id", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Device Model", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_RepositoryRule_SeesCommittedRows()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ISerginFormValidator formValidator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();

        UserName userName = new($"form-{Guid.CreateVersion7()}");

        ErrorOr<CreateUserCommandResponse> created = await dispatcher.SendAsync(new CreateUserCommand(userName));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        // The rule on the wrapper (MustBeUniqueIn) carries the member name itself, no override needed.
        // The scope the validator opens is fresh, so the row the dispatcher committed is what it reads.
        IReadOnlyCollection<string> messages = await formValidator.ValidateAsync(
            new CreateUserCommand(userName), nameof(CreateUserCommand.UserName));

        Assert.Equal([UserNameTakenMessage], messages);
    }

    [Fact]
    public async Task ValidateAsync_RequestWithoutValidator_IsEmpty()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginFormValidator formValidator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();

        // Queries have no validators — nothing to run, nothing thrown.
        IReadOnlyCollection<string> messages = await formValidator.ValidateAsync(
            new GetUserByIdQueryCommand(Guid.CreateVersion7()), nameof(GetUserByIdQueryCommand.Id));

        Assert.Empty(messages);
    }

    [Fact]
    public async Task RulesFor_IgnoresTheFormModelAndCallsThrough()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginFormValidator formValidator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();

        // The page's ToCommand(): MudForm hands over its own form model, which the adapter never reads.
        Func<object, string, Task<IEnumerable<string>>> rules =
            formValidator.RulesFor(() => new CreateUserCommand(new UserName(string.Empty)));

        IEnumerable<string> messages = await rules(new object(), nameof(CreateUserCommand.UserName));

        string message = Assert.Single(messages);
        Assert.Contains("User Name", message, StringComparison.Ordinal);
    }
}
