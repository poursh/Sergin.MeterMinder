using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.SharedKernel.Presentation.WebApi.Endpoints.Results;

namespace Sergin.MeterMinder.IntegrationTests.All.Validation;

/// <summary>
/// How a validation error reaches a caller. ValidationPipelineBehavior puts the offending property name
/// in Code and FluentValidation's message in Description, so the presentation layer must show the
/// Description rather than look the Code up as a resource key, and must show every error rather than
/// the first. Pure mapping; the fixture is used only for the host's ILocalizer.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ValidationProblemRenderingTests(SerginWebApiFactory<Program> factory)
{
    private ILocalizer Localizer => factory.Services.GetRequiredService<ILocalizer>();

    [Fact]
    public void SerginProblemFactory_ValidationError_UsesTheMessageAsDetail()
    {
        const string message = "'User Name' must not be empty.";

        SerginProblem problem = SerginProblemFactory.Create(Error.Validation("UserName", message), Localizer);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(message, problem.Detail);
        Assert.Equal(Localizer[SerginProblemFactory.ValidationTitleKey], problem.Title);
    }

    [Fact]
    public void ApiProblemResults_AllValidationErrors_ProducesValidationProblem()
    {
        IReadOnlyList<Error> errors =
        [
            Error.Validation("UserName", "'User Name' must not be empty."),
            Error.Validation("UserName", "The length of 'User Name' must be 100 characters or fewer."),
            Error.Validation("ManufacturerId", "'Manufacturer Id' must not be empty."),
        ];

        IResult result = ApiProblemResults.Problem(errors, Localizer);

        // Results.ValidationProblem answers a ProblemHttpResult carrying HttpValidationProblemDetails —
        // the errors dictionary is what an API client reads, one entry per property.
        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        HttpValidationProblemDetails details = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(2, details.Errors["UserName"].Length);
        Assert.Single(details.Errors["ManufacturerId"]);
    }

    [Fact]
    public void ApiProblemResults_MixedErrors_FallsBackToTheFirst()
    {
        IReadOnlyList<Error> errors = [Error.NotFound(), Error.Validation("UserName", "unused")];

        IResult result = ApiProblemResults.Problem(errors, Localizer);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }
}
