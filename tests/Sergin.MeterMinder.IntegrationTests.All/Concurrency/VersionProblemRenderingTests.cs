using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>Both version errors render with their HTTP status and their code's declared title and detail (common.errors.*).</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class VersionProblemRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public void Required_RendersAs428()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Required, Localizer());

        Assert.Equal(StatusCodes.Status428PreconditionRequired, problem.StatusCode);
        Assert.Equal(ErrorTexts.VersionRequired.Title.Default, problem.Title);
        Assert.Equal(ErrorTexts.VersionRequired.Detail.Default, problem.Detail);
    }

    [Fact]
    public void Stale_RendersAs412()
    {
        SerginProblem problem = SerginProblemFactory.Create(VersionErrors.Stale, Localizer());

        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
        Assert.Equal(ErrorTexts.VersionStale.Title.Default, problem.Title);
        Assert.Equal(ErrorTexts.VersionStale.Detail.Default, problem.Detail);
    }

    private IStringLocalizer Localizer() => factory.Services.GetRequiredService<IStringLocalizer>();
}
