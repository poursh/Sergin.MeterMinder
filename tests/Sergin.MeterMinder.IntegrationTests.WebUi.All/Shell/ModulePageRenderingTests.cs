using System.Net;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.WebUi.All.Shell;

[Collection(nameof(WebUiIntegrationTestCollection))]
public sealed class ModulePageRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Theory]
    [InlineData("/mm/devices")]
    [InlineData("/ua/users")]
    [InlineData("/mm/devices/new")]
    [InlineData("/ua/users/new")]
    public async Task ModulePage_RendersServerSide_WithNavFromBothModules(string path)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();

        // Both modules contributed nav entries, so the shell composed them.
        Assert.Contains("/mm/devices", html, StringComparison.Ordinal);
        Assert.Contains("/ua/users", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateUserPage_And_UserListPage_ShareTheSameData()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage listResponse = await client.GetAsync("/ua/users");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        HttpResponseMessage createResponse = await client.GetAsync("/ua/users/new");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains("User name", await createResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
