using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Presentation.WebApi.Authorization;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// The filter on a one-endpoint test server: X-Sergin-Scope becomes the current scope, no header leaves the root,
/// and anything but one non-empty GUID is a 400.
/// </summary>
public sealed class AuthorizationScopeEndpointFilterTests : IAsyncLifetime
{
    private WebApplication app = default!;
    private HttpClient client = default!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<IAuthorizationScopeContext, AuthorizationScopeContext>();

        app = builder.Build();

        RouteGroupBuilder group = app.MapGroup("/t").AddEndpointFilter<AuthorizationScopeEndpointFilter>();
        group.MapPost("/echo", (IAuthorizationScopeContext scope) => Results.Ok(scope.ScopeId));

        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Header_BecomesTheCurrentScope()
    {
        var sent = Guid.CreateVersion7();

        using HttpResponseMessage response = await PostAsync(sent.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sent, await response.Content.ReadFromJsonAsync<Guid>());
    }

    [Fact]
    public async Task NoHeader_LeavesTheRoot()
    {
        using HttpResponseMessage response = await PostAsync(null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AuthorizationScope.RootId, await response.Content.ReadFromJsonAsync<Guid>());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MalformedHeader_IsA400(string header)
    {
        using HttpResponseMessage response = await PostAsync(header);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostAsync(string? header)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/t/echo");

        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(AuthorizationScopeEndpointFilter.HeaderName, header);
        }

        return await client.SendAsync(request);
    }
}
