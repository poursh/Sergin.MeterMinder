using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Domain.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// An error that names a declared text in its metadata (<see cref="ErrorText"/>) renders that text as its detail, in
/// the current culture; one without keeps today's rule (spec decision 17). The code stays what it is.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ErrorTextTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private readonly TestTranslationStore store = new();

    private WebApplicationFactory<Program> host = default!;

    public Task InitializeAsync()
    {
        host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITranslationStore>(store);
            services.AddSingleton(TextSource.FromTypes("DeviceManagement", typeof(ErrorTestTexts)));
        }));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task MetadataKey_RendersDeclaredText()
    {
        var error = Error.Validation("Name", "fallback", ErrorText.Of(CommonTexts.Save.Key.Value));

        using (new CultureScope("en"))
        {
            SerginProblem english = SerginProblemFactory.Create(error, Localizer);

            Assert.Equal("Save", english.Detail);
            Assert.Equal(ErrorType.Validation, english.Type);
        }

        store.Set("de", CommonTexts.Save.Key.Value, "Speichern", CommonTexts.Save.Default);
        await Cache.EnsureLoadedAsync("de", CancellationToken.None);

        using CultureScope culture = new("de");

        Assert.Equal("Speichern", SerginProblemFactory.Create(error, Localizer).Detail);
    }

    [Fact]
    public void MetadataArguments_FillThePlaceholders()
    {
        using CultureScope culture = new("en");

        var error = Error.Validation(
            "Name",
            "fallback",
            ErrorText.Of(ErrorTestTexts.InUse.Key.Value, new Dictionary<string, object?> { ["Name"] = "Feeder 1" }));

        Assert.Equal("Feeder 1 is in use.", SerginProblemFactory.Create(error, Localizer).Detail);
    }

    [Fact]
    public void NoMetadata_KeepsDescription()
    {
        using CultureScope culture = new("en");

        var error = Error.Validation("Name", "'Name' must not be empty.");

        SerginProblem problem = SerginProblemFactory.Create(error, Localizer);

        Assert.Equal("'Name' must not be empty.", problem.Detail);
        Assert.Equal(Localizer[SerginProblemFactory.ValidationTitleKey].Value, problem.Title);
    }

    [Fact]
    public void UndeclaredMetadataKey_KeepsDescription()
    {
        using CultureScope culture = new("en");

        var error = Error.Validation("Name", "fallback", ErrorText.Of("device-management.nowhere.declared"));

        Assert.Equal("fallback", SerginProblemFactory.Create(error, Localizer).Detail);
    }

    private IStringLocalizer Localizer => host.Services.GetRequiredService<IStringLocalizer>();

    private ITranslationCache Cache => host.Services.GetRequiredService<ITranslationCache>();

    public static class ErrorTestTexts
    {
        private static readonly TextCategory category = TextCategory.Create("DeviceManagement", "ErrorTextTests");

        public static TextDefinition InUse { get; } = category.Text("InUse", "{Name} is in use.");
    }
}
