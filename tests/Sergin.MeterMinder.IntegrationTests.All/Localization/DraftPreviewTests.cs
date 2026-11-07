using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.Administration.Settings.Application.Configurations.Translations;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.Revoke;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The Settings module's real draft-preview policy (spec decision 9), against the real registration: a holder of the
/// translations edit permission on any scope sees draft languages in the switcher and may switch to one; everyone
/// else, including a translator whose grant was revoked, quietly gets the default culture. Draft languages are
/// created through the real Settings commands, which leave them as drafts.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DraftPreviewTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";

    [Fact]
    public async Task Translator_SeesDraftInSwitcher_AndCanSwitch()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid language = await AddDraftAsync(dispatcher, "pt-BR", "Portugues");

        try
        {
            using HttpClient client = factory.CreateClient();

            string html = await GetHomeAsync(client, "pt-BR");

            // The language menu's button names the language in use; its items render only while it is open.
            Assert.Contains("Portugues (draft)", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task Viewer_DraftCookie_FallsBack()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid language = await AddDraftAsync(dispatcher, "es-MX", "Espanol");

        try
        {
            await using WebApplicationFactory<Program> host = AsCaller(await ViewerAsync());
            using HttpClient client = host.CreateClient();

            string html = await GetHomeAsync(client, "es-MX");

            Assert.DoesNotContain("es-MX", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Espanol", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(dispatcher, language);
        }
    }

    [Fact]
    public async Task RevokedTranslator_FallsBackQuietly()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid language = await AddDraftAsync(dispatcher, "sv-SE", "Svenska");

        try
        {
            RbacScenario tree = await RbacScenario.BuildAsync(factory);
            Guid role = await tree.CreateRoleAsync(tree.North, $"translator-{Guid.NewGuid():N}"[..20], inheritable: true, TranslationPermissions.Edit.Permission);
            Guid user = await tree.CreateUserAsync("translator");
            Guid assignment = (await tree.AssignAsync(user, role, tree.North, includeSubtree: true)).Value.Id;
            TestCaller translator = new() { Id = new UserId(user) };

            SwitchableUserContextFactory caller = new(translator);
            await using WebApplicationFactory<Program> host = AsCaller(caller);
            using HttpClient client = host.CreateClient();

            Assert.Contains("Svenska (draft)", await GetHomeAsync(client, "sv-SE"), StringComparison.Ordinal);

            // The grant cache is per host, so the revocation is sent through this host, as a system identity.
            caller.Current = new TestSystemCaller();
            using (IServiceScope hostScope = host.Services.CreateScope())
            {
                ErrorOr<RevokeAssignmentCommandResponse> revoked = await hostScope.ServiceProvider
                    .GetRequiredService<ISerginDispatcher>().SendAsync(new RevokeAssignmentCommand(user, assignment));
                Assert.False(revoked.IsError, revoked.IsError ? revoked.FirstError.Description : string.Empty);
            }

            caller.Current = translator;

            string html = await GetHomeAsync(client, "sv-SE");

            Assert.DoesNotContain("sv-SE", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Svenska", html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(dispatcher, language);
        }
    }

    private static async Task<string> GetHomeAsync(HttpClient client, string cookieCulture)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString($"c={cookieCulture}|uic={cookieCulture}")}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> AsCaller(IUserContext caller) => AsCaller(new SwitchableUserContextFactory(caller));

    private WebApplicationFactory<Program> AsCaller(SwitchableUserContextFactory caller) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IUserContextFactory>(caller)));

    // The seeded viewer role at the root holds no Settings permission.
    private async Task<IUserContext> ViewerAsync()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);

        return await tree.CreateGrantedCallerAsync(tree.North);
    }

    private static async Task<Guid> AddDraftAsync(ISerginDispatcher dispatcher, string culture, string displayName)
    {
        ErrorOr<AddLanguageCommandResponse> added = await dispatcher.SendAsync(
            new AddLanguageCommand(new CultureName(culture), new LanguageDisplayName(displayName)));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        return added.Value.Id;
    }

    private static async Task DeleteAsync(ISerginDispatcher dispatcher, Guid id)
    {
        ErrorOr<Versioned<LanguageQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(id));
        ErrorOr<Versioned<DeleteLanguageCommandResponse>> deleted = await dispatcher.SendVersionedAsync(
            new DeleteLanguageCommand(id), loaded.IsError ? RowVersion.Create() : loaded.Value.Version);

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
    }

    private sealed class SwitchableUserContextFactory(IUserContext initial) : IUserContextFactory
    {
        public IUserContext Current { get; set; } = initial;

        public IUserContext CreateUserContext() => Current;
    }
}
