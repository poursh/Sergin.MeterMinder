using System.Net;
using System.Reflection;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.UserAccess.Domain.Scopes;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// UserAccess's texts are declared (spec decision 27): every key its domain errors name is in the catalog, a form label
/// is a catalog text a translation reaches, and a domain-raised validation error renders its declared text in the
/// caller's culture with its code still the property name (decision 17).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class UserAccessTextTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";
    private const string UserNameLabelKey = "user-access.users.fields.user-name.label";
    private const string RootCannotMoveKey = "user-access.scopes.errors.root-cannot-move";

    [Fact]
    public void EveryDomainErrorKey_IsDeclared()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        string[] keys =
        [
            .. typeof(Scope).Assembly.GetTypes()
                .Where(type => type.Name.EndsWith("ErrorKeys", StringComparison.Ordinal))
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!),
        ];

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(catalog.TryGet(key, out _), $"'{key}' is not a declared text."));
    }

    [Fact]
    public async Task UserCreateForm_LabelsFromCatalog()
    {
        TestTranslationStore store = new();
        store.Set("de", UserNameLabelKey, "Benutzername", "User name");

        using WebApplicationFactory<Program> host = WithStore(store);
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/ua/users/new", UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString("c=de|uic=de")}");

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Benutzername", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeMoveRefusal_TranslatedMessage()
    {
        TestTranslationStore store = new();
        store.Set("de", RootCannotMoveKey, "Der Wurzelbereich kann nicht verschoben werden.", "The root scope cannot be moved.");

        await using WebApplicationFactory<Program> host = WithStore(store);
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync("de", CancellationToken.None);

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        // Scope.MoveUnder refuses the root before it looks at the target, so the root under itself is enough.
        ErrorOr<MoveScopeCommandResponse> moved = await dispatcher.SendAsync(new MoveScopeCommand(Scope.RootId.Value, Scope.RootId.Value));

        Assert.True(moved.IsError);
        Assert.Equal(ErrorType.Validation, moved.FirstError.Type);
        Assert.Equal(nameof(Scope.ParentId), moved.FirstError.Code);

        using CultureScope culture = new("de");

        SerginProblem problem = SerginProblemFactory.Create(moved.FirstError, host.Services.GetRequiredService<IStringLocalizer>());

        Assert.Equal("Der Wurzelbereich kann nicht verschoben werden.", problem.Detail);
    }

    private WebApplicationFactory<Program> WithStore(TestTranslationStore store) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
}
