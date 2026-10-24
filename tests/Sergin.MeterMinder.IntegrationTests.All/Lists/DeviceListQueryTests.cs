using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Lists;

/// <summary>
/// The device list reads Term, Filtering and Sorting end to end against Postgres: search over the device id and
/// model name, sort on the declared columns, the four filters of the advanced filter panel, a total that counts the
/// filtered rows, and a validation error for anything the list does not declare.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceListQueryTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task Term_MatchesDeviceIdOrModelName_AndTheTotalCountsOnlyMatches()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        DeviceModelInternalId other = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"other-{CatalogSeed.Marker()}");
        await CatalogSeed.DeviceAsync(dispatcher, $"A-{marker}", other);
        await CatalogSeed.DeviceAsync(dispatcher, $"B-{CatalogSeed.Marker()}", model);
        await CatalogSeed.DeviceAsync(dispatcher, $"C-{CatalogSeed.Marker()}", other);

        ListQueryResponse<GetDeviceListItem> found = await ListAsync(dispatcher, term: marker);

        Assert.Equal(2, found.Total);
        Assert.Equal(2, found.Data.Count);
    }

    [Fact]
    public async Task Sorting_OrdersByTheRequestedColumn_InEitherDirection()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        DeviceModelInternalId model = await CatalogSeed.ModelAsync(
            dispatcher, await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}"), $"model-{marker}");
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-b", model);
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-c", model);
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-a", model);

        ListQueryResponse<GetDeviceListItem> ascending = await ListAsync(dispatcher, term: marker, sorting: Sort(DeviceListFields.DeviceId, desc: false));
        ListQueryResponse<GetDeviceListItem> descending = await ListAsync(dispatcher, term: marker, sorting: Sort(DeviceListFields.DeviceId, desc: true));

        Assert.Equal([$"{marker}-a", $"{marker}-b", $"{marker}-c"], ascending.Data.Select(d => d.DeviceId));
        Assert.Equal([$"{marker}-c", $"{marker}-b", $"{marker}-a"], descending.Data.Select(d => d.DeviceId));
    }

    [Fact]
    public async Task ManufacturerAndModelFilters_KeepOnlyTheirDevices_AndCarryTheManufacturer()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        ManufacturerId aster = await CatalogSeed.ManufacturerAsync(dispatcher, $"aster-{marker}");
        ManufacturerId norda = await CatalogSeed.ManufacturerAsync(dispatcher, $"norda-{marker}");
        DeviceModelInternalId threePhase = await CatalogSeed.ModelAsync(dispatcher, aster, "E-Series 3P");
        DeviceModelInternalId onePhase = await CatalogSeed.ModelAsync(dispatcher, aster, "E-Series 1P");
        DeviceModelInternalId gas = await CatalogSeed.ModelAsync(dispatcher, norda, "G4");
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-1", threePhase);
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-2", onePhase);
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-3", gas);

        ListQueryResponse<GetDeviceListItem> byManufacturer = await ListAsync(
            dispatcher, term: marker, filtering: Filter(DeviceListFields.ManufacturerId, aster.Value.ToString()));
        ListQueryResponse<GetDeviceListItem> byModel = await ListAsync(
            dispatcher, term: marker, filtering: Filter(DeviceListFields.DeviceModelId, onePhase.Value.ToString()));

        Assert.Equal(2, byManufacturer.Total);
        Assert.All(byManufacturer.Data, d => Assert.Equal($"aster-{marker}", d.ManufacturerName));
        GetDeviceListItem single = Assert.Single(byModel.Data);
        Assert.Equal($"{marker}-2", single.DeviceId);
        Assert.Equal(aster.Value, single.ManufacturerId);
    }

    [Fact]
    public async Task CreatedBetween_IsHalfOpen_AndAnOpenEndIsUnbounded()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        DeviceModelInternalId model = await CatalogSeed.ModelAsync(
            dispatcher, await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}"), $"model-{marker}");
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-now", model);

        string past = DateTime.UtcNow.AddDays(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        string future = DateTime.UtcNow.AddDays(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        ListQueryResponse<GetDeviceListItem> inRange = await ListAsync(
            dispatcher, term: marker, filtering: Range(DeviceListFields.CreatedAtUtc, past, future));
        ListQueryResponse<GetDeviceListItem> fromTomorrow = await ListAsync(
            dispatcher, term: marker, filtering: Range(DeviceListFields.CreatedAtUtc, future, null));

        Assert.Equal(1, inRange.Total);
        Assert.Equal(0, fromTomorrow.Total);
    }

    [Theory]
    [InlineData(null, """[{"id":"secret","desc":false}]""", "Sorting")]
    [InlineData("""[{"id":"secret","value":"x","mode":"equals"}]""", null, "Filtering")]
    [InlineData("""[{"id":"manufacturerId","value":"x","mode":"contains"}]""", null, "Filtering")]
    [InlineData("""[{"id":"manufacturerId","value":"not-a-guid","mode":"equals"}]""", null, "Filtering")]
    public async Task AnUndeclaredSortOrFilter_IsAValidationError(string? filtering, string? sorting, string property)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ListQueryResponse<GetDeviceListItem>> result = await dispatcher.SendAsync(new GetDeviceListQueryCommand(
            Paggination.Create(10, 1),
            filtering: filtering is null ? null : Filtering.Create(filtering),
            sorting: sorting is null ? null : Sorting.Create(sorting)));

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(property, error.Code);
    }

    private static async Task<ListQueryResponse<GetDeviceListItem>> ListAsync(
        ISerginDispatcher dispatcher, string? term = null, Filtering? filtering = null, Sorting? sorting = null)
    {
        ErrorOr<ListQueryResponse<GetDeviceListItem>> result = await dispatcher.SendAsync(new GetDeviceListQueryCommand(
            Paggination.Create(100, 1), term is null ? null : new Term(term), filtering, sorting));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);

        return result.Value;
    }

    private static Sorting Sort(string id, bool desc) =>
        Sorting.Create($$"""[{"id":"{{id}}","desc":{{(desc ? "true" : "false")}}}]""");

    private static Filtering Filter(string id, string value) =>
        Filtering.Create($$"""[{"id":"{{id}}","value":"{{value}}","mode":"equals"}]""");

    private static Filtering Range(string id, string? from, string? to) =>
        Filtering.Create($$"""[{"id":"{{id}}","value":[{{Json(from)}},{{Json(to)}}],"mode":"between"}]""");

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";
}
