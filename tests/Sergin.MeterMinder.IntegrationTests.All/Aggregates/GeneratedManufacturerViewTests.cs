using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated Manufacturer list shows. A list's rows load after the first render, so the prerender pins the
/// chrome (columns, search, create) and the view model pins what each row carries (links, edit and delete commands).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedManufacturerViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task ManufacturerList_RendersColumnsSearchAndCreate()
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/dm/manufacturers", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Search name or address", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/dm/manufacturers/new\"", html, StringComparison.Ordinal);
        Assert.Contains(">New manufacturer", html, StringComparison.Ordinal);
        Assert.Contains(">Name<", html, StringComparison.Ordinal);
        Assert.Contains(">Address<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ManufacturerList_RowCommands_EditLinkAndDelete()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        var id = Guid.CreateVersion7();

        // Each row's Name links to the detail page and its Edit icon to the edit page.
        Assert.Equal($"/dm/manufacturers/{id}", model.Routes.Detail!(id));
        Assert.Equal($"/dm/manufacturers/{id}/edit", model.Routes.Edit!(id));
        Assert.Equal(nameof(GetManufacturerListItem.Name), model.ListTitleField, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(model.Commands, command => command.Request == typeof(UpdateManufacturerCommand));

        // Delete is the row icon and the bulk action, so it runs once per selected row.
        CommandView delete = Assert.Single(model.Commands, command => command.Request == typeof(DeleteManufacturerCommand));
        Assert.True(delete.Selectable);
        Assert.NotNull(delete.ConfirmManyText);
    }
}
