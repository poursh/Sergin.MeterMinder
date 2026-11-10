using System.Globalization;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Application.Validations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Repositories;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.Administration.UserAccess.Application.Configurations.Scopes;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.Administration.UserAccess.Domain.Roles;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// FluentValidation's messages through the text catalog (spec decisions 14 and 15): the field name in a message is the
/// label of the command's <see cref="FieldText"/>, found through the category its configuration names (or its bound
/// root's), and the message template is the <c>common.validation.*</c> key, whose default is FluentValidation's own
/// English. Each test builds its own host over a fake translation store; every send goes through ISerginDispatcher, so
/// the validator runs inside the pipeline exactly as for a Blazor page.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ValidationTextTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private readonly TestTranslationStore store = new();

    private WebApplicationFactory<Program> host = default!;

    public Task InitializeAsync()
    {
        host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITranslationStore>(store);
            services.AddSingleton(TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(ValidationTestTexts)));
            services.AddSingleton(ModuleConfigurationSource.FromTypes(
                typeof(NamedThingCommandConfiguration), typeof(UniqueThingCommandConfiguration), typeof(ThingRootConfiguration)));
            services.AddScoped<IValidator<NamedThingCommand>, NamedThingCommandValidator>();
            services.AddScoped<IValidator<UniqueThingCommand>, UniqueThingCommandValidator>();
            services.AddSingleton<IUniqueKeyRepository<ThingName>, TakenThingNames>();
            services.AddScoped<IValidator<NamedReferenceThingCommand>, NamedReferenceThingCommandValidator>();
            services.AddTransient<IRequestHandler<NamedReferenceThingCommand, ErrorOr<Guid>>, EchoHandler<NamedReferenceThingCommand>>();
            services.AddSingleton<IRepository<ThingRoot, Guid>, NoThings>();
            services.AddTransient<IRequestHandler<NamedThingCommand, ErrorOr<Guid>>, EchoHandler<NamedThingCommand>>();
            services.AddTransient<IRequestHandler<UniqueThingCommand, ErrorOr<Guid>>, EchoHandler<UniqueThingCommand>>();
        }));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task DisplayName_ComesFromFieldText()
    {
        using CultureScope culture = new("en");

        Error error = await SendAndFailAsync(new NamedThingCommand(string.Empty));

        Assert.Equal(nameof(NamedThingCommand.Name), error.Code);
        Assert.Equal("'Display name' must not be empty.", error.Description);
    }

    [Fact]
    public async Task DisplayName_Translated()
    {
        store.Set("de", ValidationTestTexts.Name.Label.Key.Value, "Anzeigename", ValidationTestTexts.Name.Label.Default);
        store.Set("de", "common.validation.not-empty-validator", "'{PropertyName}' darf nicht leer sein.", "'{PropertyName}' must not be empty.");
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Error error = await SendAndFailAsync(new NamedThingCommand(string.Empty));

        Assert.Equal("'Anzeigename' darf nicht leer sein.", error.Description);
    }

    [Fact]
    public async Task MustBeUniqueIn_UsesKey()
    {
        Assert.Equal("common.validation.must-be-unique-in", CommonValidationTexts.MustBeUniqueIn.Key.Value);
        Assert.Equal("common.validation.must-exist-in", CommonValidationTexts.MustExistIn.Key.Value);

        using (new CultureScope("en"))
        {
            Error english = await SendAndFailAsync(new UniqueThingCommand(new ThingName(TakenThingNames.Taken)));

            // The display name comes through the bound root's category: the configuration names no category itself.
            Assert.Equal("'Display name' is already in use.", english.Description);
        }

        store.Set("de", ValidationTestTexts.Name.Label.Key.Value, "Anzeigename", ValidationTestTexts.Name.Label.Default);
        store.Set("de", CommonValidationTexts.MustBeUniqueIn.Key.Value, "'{PropertyName}' ist bereits vergeben.", CommonValidationTexts.MustBeUniqueIn.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Error error = await SendAndFailAsync(new UniqueThingCommand(new ThingName(TakenThingNames.Taken)));

        Assert.Equal(nameof(UniqueThingCommand.Name), error.Code);
        Assert.Equal("'Anzeigename' ist bereits vergeben.", error.Description);
    }

    [Fact]
    public async Task MustExistIn_WithADeclaredName_NamesTheAggregateInTheCallersCulture()
    {
        using (new CultureScope("en"))
        {
            Error english = await SendAndFailAsync(new NamedReferenceThingCommand(Guid.CreateVersion7()));

            Assert.Equal("'Thing Id' must refer to an existing thing.", english.Description);
        }

        store.Set("de", ValidationTestTexts.Thing.Key.Value, "Ding", ValidationTestTexts.Thing.Default);
        store.Set("de", CommonValidationTexts.MustExistIn.Key.Value, "'{PropertyName}' muss auf ein vorhandenes {AggregateTypeName} verweisen.", CommonValidationTexts.MustExistIn.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Error error = await SendAndFailAsync(new NamedReferenceThingCommand(Guid.CreateVersion7()));

        Assert.Equal("'Thing Id' muss auf ein vorhandenes Ding verweisen.", error.Description);
    }

    [Fact]
    public async Task CreateDevice_UnknownModel_NamesTheModelWithItsDeclaredText()
    {
        store.Set("de", DeviceModelTexts.Singular.Key.Value, "Gerätemodell", DeviceModelTexts.Singular.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        IReadOnlyList<Error> errors = await SendAndFailAllAsync(new CreateDeviceCommand(
            new DeviceId($"MTR-{Guid.CreateVersion7():N}"[..20]), new DeviceModelInternalId(Guid.CreateVersion7())));

        Error error = Assert.Single(errors, error => error.Code == nameof(CreateDeviceCommand.DeviceModelId));
        Assert.EndsWith("Gerätemodell.", error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DeviceModel), error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRole_UnknownScope_NamesTheScopeWithItsDeclaredText()
    {
        store.Set("de", ScopeTexts.Singular.Key.Value, "Bereich", ScopeTexts.Singular.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        IReadOnlyList<Error> errors = await SendAndFailAllAsync(new CreateRoleCommand(
            Guid.CreateVersion7(), new RoleName($"role-{Guid.CreateVersion7():N}"), IsInheritable: true, []));

        Error error = Assert.Single(errors, error => error.Code == nameof(CreateRoleCommand.OwnerScopeId));
        Assert.EndsWith("Bereich.", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorDefaults_AreFluentValidationsEnglish()
    {
        Assert.Equal("common.validation.not-empty-validator", CommonValidationTexts.NotEmptyValidator.Key.Value);
        Assert.Equal(
            new FluentValidation.Resources.LanguageManager().GetString("MaximumLengthValidator", CultureInfo.GetCultureInfo("en")),
            CommonValidationTexts.MaximumLengthValidator.Default);
    }

    [Fact]
    public void Registry_AnswersCategoryAndHiddenProperties()
    {
        CommandConfigurationRegistry registry = host.Services.GetRequiredService<CommandConfigurationRegistry>();

        Assert.Same(ValidationTestTexts.Category, registry.CategoryOf(typeof(NamedThingCommand)));
        Assert.Same(ValidationTestTexts.Category, registry.CategoryOf(typeof(UniqueThingCommand)));
        Assert.Null(registry.CategoryOf(typeof(ProvisionExternalUserCommand)));
        Assert.Equal(["Note"], registry.HiddenProperties(typeof(NamedThingCommand)));
        Assert.Empty(registry.HiddenProperties(typeof(UniqueThingCommand)));
    }

    private ITranslationCache Cache => host.Services.GetRequiredService<ITranslationCache>();

    private async Task<IReadOnlyList<Error>> SendAndFailAllAsync<TResponse>(ICommand<TResponse> command)
    {
        using IServiceScope circuit = host.Services.CreateScope();

        ErrorOr<TResponse> result = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(command);

        Assert.True(result.IsError, "The command must be refused by its validator.");
        return result.Errors;
    }

    private async Task<Error> SendAndFailAsync(ICommand<Guid> command)
    {
        using IServiceScope circuit = host.Services.CreateScope();

        ErrorOr<Guid> result = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(command);

        Assert.True(result.IsError, "The command must be refused by its validator.");
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        return error;
    }

    public static class ValidationTestTexts
    {
        public static TextCategory Category { get; } = TextCategory.Create("DeviceManagement", "ValidationTextTests");

        public static FieldText Name { get; } = Category.Field(nameof(NamedThingCommand.Name), "Display name");

        public static TextDefinition Thing { get; } = Category.Text("Thing", "thing");
    }

    internal sealed record NamedThingCommand(string Name, string? Note = null) : ICommand<Guid>;

    internal sealed record ThingName(string Value);

    internal sealed record UniqueThingCommand(ThingName Name) : ICreateCommand<ThingRoot, Guid>, ICommand<Guid>;

    internal sealed record NamedReferenceThingCommand(Guid ThingId) : ICommand<Guid>;

    /// <summary>A root with no table; only its feature configuration, which names the category, is read.</summary>
    internal sealed class ThingRoot : AggregateRoot<Guid>;

    internal sealed class ThingRootConfiguration : IAggregateFeatureConfiguration<ThingRoot>
    {
        public void Configure(AggregateFeatureBuilder<ThingRoot> builder) => builder.Texts(ValidationTestTexts.Category);
    }

    internal sealed class NamedThingCommandConfiguration : ICommandConfiguration<NamedThingCommand>
    {
        public void Configure(CommandConfigurationBuilder<NamedThingCommand> builder) =>
            builder.Texts(ValidationTestTexts.Category).NotShown(command => command.Note);
    }

    internal sealed class UniqueThingCommandConfiguration : ICommandConfiguration<UniqueThingCommand>
    {
        public void Configure(CommandConfigurationBuilder<UniqueThingCommand> builder)
        {
            // Typed on ThingRoot only: the category comes from the root's feature configuration.
        }
    }

    internal sealed class NamedThingCommandValidator : AbstractValidator<NamedThingCommand>
    {
        public NamedThingCommandValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    internal sealed class UniqueThingCommandValidator : AbstractValidator<UniqueThingCommand>
    {
        public UniqueThingCommandValidator(IUniqueKeyRepository<ThingName> names) => RuleFor(x => x.Name).MustBeUniqueIn(names);
    }

    internal sealed class NamedReferenceThingCommandValidator : AbstractValidator<NamedReferenceThingCommand>
    {
        public NamedReferenceThingCommandValidator(IRepository<ThingRoot, Guid> things) =>
            RuleFor(x => x.ThingId).MustExistIn(things, ValidationTestTexts.Thing);
    }

    internal sealed class NoThings : IRepository<ThingRoot, Guid>
    {
        public ValueTask<ThingRoot?> GetAsync(Guid id, CancellationToken cancellationToken = default) => ValueTask.FromResult<ThingRoot?>(null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public void Insert(ThingRoot entity) => throw new NotSupportedException();

        public void Remove(ThingRoot entity) => throw new NotSupportedException();
    }

    internal sealed class TakenThingNames : IUniqueKeyRepository<ThingName>
    {
        public const string Taken = "taken";

        public Task<bool> IsTakenAsync(ThingName key, CancellationToken cancellationToken = default) =>
            Task.FromResult(key.Value == Taken);
    }

    internal sealed class EchoHandler<TCommand> : IRequestHandler<TCommand, ErrorOr<Guid>>
        where TCommand : ICommand<Guid>
    {
        public Task<ErrorOr<Guid>> Handle(TCommand request, CancellationToken cancellationToken) =>
            Task.FromResult<ErrorOr<Guid>>(Guid.CreateVersion7());
    }
}
