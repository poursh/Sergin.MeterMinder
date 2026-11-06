using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.Create;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.Administration.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// The pseudo-locale sweep: a published culture <c>qps-ploc</c> translates every catalog key as <c>⟦default⟧</c>, and
/// every page a person can open — each parameterless module route, plus a detail and an edit route per aggregate over
/// seeded rows — is rendered under it. Every visible text in the document title and the body must then be wrapped in
/// <c>⟦⟧</c>, whitespace and punctuation, or data a swept page shows, each value read by id: the seeded rows, the
/// configured app name, the dev user and the root scope everywhere, and on a page that shows them its own rows (the
/// viewer role, the root's roles, the scope tree). Anything else is a literal that bypasses the catalog. The prerender only:
/// a table's rows and a popover's items load after first render (the headless-browser check covers those).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PseudoLocaleSweepTests(SerginWebApiFactory<Program> factory)
{
    // ICU's own spelling: the cache loads a culture under CultureInfo.Name, and the test store matches names exactly.
    private const string PseudoCulture = "qps-Ploc";

    private const string CookieName = ".AspNetCore.Culture";

    private static readonly string[] SeededPrefixes = ["maker", "street", "model", "dev", "user", "lang"];

    private static readonly string[] DevUserKeys = ["UserName", "FirstName", "LastName", "Email"];

    private static readonly string[] ParameterlessRoutes =
    [
        "/",
        "/dm/devices",
        "/dm/devices/new",
        "/dm/manufacturers",
        "/dm/manufacturers/new",
        "/st/languages",
        "/st/languages/new",
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
                $"/st/languages/{language}",
                $"/st/languages/{language}/translations",
                $"/ua/roles/{ViewerRoleId}",
                $"/ua/scopes/{Scope.RootId.Value}",
                $"/ua/users/{user}",
            ];

            string[] data =
            [
                .. await SharedRowsAsync(),
                .. SeededPrefixes.Select(prefix => $"{prefix}-{marker}"),
                .. new[] { device, user, language, manufacturer.Value, model.Value }.Select(id => id.ToString()),
                ApplicationName,

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

                string[] allowed = [.. data, .. await RouteRowsAsync(route)];

                leftovers.AddRange(VisibleTexts(html).Where(text => !IsTranslatedOrData(text, allowed)).Select(text => $"{route}: '{text}'"));
            }

            Assert.True(leftovers.Count == 0, "Untranslated text:\n" + string.Join('\n', leftovers.Distinct(StringComparer.Ordinal)));
        }
        finally
        {
            await DeleteLanguageAsync(dispatcher, language);
            await DeleteCatalogRowsAsync(dispatcher, device, manufacturer);
            await ExecuteAsync("DELETE FROM ua.users WHERE id = @Id;", user);
        }
    }

    private const string ViewerRoleId = "01920000-0000-7000-8000-0000000000a2";

    /// <summary>The document title and the body (app bar, drawer, page); the rest of the document is markup and script.</summary>
    private static IEnumerable<string> VisibleTexts(string html)
    {
        string visible = Regex.Replace(
            html,
            // The Development-only theme manager (MudBlazor.ThemeManager's button and the drawer right after it, in that
            // package's own English) is a design tool, not the product: cut from its button to the end of that drawer.
            @"<script\b.*?</script>|<style\b.*?</style>|<svg\b.*?</svg>|<!--.*?-->|<div class=""mud-thememanager-button\b.*?</aside>",
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

    private string ApplicationName => Configuration["Sergin:ApplicationName"]!;

    private IConfiguration Configuration => factory.Services.GetRequiredService<IConfiguration>();

    /// <summary>
    /// The stored values every swept page may show besides the rows the sweep seeds: the root scope's name (the app bar's
    /// scope picker) and the dev user (the app bar). Each read by id; nothing table-wide, so a word another test happens to
    /// store cannot hide a literal on a page that does not show it.
    /// </summary>
    private async Task<string[]> SharedRowsAsync()
    {
        IConfigurationSection devUser = Configuration.GetSection("Sergin:DevUser");

        return
        [
            .. await NamesAsync("SELECT name FROM ua.scopes WHERE id = @Id;", Scope.RootId.Value),
            .. DevUserKeys.Select(key => devUser[key]).OfType<string>(),
        ];
    }

    /// <summary>
    /// The stored rows one page shows of its own: the viewer role's page its name; the root scope's page the roles it
    /// owns; and every page of the scope tree the tree's live nodes, since the tree layout lists them.
    /// </summary>
    private async Task<string[]> RouteRowsAsync(string route)
    {
        List<string> values = [];

        if (route == $"/ua/roles/{ViewerRoleId}")
        {
            values.AddRange(await NamesAsync("SELECT name FROM ua.roles WHERE id = @Id;", Guid.Parse(ViewerRoleId)));
        }

        if (route == $"/ua/scopes/{Scope.RootId.Value}")
        {
            values.AddRange(await NamesAsync(
                "SELECT name FROM ua.roles WHERE owner_scope_id = @Id AND deleted_at_utc IS NULL;", Scope.RootId.Value));
        }

        if (route.StartsWith("/ua/scopes", StringComparison.Ordinal))
        {
            values.AddRange(await NamesAsync("SELECT name FROM ua.scopes WHERE deleted_at_utc IS NULL;"));
        }

        return [.. values];
    }

    private async Task<IReadOnlyList<string>> NamesAsync(string sql, Guid? id = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();

        // Every statement is a literal in this class; the id goes in as a parameter.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        if (id is { } value)
        {
            AddParameter(command, "Id", value);
        }

        List<string> names = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();

        // A literal statement; the id goes in as a parameter.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        AddParameter(command, "Id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task DeleteCatalogRowsAsync(ISerginDispatcher dispatcher, Guid device, ManufacturerId manufacturer)
    {
        ErrorOr<DeleteDeviceCommandResponse> deviceDeleted =
            await dispatcher.SendAtDeviceVersionAsync(device, new DeleteDeviceCommand(device));
        Assert.False(deviceDeleted.IsError, deviceDeleted.IsError ? deviceDeleted.FirstError.Description : string.Empty);

        // Soft-deletes the manufacturer and, with it, the seeded model.
        ErrorOr<DeleteManufacturerCommandResponse> manufacturerDeleted =
            await dispatcher.SendAtManufacturerVersionAsync(manufacturer.Value, new DeleteManufacturerCommand(manufacturer.Value));
        Assert.False(
            manufacturerDeleted.IsError, manufacturerDeleted.IsError ? manufacturerDeleted.FirstError.Description : string.Empty);
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
