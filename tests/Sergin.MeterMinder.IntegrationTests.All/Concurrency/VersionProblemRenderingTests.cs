using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>Both version errors render with their HTTP status and their code's title and detail.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class VersionProblemRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Required_RendersAs428()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Required, Localizer());

        Assert.Equal(StatusCodes.Status428PreconditionRequired, problem.StatusCode);
        Assert.Equal(Localizer()["General.VersionRequired.title"].Value, problem.Title);
        Assert.Equal(Localizer()["General.VersionRequired"].Value, problem.Detail);
    }

    [Fact]
    public void Stale_RendersAs412()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Stale, Localizer());

        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
        Assert.Equal(Localizer()["General.VersionStale.title"].Value, problem.Title);
        Assert.Equal(Localizer()["General.VersionStale"].Value, problem.Detail);
    }

    private ILocalizer Localizer() => factory.Services.GetRequiredService<ILocalizer>();
}
