using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Users.Commands.Create;
using Sergin.UserAccess.Application.Users.Commands.GetList;
using Sergin.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Users;

/// <summary>
/// The one test covering a write end to end: command handler → domain factory → EF repository →
/// SaveChangesAsync → raw-SQL read back. It used to drive <c>POST /ua/users</c> over HTTP; dropping the Web
/// API host removed that entry point, so it now dispatches in-process exactly as the Blazor UI does.
/// Everything below MediatR is the same code either way.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CreateAndGetUserTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task CreateUser_ThenListUsers_IncludesCreatedUser()
    {
        // ISerginDispatcher (ScopedSerginDispatcher) opens a fresh DI scope per send, so the write and the
        // read run in separate scopes — the list genuinely round-trips through Postgres rather than being
        // served out of the writing DbContext's change tracker. It is itself scoped, because it carries the
        // caller's IUserContext into each of those scopes, so it comes from a scope and not the root
        // provider — which is what a Blazor page's circuit scope does too.
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher sender = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        string userName = $"integration-test-{Guid.CreateVersion7()}";

        ErrorOr<CreateUserCommandResponse> created =
            await sender.SendAsync(new CreateUserCommand(new UserName(userName)));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        ErrorOr<ListQueryResponse<GetUserListItem>> list =
            await sender.SendAsync(new GetUserListQueryCommand(Paggination.Create(100, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);

        Assert.Contains(list.Value.Data, item => item.Id == created.Value.Id && item.UserName == userName);
    }
}
