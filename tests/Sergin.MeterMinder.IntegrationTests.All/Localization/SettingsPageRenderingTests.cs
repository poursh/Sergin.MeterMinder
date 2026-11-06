using System.Globalization;
using System.Net;
using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.Administration.Settings.Application.Configurations.Languages;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetAudit;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.Add;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Settings module's admin pages (spec decision 23) as the prerender draws them: the language list, a language's
/// detail with its progress, and a language's translations with their columns and filter. A list's rows are loaded by
/// the table after the first render, so they are not in the prerender (the headless-browser check covers them); the
/// drawer entry needs the languages read permission. Pages are HTML-decoded, since Blazor encodes non-ASCII text.
/// Every test deletes the language it adds.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SettingsPageRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task LanguageList_Renders()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "nl-NL", "Nederlands");

        try
        {
            string html = await GetAsync("/st/languages");

            Assert.Contains("<title>Languages</title>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/st/languages/new\"", html, StringComparison.Ordinal);
            Assert.Contains("Search culture or name…", html, StringComparison.Ordinal);
            Assert.Contains(">Display name<", html, StringComparison.Ordinal);
            Assert.Contains(">Direction<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageCreate_Renders()
    {
        string html = await GetAsync("/st/languages/new");

        Assert.Contains("<title>New language</title>", html, StringComparison.Ordinal);
        Assert.Contains("Culture", html, StringComparison.Ordinal);
        Assert.Contains("Display name", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageDetail_ShowsProgress()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        int total = factory.Services.GetRequiredService<TextCatalog>().Entries.Count;
        Guid language = await AddLanguageAsync(dispatcher, "sk-SK", "Slovenčina");

        try
        {
            ErrorOr<AddTranslationCommandResponse> added = await dispatcher.SendAsync(
                new AddTranslationCommand(new LanguageId(language), CommonTexts.Save.Key.Value, "Uložiť"));
            Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains("Slovenčina", html, StringComparison.Ordinal);
            Assert.Contains("sk-SK", html, StringComparison.Ordinal);
            Assert.Contains($"1 of {total.ToString("N0", CultureInfo.GetCultureInfo("en"))} translated", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/st/languages/{language}/translations\"", html, StringComparison.Ordinal);
            Assert.Contains("Publish", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>
    /// A reader (languages read, not manage) sees the name and direction as text: no rename form, no direction select,
    /// no publish or delete. The administrator sees them all.
    /// </summary>
    [Fact]
    public async Task LanguageDetail_ForAReader_HidesTheManageControls()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "lv-LV", "Latviesu");

        try
        {
            string manager = await GetAsync($"/st/languages/{language}");
            Assert.Contains(">Rename<", manager, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Direction\"", manager, StringComparison.Ordinal);

            RbacScenario scenario = await RbacScenario.BuildAsync(factory);
            TestCaller reader = await scenario.CreateGrantedCallerAsync(scenario.North, LanguagePermissions.Read.Permission);
            await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(reader))));
            using HttpClient client = host.CreateClient();

            string html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/st/languages/{language}", UriKind.Relative)));

            Assert.Contains("Latviesu", html, StringComparison.Ordinal);
            Assert.Contains("From the culture (Left to right)", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Rename<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-label=\"Direction\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Publish<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Delete<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageDetail_UnknownId_ShowsNotFound()
    {
        string html = await GetAsync($"/st/languages/{Guid.CreateVersion7()}");

        Assert.Contains("<title>Language</title>", html, StringComparison.Ordinal);
        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.Contains("The record was not found, or you cannot see it here.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" translated", html, StringComparison.Ordinal);
    }

    /// <summary>The detail view's information dialog: a language is audited and belongs to no scope.</summary>
    [Fact]
    public async Task LanguageAudit_HasStamps_AndNoScope()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "et-EE", "Eesti");

        try
        {
            ErrorOr<AuditInfo> audit = await dispatcher.SendAsync(new GetLanguageAuditQueryCommand(language));

            Assert.False(audit.IsError, audit.IsError ? audit.FirstError.Description : string.Empty);
            Assert.NotEqual(Guid.Empty, audit.Value.CreatedBy);
            Assert.Null(audit.Value.ModifiedAtUtc);
            Assert.Null(audit.Value.ScopeId);

            ErrorOr<AuditInfo> missing = await dispatcher.SendAsync(new GetLanguageAuditQueryCommand(Guid.CreateVersion7()));
            Assert.Equal(ErrorType.NotFound, missing.FirstError.Type);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task TranslationList_RendersColumnsFilterToggleAndTrail()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "sl-SI", "Slovenščina");

        try
        {
            string html = await GetAsync($"/st/languages/{language}/translations");

            Assert.Contains("<title>Translations</title>", html, StringComparison.Ordinal);
            Assert.Contains(">Default (English)<", html, StringComparison.Ordinal);
            Assert.Contains(">Translation<", html, StringComparison.Ordinal);
            Assert.Contains(">Status<", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Advanced filter\"", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/st/languages/{language}\"", html, StringComparison.Ordinal);
            Assert.Contains("Slovenščina", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task NavEntry_HiddenWithoutRead()
    {
        string administrator = await GetAsync("/");
        Assert.Contains("href=\"/st/languages\"", administrator, StringComparison.Ordinal);

        IUserContext viewer = await ViewerAsync();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(viewer))));
        using HttpClient client = host.CreateClient();

        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.DoesNotContain("href=\"/st/languages\"", html, StringComparison.Ordinal);
    }

    private static ISerginDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
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
        ErrorOr<Versioned<LanguageQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(id));
        ErrorOr<Versioned<DeleteLanguageCommandResponse>> deleted = await dispatcher.SendVersionedAsync(
            new DeleteLanguageCommand(id), loaded.IsError ? RowVersion.Create() : loaded.Value.Version);

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
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

    private sealed class FixedUserContextFactory(IUserContext caller) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => caller;
    }
}
