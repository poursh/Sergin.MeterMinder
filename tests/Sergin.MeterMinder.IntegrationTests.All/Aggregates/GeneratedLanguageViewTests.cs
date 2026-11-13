using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.Settings.Application.Configurations.Languages;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetList;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Publish;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Rename;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Validation;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated Language views show: the reverse pair Publish/Unpublish (only the one that applies, also after a
/// stale publish), the inline rename and direction select, the list's direction column and the Translations link, which a
/// reader sees too. Every test deletes the language it adds.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedLanguageViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task LanguageDetail_Draft_OffersPublishNotUnpublish()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "hr-HR", "Hrvatski");

        try
        {
            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains("Hrvatski", html, StringComparison.Ordinal);
            Assert.Contains(">Publish<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Unpublish<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageDetail_Published_OffersUnpublishNotPublish()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "bg-BG", "Balgarski");

        try
        {
            ErrorOr<PublishLanguageCommandResponse> published =
                await dispatcher.SendAtLanguageVersionAsync(language, new PublishLanguageCommand(new LanguageId(language)));
            Assert.False(published.IsError, published.IsError ? published.FirstError.Description : string.Empty);

            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains(">Unpublish<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Publish<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>
    /// The language published elsewhere after the view read it: Publish at the old version changes nothing, so nothing is
    /// saved and no version is checked (a version sent but nothing changed still succeeds); the reload offers Unpublish.
    /// </summary>
    [Fact]
    public async Task LanguageDetail_PublishedElsewhere_ReloadsAndOffersUnpublish()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "ga-IE", "Gaeilge");

        try
        {
            AggregateViewModel model = Model();
            CommandView publish = Assert.Single(model.Commands, command => command.Request == typeof(PublishLanguageCommand));

            ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, language);
            Assert.False(loaded.IsError);

            using (IServiceScope other = factory.Services.CreateScope())
            {
                ErrorOr<PublishLanguageCommandResponse> elsewhere =
                    await Dispatcher(other).SendAtLanguageVersionAsync(language, new PublishLanguageCommand(new LanguageId(language)));
                Assert.False(elsewhere.IsError, elsewhere.IsError ? elsewhere.FirstError.Description : string.Empty);
            }

            ViewSendResult late = await GeneratedViewSupport.SendFromIdAsync(dispatcher, publish, language, loaded.Version);

            Assert.False(late.IsError, late.IsError ? late.Errors[0].Code : string.Empty);

            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains(">Unpublish<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Publish<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    /// <summary>
    /// The language renamed elsewhere after the view read it: Publish at the old version is refused as stale and saves
    /// nothing, so the reload shows the current name and still offers Publish.
    /// </summary>
    [Fact]
    public async Task LanguageDetail_StalePublish_IsRefusedAndReloadKeepsDraft()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "sq-AL", "Shqip");

        try
        {
            AggregateViewModel model = Model();
            CommandView publish = Assert.Single(model.Commands, command => command.Request == typeof(PublishLanguageCommand));
            Assert.True(publish.RequiresVersion);

            ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, language);
            Assert.False(loaded.IsError);

            using (IServiceScope other = factory.Services.CreateScope())
            {
                ErrorOr<RenameLanguageCommandResponse> renamed = await Dispatcher(other).SendAtLanguageVersionAsync(
                    language, new RenameLanguageCommand(new LanguageId(language), new LanguageDisplayName("Shqip e re")));
                Assert.False(renamed.IsError, renamed.IsError ? renamed.FirstError.Description : string.Empty);
            }

            ViewSendResult stale = await GeneratedViewSupport.SendFromIdAsync(dispatcher, publish, language, loaded.Version);

            Assert.True(stale.IsError);
            Assert.Contains(stale.Errors, VersionErrors.IsStale);

            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains("Shqip e re", html, StringComparison.Ordinal);
            Assert.Contains(">Publish<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Unpublish<", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageDetail_InlineRename_Stale_ReloadsKeepsCurrentName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "mt-MT", "Malti");

        try
        {
            AggregateViewModel model = Model();
            CommandView rename = Assert.Single(model.Commands, command => command.Request == typeof(RenameLanguageCommand));
            Assert.Equal(SerginEditMode.Inline, rename.EditMode);
            GeneratedSetProperty setProperty = new(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());

            ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, language);
            ErrorOr<RenameLanguageCommandResponse> renamed = await dispatcher.SendAtLanguageVersionAsync(
                language, new RenameLanguageCommand(new LanguageId(language), new LanguageDisplayName("Malti kurrenti")));
            Assert.False(renamed.IsError, renamed.IsError ? renamed.FirstError.Description : string.Empty);

            SetPropertyStatus status = await setProperty.SubmitAsync(dispatcher, rename, language, "Malti tard", loaded.Version);

            Assert.Equal(SetPropertyStatus.Stale, status);
            Assert.Contains(setProperty.Errors, VersionErrors.IsStale);

            // The reload shows the current name, in the inline card's field too.
            ViewSendResult reloaded = await model.LoadRecordAsync(dispatcher, language);
            Assert.Equal("Malti kurrenti", Assert.IsType<LanguageQueryResponse>(reloaded.Value).DisplayName);
            string html = await GetAsync($"/st/languages/{language}");
            Assert.Contains("value=\"Malti kurrenti\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Malti tard", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageDetail_DirectionSelect_ForManager()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "cy-GB", "Cymraeg");

        try
        {
            string html = await GetAsync($"/st/languages/{language}");

            Assert.Contains("aria-label=\"Direction\"", html, StringComparison.Ordinal);
            Assert.Contains(">Rename<", html, StringComparison.Ordinal);

            // The resolved direction is not a field of its own on the detail.
            Assert.DoesNotContain(nameof(LanguageQueryResponse.IsRightToLeft), html, StringComparison.Ordinal);
            Assert.DoesNotContain(Model().DetailFields, field => field.Name == nameof(LanguageQueryResponse.IsRightToLeft));
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task LanguageList_DirectionColumnShowsValueText()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FieldView direction = Assert.Single(Model().ListFields, field => field.Name == nameof(GetLanguageListItem.IsRightToLeft));
        ICultureFormatter formatter = scope.ServiceProvider.GetRequiredService<ICultureFormatter>();
        IStringLocalizer localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer>();

        Assert.False(direction.Hidden);
        Assert.Equal(LanguageTexts.Direction.Label, direction.Label);
        Assert.Equal("Right to left", FieldValueText.Render(direction, true, formatter, localizer));
        Assert.Equal("Left to right", FieldValueText.Render(direction, false, formatter, localizer));

        string html = await GetAsync("/st/languages");

        Assert.Contains(">Direction<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageDetail_TranslationsLink()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = Dispatcher(scope);
        Guid language = await AddLanguageAsync(dispatcher, "eu-ES", "Euskara");

        try
        {
            string manager = await GetAsync($"/st/languages/{language}");
            Assert.Contains($"href=\"/st/languages/{language}/translations\"", manager, StringComparison.Ordinal);

            RbacScenario scenario = await RbacScenario.BuildAsync(factory);
            TestCaller reader = await scenario.CreateGrantedCallerAsync(scenario.North, LanguagePermissions.Read.Permission);
            string html = await GetAsAsync(reader, $"/st/languages/{language}");

            Assert.Contains("Euskara", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/st/languages/{language}/translations\"", html, StringComparison.Ordinal);
            Assert.Contains(">Translations<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-label=\"Direction\"", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    private static ISerginDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

    private AggregateViewModel Model() => factory.Services.GetRequiredService<AggregateViewRegistry>().For<Language>();

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> GetAsAsync(IUserContext caller, string path)
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(caller))));
        using HttpClient client = host.CreateClient();

        return WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(path, UriKind.Relative)));
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
        ErrorOr<DeleteLanguageCommandResponse> deleted = await dispatcher.SendAtLanguageVersionAsync(id, new DeleteLanguageCommand(new LanguageId(id)));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    private sealed class FixedUserContextFactory(IUserContext caller) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => caller;
    }
}
