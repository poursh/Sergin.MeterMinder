using System.Net;
using System.Reflection;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// DeviceManagement's texts are declared (spec decision 27): every key its domain errors name is in the catalog, a
/// form label is a catalog text a translation reaches, a domain-raised validation error renders its declared text in
/// the caller's culture, and every list field shares its key with a field text (decision 7).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceManagementTextTests(SerginWebApiFactory<Program> factory)
{
    private const string CookieName = ".AspNetCore.Culture";
    private const string DeviceIdLabelKey = "device-management.devices.fields.device-id.label";
    private const string NameInUseKey = "device-management.manufacturers.device-models.errors.name-in-use";

    [Fact]
    public void EveryDomainErrorKey_IsDeclared()
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();

        string[] keys =
        [
            .. typeof(Manufacturer).Assembly.GetTypes()
                .Where(type => type.Name.EndsWith("ErrorKeys", StringComparison.Ordinal))
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!),
        ];

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(catalog.TryGet(key, out _), $"'{key}' is not a declared text."));
    }

    [Fact]
    public async Task DeviceCreateForm_LabelsFromCatalog()
    {
        TestTranslationStore store = new();
        store.Set("de", DeviceIdLabelKey, "Geraetekennung", "Device ID");

        using WebApplicationFactory<Program> host = WithStore(store);
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/dm/devices/new", UriKind.Relative));
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString("c=de|uic=de")}");

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Geraetekennung", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateModelName_TranslatedMessage()
    {
        TestTranslationStore store = new();
        store.Set("de", NameInUseKey, "Der Name wird bereits verwendet.", "'Name' is already in use.");

        await using WebApplicationFactory<Program> host = WithStore(store);
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync("de", CancellationToken.None);

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(created.IsError);
        ManufacturerId manufacturerId = new(created.Value.Id);
        DeviceModelName name = new($"model-{Guid.CreateVersion7()}");

        ErrorOr<AddDeviceModelCommandResponse> first = await AddAsync(dispatcher, manufacturerId, name);
        Assert.False(first.IsError);

        ErrorOr<AddDeviceModelCommandResponse> second = await AddAsync(dispatcher, manufacturerId, name);

        Assert.True(second.IsError);
        Assert.Equal(nameof(AddDeviceModelCommand.Name), second.FirstError.Code);

        using CultureScope culture = new("de");

        SerginProblem problem = SerginProblemFactory.Create(second.FirstError, host.Services.GetRequiredService<IStringLocalizer>());

        Assert.Equal("Der Name wird bereits verwendet.", problem.Detail);
    }

    [Theory]
    [InlineData("devices")]
    [InlineData("manufacturers")]
    [InlineData("manufacturers", "device-models")]
    public void EveryListField_SharesItsKeyWithAFieldText(params string[] path)
    {
        TextCatalog catalog = factory.Services.GetRequiredService<TextCatalog>();
        var category = TextCategory.Create("DeviceManagement", path);
        ListFieldSet set = string.Join('.', path) switch
        {
            "devices" => DeviceListFields.Set,
            "manufacturers" => ManufacturerListFields.Set,
            _ => DeviceModelListFields.Set,
        };

        string[] declared = [.. catalog.FieldsOf(category).Select(field => SerginStringKey.ToSegment(field.PropertyName))];

        Assert.All(set.Fields, field => Assert.Contains(SerginStringKey.ToSegment(field.Id), declared));
    }

    private static async Task<ErrorOr<AddDeviceModelCommandResponse>> AddAsync(
        ISerginDispatcher dispatcher, ManufacturerId manufacturerId, DeviceModelName name)
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturerId.Value));

        return (await dispatcher.SendVersionedAsync(new AddDeviceModelCommand(manufacturerId, name), loaded.Value.Version))
            .Then(sent => sent.Value);
    }

    private WebApplicationFactory<Program> WithStore(TestTranslationStore store) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<ITranslationStore>(store)));
}
