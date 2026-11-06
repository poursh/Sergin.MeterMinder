using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.Platform.Application.Contracts.Languages.Commands.Add;
using Sergin.Platform.Application.Contracts.Languages.Commands.Delete;
using Sergin.Platform.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Platform.Domain.Languages;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Contracts.Users.Commands.Create;
using Sergin.UserAccess.Domain.Scopes;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The pseudo-locale sweep: a published culture <c>qps-ploc</c> translates every catalog key as <c>⟦default⟧</c>, and
/// every page a person can open — each parameterless module route, plus a detail and an edit route per aggregate over
/// seeded rows — is rendered under it. Every visible text in the app bar, the drawer and <c>&lt;main&gt;</c> must then
/// be wrapped in <c>⟦⟧</c>, whitespace and punctuation, or stored data (seeded rows, the configured app name, the
/// dev user, the scope and role names). Anything else is a literal that bypasses the catalog. The prerender only:
/// a table's rows and a popover's items load after first render (the headless-browser check covers those).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PseudoLocaleSweepTests(SerginWebApiFactory<Program> factory)
{
    // ICU's own spelling: the cache loads a culture under CultureInfo.Name, and the test store matches names exactly.
    private const string PseudoCulture = "qps-Ploc";

    private const string CookieName = ".AspNetCore.Culture";

    private static readonly string[] SeededPrefixes = ["maker", "street", "model", "dev", "user", "lang"];

    private static readonly string[] ParameterlessRoutes =
    [
        "/",
        "/dm/devices",
        "/dm/devices/new",
        "/dm/manufacturers",
        "/dm/manufacturers/new",
        "/pf/languages",
        "/pf/languages/new",
        "/ua/roles/new",
        "/ua/scopes",
        "/ua/scopes/new",
        "/ua/users",
        "/ua/users/new",
    ];

    [Fact]
    public async Task EveryVisibleText_IsTranslatedOrData()
    {
        TestTranslationStore store = new();
        store.AddLanguage(PseudoCulture, published: true);

        foreach (TextCatalogEntry entry in factory.Services.GetRequiredService<TextCatalog>().Entries)
        {
            store.Set(PseudoCulture, entry.Definition.Key.Value, $"⟦{entry.Definition.Default}⟧", entry.Definition.Default);
        }

        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ITranslationStore>(store)));

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}", $"street-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);
        Guid user = await CreateUserAsync(dispatcher, $"user-{marker}");
        Guid language = await AddLanguageAsync(dispatcher, "da-DK", $"lang-{marker}");

        try
        {
            string[] routes =
            [
                .. ParameterlessRoutes,
                $"/dm/devices/{device}",
                $"/dm/devices/{device}/edit",
                $"/dm/manufacturers/{manufacturer.Value}",
                $"/dm/manufacturers/{manufacturer.Value}/edit",
                $"/dm/manufacturers/{manufacturer.Value}/models/new",
                $"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}",
                $"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}/edit",
                $"/pf/languages/{language}",
                $"/pf/languages/{language}/translations",
                $"/ua/roles/{ViewerRoleId}",
                $"/ua/scopes/{Scope.RootId.Value}",
                $"/ua/users/{user}",
            ];

            string[] data =
            [
                .. await StoredDataAsync(),
                .. SeededPrefixes.Select(prefix => $"{prefix}-{marker}"),
                .. new[] { device, user, language, manufacturer.Value, model.Value }.Select(id => id.ToString()),
                "Meter Minder",

                // The language switcher names each language in itself (its endonym), whatever the page's language.
                CultureInfo.GetCultureInfo("en").NativeName,
                "da-DK",
                PseudoCulture,
            ];
            List<string> leftovers = [];

            using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            foreach (string route in routes)
            {
                string html = await RenderAsync(client, route);

                leftovers.AddRange(VisibleTexts(html).Where(text => !IsTranslatedOrData(text, data)).Select(text => $"{route}: '{text}'"));
            }

            Assert.True(leftovers.Count == 0, "Untranslated text:\n" + string.Join('\n', leftovers.Distinct(StringComparer.Ordinal)));
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
        }
    }

    private const string ViewerRoleId = "01920000-0000-7000-8000-0000000000a2";

    /// <summary>The document title and the body (app bar, drawer, page); the rest of the document is markup and script.</summary>
    private static IEnumerable<string> VisibleTexts(string html)
    {
        string visible = Regex.Replace(
            html,
            // The Development-only theme manager (a temporary drawer in MudBlazor.ThemeManager's own English) is a
            // design tool, not the product.
            @"<script\b.*?</script>|<style\b.*?</style>|<svg\b.*?</svg>|<!--.*?-->|<aside class=""[^""]*mud-drawer-temporary[^""]*"".*?</aside>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(5));

        foreach (Match region in Regex.Matches(
            visible,
            @"<title>.*?</title>|<body\b.*</body>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(5)))
        {
            foreach (Match node in Regex.Matches(region.Value, @">([^<]+)<", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                string text = WebUtility.HtmlDecode(node.Groups[1].Value).Trim();

                if (text.Length > 0)
                {
                    yield return text;
                }
            }

            foreach (Match attribute in Regex.Matches(
                region.Value, @"\b(?:placeholder|aria-label|title)=""([^""]*)""", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                string text = WebUtility.HtmlDecode(attribute.Groups[1].Value).Trim();

                if (text.Length > 0)
                {
                    yield return text;
                }
            }
        }
    }

    /// <summary>
    /// What is left once stored data (as whole words, so a short user name cannot hide letters inside a word) and every
    /// <c>⟦…⟧</c> (innermost first, since a translated template may wrap a translated label) are taken out must hold no
    /// letter.
    /// </summary>
    private static bool IsTranslatedOrData(string text, string[] data)
    {
        string rest = data.Where(value => value.Length > 0).OrderByDescending(value => value.Length)
            .Aggregate(text, (current, value) => Regex.Replace(
                current, $@"(?<!\p{{L}}){Regex.Escape(value)}(?!\p{{L}})", " ", RegexOptions.None, TimeSpan.FromSeconds(5)));

        string previous;

        do
        {
            previous = rest;
            rest = Regex.Replace(rest, "⟦[^⟦⟧]*⟧", " ", RegexOptions.None, TimeSpan.FromSeconds(5));
        }
        while (!string.Equals(previous, rest, StringComparison.Ordinal));

        return !rest.Any(char.IsLetter);
    }

    private async Task<string[]> StoredDataAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT name FROM ua.scopes
            UNION SELECT name FROM ua.roles
            UNION SELECT user_name FROM ua.users
            UNION SELECT first_name FROM ua.users WHERE first_name IS NOT NULL
            UNION SELECT last_name FROM ua.users WHERE last_name IS NOT NULL
            UNION SELECT email FROM ua.users WHERE email IS NOT NULL;
            """;

        List<string> values = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static async Task<string> RenderAsync(HttpClient client, string route)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(route, UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString($"c={PseudoCulture}|uic={PseudoCulture}")}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route} answered {response.StatusCode}.");

        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"lang=\"{PseudoCulture}\"", html, StringComparison.OrdinalIgnoreCase);
        return html;
    }

    private static async Task<Guid> CreateUserAsync(ISerginDispatcher dispatcher, string userName)
    {
        ErrorOr<CreateUserCommandResponse> created = await dispatcher.SendAsync(new CreateUserCommand(new UserName(userName)));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return created.Value.Id;
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
}
