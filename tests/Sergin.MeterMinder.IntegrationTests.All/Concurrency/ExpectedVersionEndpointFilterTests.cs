using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.WebApi.Concurrency;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// The filter on a one-endpoint test server: a strong If-Match becomes the expected version, a version the
/// endpoint leaves in Current becomes the ETag, a malformed tag is a 400, and * or a weak tag counts as absent
/// (so a guarded command then answers 428 from the pipeline).
/// </summary>
public sealed class ExpectedVersionEndpointFilterTests : IAsyncLifetime
{
    private static readonly Guid Written = Guid.CreateVersion7();

    private WebApplication app = default!;
    private HttpClient client = default!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ConcurrencyContext>();

        app = builder.Build();

        RouteGroupBuilder group = app.MapGroup("/t").AddEndpointFilter<ExpectedVersionEndpointFilter>();
        group.MapPost("/echo", (ConcurrencyContext concurrency) =>
        {
            Guid? seen = concurrency.Expected?.Value;
            concurrency.Current = RowVersion.Create(Written);
            return Results.Ok(seen);
        });

        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task StrongIfMatch_BecomesTheExpectedVersion_AndCurrentBecomesTheETag()
    {
        var sent = Guid.CreateVersion7();

        using HttpResponseMessage response = await PostAsync($"\"{sent}\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sent, await response.Content.ReadFromJsonAsync<Guid?>());
        Assert.Equal($"\"{Written}\"", response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"0192a000-0000-7000-8000-000000000001\"")]
    public async Task AbsentWildcardOrWeakTag_LeavesNoExpectedVersion(string? ifMatch)
    {
        using HttpResponseMessage response = await PostAsync(ifMatch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Results.Ok writes no body at all for a null value (it never emits the literal "null"), so a null
        // echo is read as an empty response rather than deserialized.
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(body), $"Expected no echoed version, got '{body}'.");
    }

    [Theory]
    [InlineData("not-a-tag")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    public async Task MalformedTag_IsA400(string ifMatch)
    {
        using HttpResponseMessage response = await PostAsync(ifMatch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostAsync(string? ifMatch)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/t/echo");

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }
}
