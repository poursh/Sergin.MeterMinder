using System.Data.Common;
using System.Net;
using System.Security.Claims;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.Administration.Settings.Application.Configurations.Languages;
using Sergin.Administration.Settings.Application.Configurations.Translations;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Publish;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.Change;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.GetList;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.GetProgress;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.Administration.Settings.Domain.Translations;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Translation aggregate (spec decisions 5, 22 and 26) through <see cref="ISerginDispatcher"/>: a save reaches the
/// next render, a translation must keep the default's placeholders, a change is checked against the version it was
/// loaded at, and the list merges the catalog with a language's rows into four statuses. Every test deletes the
/// language it adds, which takes its translations with it: the database and the cache are shared by the collection.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TranslationTests(SerginWebApiFactory<Program> factory)
{
    private const string GoneKey = "settings.gone.key";

    [Fact]
    public async Task Add_ShowsOnNextRender()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{CatalogSeed.Marker()}");
        Guid language = await AddLanguageAsync(dispatcher, "de", "Deutsch");

        try
        {
            Assert.False((await SendAtLanguageVersionAsync(dispatcher, language, new PublishLanguageCommand(language))).IsError);
            string path = $"/dm/manufacturers/{manufacturer.Value}/edit";

            string before = await RenderAsync(path, "de");
            Assert.DoesNotContain("Speichern", before, StringComparison.Ordinal);

            await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Speichern");

            string after = await RenderAsync(path, "de");
            Assert.Contains("Speichern", after, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>Review focus 2: a mistyped placeholder would render as written, so it is refused on save.</summary>
    [Fact]
    public async Task Add_PlaceholderTypo_Refused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "da-DK", "Dansk");

        try
        {
            Assert.Contains("{DeviceId}", DeviceTexts.DeleteConfirm.Default, StringComparison.Ordinal);

            ErrorOr<AddTranslationCommandResponse> added = await dispatcher.SendAsync(
                new AddTranslationCommand(new LanguageId(language), DeviceTexts.DeleteConfirm.Key.Value, "Gerät {DeviceID} löschen?"));

            Assert.True(added.IsError);
            Error error = Assert.Single(added.Errors);
            Assert.Equal(ErrorType.Validation, error.Type);
            Assert.Equal(nameof(AddTranslationCommand.Text), error.Code);
            Assert.Contains("{DeviceId}", error.Description, StringComparison.Ordinal);

            ErrorOr<AddTranslationCommandResponse> right = await dispatcher.SendAsync(
                new AddTranslationCommand(new LanguageId(language), DeviceTexts.DeleteConfirm.Key.Value, "{DeviceId} slettes?"));
            Assert.False(right.IsError, right.IsError ? right.FirstError.Description : string.Empty);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task Add_Twice_Or_UnknownKey_Refused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "is-IS", "Íslenska");

        try
        {
            await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Vista");

            ErrorOr<AddTranslationCommandResponse> twice = await dispatcher.SendAsync(
                new AddTranslationCommand(new LanguageId(language), CommonTexts.Save.Key.Value, "Vista aftur"));
            ErrorOr<AddTranslationCommandResponse> unknown = await dispatcher.SendAsync(
                new AddTranslationCommand(new LanguageId(language), GoneKey, "Farinn"));

            Assert.Equal(nameof(AddTranslationCommand.Key), Assert.Single(twice.Errors).Code);
            Assert.Equal(nameof(AddTranslationCommand.Key), Assert.Single(unknown.Errors).Code);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>Review focus 3: two translators load the same row; the second save is stale and the first edit survives.</summary>
    [Fact]
    public async Task Change_StaleVersion_412()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "nb-NO", "Norsk bokmål");

        try
        {
            TranslationId id = await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Lagre");

            RowVersion first = (await dispatcher.SendVersionedAsync(new GetTranslationByIdQueryCommand(id))).Value.Version;
            RowVersion second = (await dispatcher.SendVersionedAsync(new GetTranslationByIdQueryCommand(id))).Value.Version;

            ErrorOr<Versioned<ChangeTranslationCommandResponse>> saved =
                await dispatcher.SendVersionedAsync(new ChangeTranslationCommand(id, "Lagre nå"), first);
            Assert.False(saved.IsError, saved.IsError ? saved.FirstError.Description : string.Empty);

            ErrorOr<Versioned<ChangeTranslationCommandResponse>> stale =
                await dispatcher.SendVersionedAsync(new ChangeTranslationCommand(id, "Spar"), second);

            Assert.True(stale.IsError);
            Assert.True(VersionErrors.IsStale(stale.FirstError));
            Assert.Equal("Lagre nå", (await dispatcher.SendAsync(new GetTranslationByIdQueryCommand(id))).Value.Text);

            ErrorOr<ChangeTranslationCommandResponse> unversioned = await dispatcher.SendAsync(new ChangeTranslationCommand(id, "Spar"));
            Assert.Equal(VersionErrors.Required.Code, unversioned.FirstError.Code);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>A change stores the default as it reads now, so it clears a needs-review flag.</summary>
    [Fact]
    public async Task Status_NeedsReview_WhenDefaultChanged()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "pt-PT", "Português");
        string key = CommonTexts.Save.Key.Value;

        try
        {
            TranslationId id = await AddTranslationAsync(dispatcher, language, key, "Guardar");
            Assert.Equal(TranslationStatus.Translated, (await ItemAsync(dispatcher, language, key)).Status);

            // The code's default changed after the translation was made: what the row was translated from no longer matches.
            await ExecuteAsync("UPDATE st.translation SET source_text = 'Store' WHERE id = @Id;", ("Id", id.Value));

            GetTranslationListItem flagged = await ItemAsync(dispatcher, language, key);
            Assert.Equal(TranslationStatus.NeedsReview, flagged.Status);
            Assert.Equal(CommonTexts.Save.Default, flagged.Default);
            Assert.Equal("Guardar", flagged.Text);

            ErrorOr<ChangeTranslationCommandResponse> changed = await SendAtTranslationVersionAsync(
                dispatcher, id, new ChangeTranslationCommand(id, "Gravar"));
            Assert.False(changed.IsError, changed.IsError ? changed.FirstError.Description : string.Empty);
            Assert.Equal(TranslationStatus.Translated, (await ItemAsync(dispatcher, language, key)).Status);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task Status_Orphaned_ForUnknownKey()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "cs-CZ", "Čeština");

        try
        {
            var orphanId = Guid.CreateVersion7();
            await ExecuteAsync(
                """
                INSERT INTO st.translation (id, language_id, key, text, source_text, created_at_utc, created_by, row_version)
                VALUES (@Id, @LanguageId, @Key, 'Pryč', 'Gone', now(), @Id, gen_random_uuid());
                """,
                ("Id", orphanId),
                ("LanguageId", language),
                ("Key", GoneKey));

            GetTranslationListItem orphan = await ItemAsync(dispatcher, language, GoneKey);
            Assert.Equal(TranslationStatus.Orphaned, orphan.Status);
            Assert.Equal(orphanId, orphan.TranslationId);
            Assert.Equal("settings", orphan.Module);
            Assert.Equal("gone", orphan.Category);
            Assert.Equal("Gone", orphan.Default);

            TranslationId known = await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Uložit");
            ErrorOr<DeleteTranslationCommandResponse> refused =
                await SendAtTranslationVersionAsync(dispatcher, known, new DeleteTranslationCommand(known));
            Error refusal = Assert.Single(refused.Errors);
            Assert.Equal(ErrorType.Validation, refusal.Type);
            Assert.Equal("Id", refusal.Code);

            ErrorOr<DeleteTranslationCommandResponse> deleted = await SendAtTranslationVersionAsync(
                dispatcher, new TranslationId(orphanId), new DeleteTranslationCommand(new TranslationId(orphanId)));
            Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
            Assert.DoesNotContain((await ListAsync(dispatcher, language, status: TranslationStatus.Orphaned)).Data, item => item.Key == GoneKey);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task List_FiltersByModuleAndStatus()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        Guid language = await AddLanguageAsync(dispatcher, "hu-HU", "Magyar");

        try
        {
            await AddTranslationAsync(dispatcher, language, TranslationTexts.Title.Key.Value, "Fordítások");
            await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Mentés");

            ListQueryResponse<GetTranslationListItem> translated =
                await ListAsync(dispatcher, language, module: "settings", status: TranslationStatus.Translated);
            Assert.Equal([TranslationTexts.Title.Key.Value], translated.Data.Select(item => item.Key));
            Assert.Equal(1, translated.Total);

            int settingsKeys = catalog.Entries.Count(entry => entry.Module == "settings");
            ListQueryResponse<GetTranslationListItem> untranslated =
                await ListAsync(dispatcher, language, module: "settings", status: TranslationStatus.Untranslated);
            Assert.Equal(settingsKeys - 1, untranslated.Total);
            Assert.All(untranslated.Data, item => Assert.Null(item.Text));

            ListQueryResponse<GetTranslationListItem> category =
                await ListAsync(dispatcher, language, module: "settings", category: "translations");
            Assert.NotEmpty(category.Data);
            Assert.All(category.Data, item => Assert.StartsWith("settings.translations.", item.Key, StringComparison.Ordinal));

            ListQueryResponse<GetTranslationListItem> searched = await ListAsync(dispatcher, language, term: "MENTÉS");
            Assert.Equal([CommonTexts.Save.Key.Value], searched.Data.Select(item => item.Key));

            ListQueryResponse<GetTranslationListItem> all = await ListAsync(dispatcher, language);
            Assert.Equal(catalog.Entries.Count, all.Total);
            Assert.Equal(all.Data.Select(item => item.Key).Order(StringComparer.Ordinal), all.Data.Select(item => item.Key));
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task List_PagesTheFilteredRows()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        Guid language = await AddLanguageAsync(dispatcher, "lt-LT", "Lietuvių");

        try
        {
            int settingsKeys = catalog.Entries.Count(entry => entry.Module == "settings");

            ErrorOr<ListQueryResponse<GetTranslationListItem>> page = await dispatcher.SendAsync(new GetTranslationListQueryCommand(
                new LanguageId(language), Paggination.Create(1, 2), module: "settings", status: TranslationStatus.Untranslated));

            Assert.False(page.IsError, page.IsError ? page.FirstError.Description : string.Empty);
            Assert.Equal(settingsKeys, page.Value.Total);
            Assert.Single(page.Value.Data);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task Progress_CountsTranslated()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        Guid language = await AddLanguageAsync(dispatcher, "ro-RO", "Română");

        try
        {
            Assert.Equal(new LanguageProgressQueryResponse(0, catalog.Entries.Count), await ProgressAsync(dispatcher, language));

            await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Salvează");
            TranslationId review = await AddTranslationAsync(dispatcher, language, TranslationTexts.Title.Key.Value, "Traduceri");
            await ExecuteAsync("UPDATE st.translation SET source_text = 'Old' WHERE id = @Id;", ("Id", review.Value));
            await ExecuteAsync(
                """
                INSERT INTO st.translation (id, language_id, key, text, source_text, created_at_utc, created_by, row_version)
                VALUES (@Id, @LanguageId, @Key, 'Dispărut', 'Gone', now(), @Id, gen_random_uuid());
                """,
                ("Id", Guid.CreateVersion7()),
                ("LanguageId", language),
                ("Key", GoneKey));

            // A translation needing review is still shown, so it counts; an orphan is not a declared text, so it does not.
            Assert.Equal(new LanguageProgressQueryResponse(2, catalog.Entries.Count), await ProgressAsync(dispatcher, language));
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>R4: a deleted language takes its translations with it, in the same save, and out of the cache.</summary>
    [Fact]
    public async Task Delete_RemovesTranslations()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        ITranslationCache cache = factory.Services.GetRequiredService<ITranslationCache>();
        Guid language = await AddLanguageAsync(dispatcher, "fi-FI", "Suomi");

        await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Tallenna");
        Assert.True(cache.TryGet("fi-FI", CommonTexts.Save.Key.Value, out StoredTranslation? cached));
        Assert.Equal(new StoredTranslation("Tallenna", CommonTexts.Save.Default), cached);

        await DeleteLanguageAsync(dispatcher, language);

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM st.translation WHERE language_id = @Id;", ("Id", language)));
        Assert.False(cache.TryGet("fi-FI", CommonTexts.Save.Key.Value, out _));
    }

    /// <summary>
    /// The refresh after commit cannot turn a saved translation into an error. A failing refresh forgets the culture, so
    /// the next load reads it again; without that, the culture would keep the entry it had before the save.
    /// </summary>
    [Fact]
    public async Task Add_RefreshFailure_StillSucceeds_AndTheNextLoadRetries()
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ServiceDescriptor settings = services.Last(descriptor => descriptor.ServiceType == typeof(ITranslationStore));
            services.Replace(ServiceDescriptor.Singleton<ITranslationStore>(provider =>
                new FailingTranslationsStore((ITranslationStore)ActivatorUtilities.CreateInstance(provider, settings.ImplementationType!))));
        }));
        var store = (FailingTranslationsStore)host.Services.GetRequiredService<ITranslationStore>();
        ITranslationCache cache = host.Services.GetRequiredService<ITranslationCache>();

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "lt-LT", "Lietuvių");

        try
        {
            await cache.EnsureLoadedAsync("lt-LT", CancellationToken.None);

            store.Fail = true;
            await AddTranslationAsync(dispatcher, language, CommonTexts.Save.Key.Value, "Išsaugoti");
            store.Fail = false;

            Assert.False(cache.TryGet("lt-LT", CommonTexts.Save.Key.Value, out _));

            await cache.EnsureLoadedAsync("lt-LT", CancellationToken.None);

            Assert.True(cache.TryGet("lt-LT", CommonTexts.Save.Key.Value, out StoredTranslation? reloaded));
            Assert.Equal("Išsaugoti", reloaded.Text);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task Edit_RequiresPermission()
    {
        using IServiceScope admin = factory.Services.CreateScope();
        Guid language = await AddLanguageAsync(Dispatcher(admin), "sk-SK", "Slovenčina");

        try
        {
            TranslationId id = await AddTranslationAsync(Dispatcher(admin), language, CommonTexts.Save.Key.Value, "Uložiť");

            using IServiceScope scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = await ViewerAsync();
            ISerginDispatcher viewer = Dispatcher(scope);

            ErrorOr<AddTranslationCommandResponse> added = await viewer.SendAsync(
                new AddTranslationCommand(new LanguageId(language), TranslationTexts.Title.Key.Value, "Preklady"));
            ErrorOr<Versioned<ChangeTranslationCommandResponse>> changed =
                await viewer.SendVersionedAsync(new ChangeTranslationCommand(id, "Uložte"), RowVersion.Create());
            ErrorOr<Versioned<DeleteTranslationCommandResponse>> deleted =
                await viewer.SendVersionedAsync(new DeleteTranslationCommand(id), RowVersion.Create());

            Assert.Equal(ErrorType.Forbidden, added.FirstError.Type);
            Assert.Equal(ErrorType.Forbidden, changed.FirstError.Type);
            Assert.Equal(ErrorType.Forbidden, deleted.FirstError.Type);
        }
        finally
        {
            await DeleteLanguageAsync(Dispatcher(admin), language);
        }
    }

    // Translations belong to no scope; every write needs Edit, and every write but Add starts from a loaded row.
    [Theory]
    [InlineData(typeof(AddTranslationCommand), false)]
    [InlineData(typeof(ChangeTranslationCommand), true)]
    [InlineData(typeof(DeleteTranslationCommand), true)]
    public void Writes_NeedEdit_OnAnyScope(Type request, bool versioned)
    {
        CommandSettings settings = factory.Services.GetRequiredService<CommandConfigurationRegistry>().For(request);

        Assert.Equal(TranslationPermissions.Edit, Assert.Single(settings.RequiredPermissions));
        Assert.Equal("permission.st.translations.edit", TranslationPermissions.Edit.Permission.Value);
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
        Assert.Equal(typeof(Translation), settings.AggregateRoot);
        Assert.Equal(versioned, settings.RequiresExpectedVersion);
    }

    [Theory]
    [InlineData(typeof(GetTranslationByIdQueryCommand))]
    [InlineData(typeof(GetTranslationListQueryCommand))]
    [InlineData(typeof(GetLanguageProgressQueryCommand))]
    public void Reads_NeedLanguageRead_OnAnyScope(Type request)
    {
        CommandSettings settings = factory.Services.GetRequiredService<CommandConfigurationRegistry>().For(request);

        Assert.Equal(LanguagePermissions.Read, Assert.Single(settings.RequiredPermissions));
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void ErrorKeys_AreTheDeclaredOnes()
    {
        Assert.Equal("settings.translations.errors.placeholders-differ", TranslationTexts.PlaceholdersDiffer.Key.Value);
        Assert.Equal(["Expected"], TranslationTexts.PlaceholdersDiffer.Placeholders);
        Assert.True(factory.Services.GetRequiredService<TextCatalog>().TryGet(TranslationTexts.PlaceholdersDiffer.Key.Value, out _));
    }

    private static ISerginDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

    private async Task<string> RenderAsync(string path, string culture)
    {
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", $".AspNetCore.Culture={Uri.EscapeDataString($"c={culture}|uic={culture}")}");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<Guid> AddLanguageAsync(ISerginDispatcher dispatcher, string culture, string displayName)
    {
        ErrorOr<AddLanguageCommandResponse> added = await dispatcher.SendAsync(
            new AddLanguageCommand(new CultureName(culture), new LanguageDisplayName(displayName)));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return added.Value.Id;
    }

    private static async Task DeleteLanguageAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<DeleteLanguageCommandResponse> deleted = await SendAtLanguageVersionAsync(dispatcher, id, new DeleteLanguageCommand(id));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    private static async Task<TranslationId> AddTranslationAsync(ISerginDispatcher dispatcher, Guid language, string key, string text)
    {
        ErrorOr<AddTranslationCommandResponse> added =
            await dispatcher.SendAsync(new AddTranslationCommand(new LanguageId(language), key, text));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return new TranslationId(added.Value.Id);
    }

    private static async Task<ListQueryResponse<GetTranslationListItem>> ListAsync(
        ISerginDispatcher dispatcher,
        Guid language,
        string? term = null,
        string? module = null,
        string? category = null,
        TranslationStatus? status = null)
    {
        ErrorOr<ListQueryResponse<GetTranslationListItem>> list = await dispatcher.SendAsync(new GetTranslationListQueryCommand(
            new LanguageId(language), Paggination.Create(100_000, 1), term is null ? null : new Term(term), module, category, status));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        return list.Value;
    }

    private static async Task<GetTranslationListItem> ItemAsync(ISerginDispatcher dispatcher, Guid language, string key) =>
        Assert.Single((await ListAsync(dispatcher, language, term: key)).Data, item => item.Key == key);

    private static async Task<LanguageProgressQueryResponse> ProgressAsync(ISerginDispatcher dispatcher, Guid language)
    {
        ErrorOr<LanguageProgressQueryResponse> progress = await dispatcher.SendAsync(new GetLanguageProgressQueryCommand(new LanguageId(language)));

        Assert.False(progress.IsError, progress.IsError ? progress.FirstError.Description : string.Empty);
        return progress.Value;
    }

    private static async Task<ErrorOr<TResponse>> SendAtLanguageVersionAsync<TResponse>(
        ISerginDispatcher dispatcher, Guid id, IRequest<ErrorOr<TResponse>> command)
    {
        ErrorOr<Versioned<LanguageQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(id));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    private static async Task<ErrorOr<TResponse>> SendAtTranslationVersionAsync<TResponse>(
        ISerginDispatcher dispatcher, TranslationId id, IRequest<ErrorOr<TResponse>> command)
    {
        ErrorOr<Versioned<TranslationQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetTranslationByIdQueryCommand(id));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = Command(connection, sql, parameters);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = Command(connection, sql, parameters);

        return await command.ExecuteScalarAsync();
    }

    private static DbCommand Command(DbConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();

        // Every statement is a literal in this class; values go in as parameters.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private sealed class FailingTranslationsStore(ITranslationStore inner) : ITranslationStore
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken) =>
            inner.GetLanguagesAsync(cancellationToken);

        public Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken cancellationToken) =>
            Fail ? throw new InvalidOperationException("The translations cannot be read.") : inner.GetTranslationsAsync(culture, cancellationToken);
    }

    // A user signed in through the identity provider for the first time: the seeded viewer role at the root, which
    // holds no Settings permission.
    private async Task<IUserContext> ViewerAsync()
    {
        string subject = Guid.CreateVersion7().ToString();

        using IServiceScope scope = factory.Services.CreateScope();
        ExternalIdentityResult viewer = await scope.ServiceProvider.GetRequiredService<IExternalIdentityResolver>().ResolveAsync(
            new ExternalIdentity(subject, $"viewer-{subject[..8]}", $"{subject[..8]}@sergin.local", "Test", "Viewer"),
            CancellationToken.None);

        ClaimsIdentity identity = new([new Claim(SerginClaimTypes.UserId, viewer.UserId.ToString())], authenticationType: "test");

        return ClaimsPrincipalUserContext.Create(new ClaimsPrincipal(identity));
    }
}
