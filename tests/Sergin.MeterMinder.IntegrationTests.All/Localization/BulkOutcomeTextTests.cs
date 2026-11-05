using System.Globalization;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// A bulk run's report is declared text (ruling R17): the done and not-sent lines are <c>common.bulk.*</c> texts with a
/// named <c>{Count}</c>, and the count goes through the culture formatter, so a Persian page shows Persian digits.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BulkOutcomeTextTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private readonly TestTranslationStore store = new();

    private WebApplicationFactory<Program> host = default!;

    public Task InitializeAsync()
    {
        host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ITranslationStore>(store)));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public void Texts_AreDeclaredWithANamedCount()
    {
        Assert.Equal("common.bulk.deleted", CommonTexts.BulkDeleted.Key.Value);
        Assert.Equal("{Count} deleted", CommonTexts.BulkDeleted.Default);
        Assert.Equal("{Count} removed", CommonTexts.BulkRemoved.Default);
        Assert.Equal("common.bulk.not-sent", CommonTexts.BulkNotSent.Key.Value);
        Assert.Equal("{Count} not sent", CommonTexts.BulkNotSent.Default);
    }

    [Fact]
    public async Task Report_English_ShowsTheDeclaredDefaults()
    {
        IReadOnlyList<string> shown = await ReportAsync("en-US", new SerginBulkOutcome(3, [], 2));

        Assert.Equal(["3 deleted", "2 not sent"], shown);
    }

    [Fact]
    public async Task Report_Persian_TranslatesAndFormatsTheCount()
    {
        store.Set("fa", CommonTexts.BulkDeleted.Key.Value, "{Count} حذف شد", CommonTexts.BulkDeleted.Default);
        store.Set("fa", CommonTexts.BulkNotSent.Key.Value, "{Count} ارسال نشد", CommonTexts.BulkNotSent.Default);
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync("fa", CancellationToken.None);

        IReadOnlyList<string> shown = await ReportAsync("fa-IR", new SerginBulkOutcome(1200, [], 3));

        Assert.Equal(["۱٬۲۰۰ حذف شد", "۳ ارسال نشد"], shown);
    }

    private Task<IReadOnlyList<string>> ReportAsync(string culture, SerginBulkOutcome outcome)
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            using IServiceScope scope = host.Services.CreateScope();
            using SnackbarService snackbar = new(new NavigationManagerStub(), TimeProvider.System);

            outcome.Report(
                new SilentPresenter(),
                snackbar,
                scope.ServiceProvider.GetRequiredService<IStringLocalizer>(),
                scope.ServiceProvider.GetRequiredService<ICultureFormatter>(),
                CommonTexts.BulkDeleted);

            return Task.FromResult<IReadOnlyList<string>>([.. snackbar.ShownSnackbars.Select(shown => shown.Message ?? string.Empty)]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private sealed class SilentPresenter : IUiErrorPresenter
    {
        public SerginProblem Present(Error error) => throw new NotSupportedException();

        public void Notify(Error error)
        {
        }

        public void Notify(IReadOnlyList<Error> errors)
        {
        }
    }

    private sealed class NavigationManagerStub : Microsoft.AspNetCore.Components.NavigationManager
    {
        public NavigationManagerStub() => Initialize("http://localhost/", "http://localhost/");
    }
}
