using System.Security.Claims;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetList;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Publish;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Rename;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.SetDirection;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Unpublish;
using Sergin.Administration.Settings.Application.Configurations.Languages;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Language aggregate (spec decision 21) through <see cref="ISerginDispatcher"/>, the way its pages send: the
/// culture is normalised and unique after normalisation, English and unknown cultures are refused, the direction is
/// derived and overridable, a write reaches the translation cache's language list, and the writes need
/// <c>permission.st.languages.manage</c> and a current version. Every test deletes the languages it adds: the database
/// and the cache are shared by the whole collection, and a published language would show in every other test's switcher.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class LanguageTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task AddLanguage_NormalisesCulture()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid id = await AddAsync(dispatcher, "FA-ir", "فارسی");

        try
        {
            ErrorOr<LanguageQueryResponse> language = await dispatcher.SendAsync(new GetLanguageByIdQueryCommand(new LanguageId(id)));

            Assert.False(language.IsError);
            Assert.Equal("fa-IR", language.Value.Culture);
            Assert.Equal("فارسی", language.Value.DisplayName);
            Assert.Equal(LanguageStatus.Draft, language.Value.Status);
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }
    }

    [Fact]
    public async Task AddLanguage_DuplicateAfterNormalisation_Refused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid id = await AddAsync(dispatcher, "ar-EG", "العربية");

        try
        {
            ErrorOr<AddLanguageCommandResponse> duplicate = await dispatcher.SendAsync(
                new AddLanguageCommand(new CultureName("AR-eg"), new LanguageDisplayName("العربية")));

            Assert.True(duplicate.IsError);
            Error error = Assert.Single(duplicate.Errors);
            Assert.Equal(ErrorType.Validation, error.Type);
            Assert.Equal(nameof(AddLanguageCommand.Culture), error.Code);
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }
    }

    [Theory]
    [InlineData("qq-QQ")]
    [InlineData("not a culture")]
    public async Task AddLanguage_UnknownCulture_Refused(string culture)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        ErrorOr<AddLanguageCommandResponse> added = await Dispatcher(scope).SendAsync(
            new AddLanguageCommand(new CultureName(culture), new LanguageDisplayName("Unknown")));

        Assert.True(added.IsError);
        Error error = Assert.Single(added.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(AddLanguageCommand.Culture), error.Code);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("EN-us")]
    public async Task AddLanguage_English_Refused(string culture)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        ErrorOr<AddLanguageCommandResponse> added = await Dispatcher(scope).SendAsync(
            new AddLanguageCommand(new CultureName(culture), new LanguageDisplayName("English")));

        Assert.True(added.IsError);
        Error error = Assert.Single(added.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(AddLanguageCommand.Culture), error.Code);
    }

    /// <summary>The domain refuses what the validator refuses, so a caller that skips the pipeline cannot store English.</summary>
    [Fact]
    public void Create_RefusesEnglishAndUnknownCultures_WithDeclaredKeys()
    {
        ErrorOr<Language> english = Language.Create(new CultureName("en-AU"), new LanguageDisplayName("English"));
        ErrorOr<Language> unknown = Language.Create(new CultureName("qq-QQ"), new LanguageDisplayName("?"));

        Assert.Equal(nameof(Language.Culture), english.FirstError.Code);
        Assert.Equal(LanguageErrorKeys.BuiltInEnglish, english.FirstError.Metadata![Sergin.SharedKernel.Domain.Localizations.ErrorText.MetadataKey]);
        Assert.Equal(LanguageErrorKeys.UnknownCulture, unknown.FirstError.Metadata![Sergin.SharedKernel.Domain.Localizations.ErrorText.MetadataKey]);

        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        Assert.True(catalog.TryGet(LanguageErrorKeys.BuiltInEnglish, out _));
        Assert.True(catalog.TryGet(LanguageErrorKeys.UnknownCulture, out _));
    }

    // A stored culture this machine cannot build (written on another host) reads left to right instead of throwing.
    [Fact]
    public void Direction_UnbuildableStoredCulture_LeftToRight()
    {
        Assert.False(Language.ResolveRightToLeft("not a culture!", LanguageDirection.FromCulture));
        Assert.True(Language.ResolveRightToLeft("not a culture!", LanguageDirection.RightToLeft));
    }

    [Fact]
    public async Task Direction_DerivedFromCulture_AndOverridable()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid id = await AddAsync(dispatcher, "fa-IR", "فارسی");

        try
        {
            LanguageQueryResponse derived = await GetAsync(dispatcher, id);
            Assert.Equal(LanguageDirection.FromCulture, derived.Direction);
            Assert.True(derived.IsRightToLeft);

            ErrorOr<SetLanguageDirectionCommandResponse> set =
                await SendAtVersionAsync(dispatcher, id, new SetLanguageDirectionCommand(new LanguageId(id), LanguageDirection.LeftToRight));
            Assert.False(set.IsError, set.IsError ? set.FirstError.Description : string.Empty);

            LanguageQueryResponse overridden = await GetAsync(dispatcher, id);
            Assert.Equal(LanguageDirection.LeftToRight, overridden.Direction);
            Assert.False(overridden.IsRightToLeft);
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }
    }

    /// <summary>
    /// The cache's language list is loaded once, so every write refreshes it after the save; the store reports the
    /// resolved direction, override applied, since SharedKernel reads <see cref="LanguageInfo.IsRightToLeft"/> as is.
    /// </summary>
    [Fact]
    public async Task Publish_ShowsInCacheLanguages()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        ITranslationCache cache = factory.Services.GetRequiredService<ITranslationCache>();

        Guid id = await AddAsync(dispatcher, "de-DE", "Deutsch");

        try
        {
            LanguageInfo draft = Assert.Single(cache.Languages, language => language.Culture == "de-DE");
            Assert.False(draft.IsPublished);

            Assert.False((await SendAtVersionAsync(dispatcher, id, new PublishLanguageCommand(new LanguageId(id)))).IsError);
            Assert.False((await SendAtVersionAsync(
                dispatcher, id, new SetLanguageDirectionCommand(new LanguageId(id), LanguageDirection.RightToLeft))).IsError);
            Assert.False((await SendAtVersionAsync(dispatcher, id, new RenameLanguageCommand(new LanguageId(id), new LanguageDisplayName("Deutsch (DE)")))).IsError);

            LanguageInfo published = Assert.Single(cache.Languages, language => language.Culture == "de-DE");
            Assert.True(published.IsPublished);
            Assert.True(published.IsRightToLeft);
            Assert.Equal("Deutsch (DE)", published.DisplayName);

            Assert.False((await SendAtVersionAsync(dispatcher, id, new UnpublishLanguageCommand(new LanguageId(id)))).IsError);
            Assert.False(Assert.Single(cache.Languages, language => language.Culture == "de-DE").IsPublished);
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }

        Assert.DoesNotContain(cache.Languages, language => language.Culture == "de-DE");
    }

    [Fact]
    public async Task Delete_RemovesTheLanguage()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid id = await AddAsync(dispatcher, "tr-TR", "Türkçe");

        ErrorOr<DeleteLanguageCommandResponse> deleted = await SendAtVersionAsync(dispatcher, id, new DeleteLanguageCommand(new LanguageId(id)));
        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);

        ErrorOr<LanguageQueryResponse> gone = await dispatcher.SendAsync(new GetLanguageByIdQueryCommand(new LanguageId(id)));
        Assert.Equal(ErrorType.NotFound, gone.FirstError.Type);

        // Not soft-deletable: the culture is free again at once.
        Guid again = await AddAsync(dispatcher, "tr-TR", "Türkçe");
        await DeleteAsync(dispatcher, again);
    }

    [Fact]
    public async Task List_SearchesCultureAndName_SortsByCulture()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid italian = await AddAsync(dispatcher, "it-IT", "Italiano");
        Guid japanese = await AddAsync(dispatcher, "ja-JP", "日本語");

        try
        {
            // Only this test's own cultures are asserted on: rows another test left behind must not decide the outcome.
            string[] own = ["it-IT", "ja-JP"];

            ErrorOr<ListQueryResponse<GetLanguageListItem>> byName = await dispatcher.SendAsync(
                new GetLanguageListQueryCommand(Paggination.Create(100, 1), new Term("italia")));
            Assert.False(byName.IsError, byName.IsError ? byName.FirstError.Description : string.Empty);
            Assert.Equal(["it-IT"], byName.Value.Data.Select(language => language.Culture).Where(own.Contains));

            ErrorOr<ListQueryResponse<GetLanguageListItem>> all = await dispatcher.SendAsync(new GetLanguageListQueryCommand(
                Paggination.Create(100, 1), sorting: Sorting.Create($$"""[{"id":"{{LanguageListFields.Culture}}","desc":true}]""")));
            Assert.False(all.IsError, all.IsError ? all.FirstError.Description : string.Empty);
            Assert.Equal(["ja-JP", "it-IT"], all.Value.Data.Select(language => language.Culture).Where(own.Contains));
        }
        finally
        {
            await DeleteAsync(dispatcher, italian);
            await DeleteAsync(dispatcher, japanese);
        }
    }

    /// <summary>
    /// The refresh after commit cannot turn a saved write into an error: the user would retry into "culture in use". A
    /// failing refresh is logged, the list is forgotten so the next load reads it again, and the write still succeeds.
    /// </summary>
    [Fact]
    public async Task AddLanguage_RefreshFailure_StillSucceeds_AndTheNextLoadRetries()
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ServiceDescriptor settings = services.Last(descriptor => descriptor.ServiceType == typeof(ITranslationStore));
            services.Replace(ServiceDescriptor.Singleton<ITranslationStore>(provider =>
                new FailingLanguagesStore((ITranslationStore)ActivatorUtilities.CreateInstance(provider, settings.ImplementationType!))));
        }));
        var store = (FailingLanguagesStore)host.Services.GetRequiredService<ITranslationStore>();
        ITranslationCache cache = host.Services.GetRequiredService<ITranslationCache>();
        await cache.EnsureLoadedAsync("en", CancellationToken.None);

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        store.Fail = true;
        Guid id = await AddAsync(dispatcher, "sv-SE", "Svenska");
        store.Fail = false;

        try
        {
            Assert.Equal("sv-SE", (await GetAsync(dispatcher, id)).Culture);
            Assert.Empty(cache.Languages);

            await cache.EnsureLoadedAsync("en", CancellationToken.None);

            Assert.Contains(cache.Languages, language => language.Culture == "sv-SE");
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }
    }

    [Fact]
    public async Task Manage_RequiresPermission()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = await ViewerAsync();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        ErrorOr<AddLanguageCommandResponse> added = await dispatcher.SendAsync(
            new AddLanguageCommand(new CultureName("pl-PL"), new LanguageDisplayName("Polski")));
        ErrorOr<Versioned<RenameLanguageCommandResponse>> renamed = await dispatcher.SendVersionedAsync(
            new RenameLanguageCommand(new LanguageId(Guid.CreateVersion7()), new LanguageDisplayName("Polski")), RowVersion.Create());

        Assert.Equal(ErrorType.Forbidden, added.FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, renamed.FirstError.Type);
    }

    [Fact]
    public async Task Rename_StaleVersion_412()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);

        Guid id = await AddAsync(dispatcher, "nl-NL", "Nederlands");

        try
        {
            RowVersion loaded = (await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(new LanguageId(id)))).Value.Version;

            ErrorOr<Versioned<RenameLanguageCommandResponse>> first =
                await dispatcher.SendVersionedAsync(new RenameLanguageCommand(new LanguageId(id), new LanguageDisplayName("Nederlands (NL)")), loaded);
            Assert.False(first.IsError, first.IsError ? first.FirstError.Description : string.Empty);

            ErrorOr<Versioned<RenameLanguageCommandResponse>> stale =
                await dispatcher.SendVersionedAsync(new RenameLanguageCommand(new LanguageId(id), new LanguageDisplayName("Vlaams")), loaded);

            Assert.True(stale.IsError);
            Assert.True(VersionErrors.IsStale(stale.FirstError));
            Assert.Equal("Nederlands (NL)", (await GetAsync(dispatcher, id)).DisplayName);
        }
        finally
        {
            await DeleteAsync(dispatcher, id);
        }
    }

    // Languages belong to no scope, so a holder of the permission on any scope passes; every write but Add starts
    // from a loaded record and carries its version.
    [Theory]
    [InlineData(typeof(AddLanguageCommand), false)]
    [InlineData(typeof(RenameLanguageCommand), true)]
    [InlineData(typeof(SetLanguageDirectionCommand), true)]
    [InlineData(typeof(PublishLanguageCommand), true)]
    [InlineData(typeof(UnpublishLanguageCommand), true)]
    [InlineData(typeof(DeleteLanguageCommand), true)]
    public void Writes_NeedManage_OnAnyScope(Type request, bool versioned)
    {
        CommandSettings settings = factory.Services.GetRequiredService<CommandConfigurationRegistry>().For(request);

        Assert.Equal(LanguagePermissions.Manage, Assert.Single(settings.RequiredPermissions));
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
        Assert.Equal(typeof(Language), settings.AggregateRoot);
        Assert.Equal(versioned, settings.RequiresExpectedVersion);
    }

    [Theory]
    [InlineData(typeof(GetLanguageByIdQueryCommand))]
    [InlineData(typeof(GetLanguageListQueryCommand))]
    public void Reads_NeedRead_OnAnyScope(Type request)
    {
        CommandSettings settings = factory.Services.GetRequiredService<CommandConfigurationRegistry>().For(request);

        Assert.Equal(LanguagePermissions.Read, Assert.Single(settings.RequiredPermissions));
        Assert.Equal(PermissionScope.Any, settings.PermissionScope);
    }

    [Fact]
    public void EveryListField_SharesItsKeyWithAFieldText()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        string[] declared = [.. catalog.FieldsOf(LanguageTexts.Category).Select(field => SerginStringKey.ToSegment(field.PropertyName))];

        Assert.All(LanguageListFields.Set.Fields, field => Assert.Contains(SerginStringKey.ToSegment(field.Id), declared));
    }

    private static ISerginDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

    private static async Task<Guid> AddAsync(ISerginDispatcher dispatcher, string culture, string displayName)
    {
        ErrorOr<AddLanguageCommandResponse> added = await dispatcher.SendAsync(
            new AddLanguageCommand(new CultureName(culture), new LanguageDisplayName(displayName)));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return added.Value.Id;
    }

    private static async Task<LanguageQueryResponse> GetAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<LanguageQueryResponse> language = await dispatcher.SendAsync(new GetLanguageByIdQueryCommand(new LanguageId(id)));

        Assert.False(language.IsError, language.IsError ? language.FirstError.Description : string.Empty);
        return language.Value;
    }

    private static async Task DeleteAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<DeleteLanguageCommandResponse> deleted = await SendAtVersionAsync(dispatcher, id, new DeleteLanguageCommand(new LanguageId(id)));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    // Read the language's current version, then send the guarded command with it, as the detail page does.
    private static async Task<ErrorOr<TResponse>> SendAtVersionAsync<TResponse>(
        ISerginDispatcher dispatcher, Guid id, IRequest<ErrorOr<TResponse>> command)
    {
        ErrorOr<Versioned<LanguageQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(new LanguageId(id)));

        return (await dispatcher.SendVersionedAsync(command, loaded.IsError ? RowVersion.Create() : loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    private sealed class FailingLanguagesStore(ITranslationStore inner) : ITranslationStore
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken) =>
            Fail ? throw new InvalidOperationException("The language list cannot be read.") : inner.GetLanguagesAsync(cancellationToken);

        public Task<IReadOnlyDictionary<string, StoredTranslation>> GetTranslationsAsync(string culture, CancellationToken cancellationToken) =>
            inner.GetTranslationsAsync(culture, cancellationToken);
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
