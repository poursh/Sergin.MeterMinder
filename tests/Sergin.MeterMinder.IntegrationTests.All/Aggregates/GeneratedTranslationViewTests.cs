using System.Net;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Add;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.Delete;
using Sergin.Administration.Settings.Application.Contracts.Languages.Commands.GetOne;
using Sergin.Administration.Settings.Application.Contracts.Translations.Commands.GetList;
using Sergin.Administration.Settings.Domain.Languages;
using Sergin.Administration.Settings.Presentation.Blazor.Translations.Components;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The translation list as a generated list under its language, its table the grid in the list body slot: the strip
/// when the language is unknown, and the grid loading its rows again when the view's reload hands it a new generation.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedTranslationViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task TranslationList_UnknownLanguage_KeepsPlaceholderTrail()
    {
        var unknown = Guid.CreateVersion7();

        string html = await GetAsync($"/st/languages/{unknown}/translations");

        // The language's singular, still linking to where it would be; the current step is disabled; the page renders.
        Assert.Contains($"href=\"/st/languages/{unknown}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Language<", html, StringComparison.Ordinal);
        Assert.Contains(">Translations<", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"/st/languages/{unknown}/translations\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslationList_Reload_ReloadsGrid()
    {
        var counter = new ListQueryCounter();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(counter);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));
        }));
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ErrorOr<AddLanguageCommandResponse> added = await dispatcher.SendAsync(
            new AddLanguageCommand(new CultureName("lv-LV"), new LanguageDisplayName("Latviesu")));
        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);
        Guid language = added.Value.Id;

        try
        {
            ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
                .Initialize("http://localhost/", $"http://localhost/st/languages/{language}/translations");
            ErrorOr<LanguageQueryResponse> loaded = await dispatcher.SendAsync(new GetLanguageByIdQueryCommand(new LanguageId(language)));
            await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<GridHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(GridHost.Language)] = loaded.Value,
                    [nameof(GridHost.LanguageId)] = language,
                }));

                int before = counter.Count;
                await GridHost.Current!.ReloadAsync();

                // The same grid instance, handed generation 1, sends its list query once more.
                Assert.Equal(before + 1, counter.Count);
                Assert.Equal(1, GridHost.Current.Generation);
            });
        }
        finally
        {
            ErrorOr<Versioned<LanguageQueryResponse>> current = await dispatcher.SendVersionedAsync(new GetLanguageByIdQueryCommand(new LanguageId(language)));
            await dispatcher.SendVersionedAsync(new DeleteLanguageCommand(new LanguageId(language)), current.Value.Version);
        }
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}: {body[..Math.Min(body.Length, 3000)]}");

        return body;
    }

    private sealed class ListQueryCounter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);
    }

    private sealed class CountingBehavior<TRequest, TResponse>(ListQueryCounter counter) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is GetTranslationListQueryCommand)
            {
                counter.Increment();
            }

            return next(cancellationToken);
        }
    }

    /// <summary>Renders the grid the way the generated list view does and hands it a new generation on demand.</summary>
    private sealed class GridHost : ComponentBase
    {
        public static GridHost? Current { get; set; }

        public int Generation { get; private set; }

        [Parameter]
        public LanguageQueryResponse? Language { get; set; }

        [Parameter]
        public Guid LanguageId { get; set; }

        public Task ReloadAsync()
        {
            Generation++;
            return InvokeAsync(StateHasChanged);
        }

        protected override void OnInitialized() => Current = this;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<TranslationGrid>(1);
            builder.AddComponentParameter(
                2, nameof(TranslationGrid.Context), new AggregateViewContext(null, null, () => Task.CompletedTask, [], null, LanguageId, Language, Generation));
            builder.CloseComponent();
        }
    }
}
