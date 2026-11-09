using ErrorOr;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Domain;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

internal sealed record TestTypedRootId(Guid Value);

internal sealed record TestTypedChildId(Guid Value);

internal sealed class TestTypedChild : Entity<TestTypedChildId>;

internal sealed class TestTypedRoot : AggregateRoot<TestTypedRootId>;

internal sealed class TestOtherRoot : AggregateRoot<TestTypedRootId>;

internal sealed record TestListQuery(Paggination Paggination) : ListQuery(Paggination);

internal sealed record TestCreatedResponse(Guid Id) : ICreatedResponse;

internal sealed record TestDeleteCommand(TestTypedRootId Id)
    : IDeleteCommand<TestTypedRoot, TestTypedRootId>, ICommand<Deleted>;

internal sealed record TestSelectableDeleteCommand(TestTypedRootId Id)
    : IDeleteCommand<TestTypedRoot, TestTypedRootId>, ISelectable, ICommand<Deleted>;

internal sealed record TestCreateCommand(string Name)
    : ICreateCommand<TestTypedRoot, TestTypedRootId>, ICommand<TestCreatedResponse>;

internal sealed record TestCreateUnderParentCommand(string Name, TestTypedRootId ParentId)
    : ICreateCommand<TestTypedRoot, TestTypedRootId>, IUnderParent<TestTypedRoot, TestTypedRootId>, ICommand<TestCreatedResponse>;

internal sealed record TestAddChildCommand(TestTypedRootId RootId, string Name)
    : IAddChildCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand<TestCreatedResponse>;

internal sealed record TestUpdateCommand(TestTypedRootId Id, string Name)
    : IUpdateCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestUpdateChildCommand(TestTypedRootId RootId, TestTypedChildId Id, string Name)
    : IUpdateCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand;

internal sealed record TestSetPropertyCommand(TestTypedRootId Id, string Name)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestSetPropertyChildCommand(TestTypedRootId RootId, TestTypedChildId Id, string Name)
    : ISetPropertyCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand;

internal sealed record TestTransitionCommand(TestTypedRootId Id)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestReverseTransitionCommand(TestTypedRootId Id)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId>, IReverseOf<TestTransitionCommand>, ICommand;

internal sealed record TestTransitionChildCommand(TestTypedRootId RootId, TestTypedChildId Id)
    : ITransitionCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand;

internal sealed record TestMoveCommand(TestTypedRootId Id, TestTypedRootId NewParentId)
    : IMoveCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestRemoveChildCommand(TestTypedRootId RootId, TestTypedChildId Id)
    : IRemoveChildCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand<Deleted>;

internal sealed record TestAddValueCommand(TestTypedRootId Id, string Value)
    : IAddValueCommand<TestTypedRoot, TestTypedRootId, string>, ICommand;

internal sealed record TestRemoveValueCommand(TestTypedRootId Id, string Value)
    : IRemoveValueCommand<TestTypedRoot, TestTypedRootId, string>, ICommand;

internal sealed record TestSystemCommand : ISystemCommand, ICommand;

internal sealed record TestUntypedCommand(string Name) : ICommand;

internal sealed record TestGetOneQuery(TestTypedRootId Id)
    : IGetOneQuery<TestTypedRoot, TestTypedRootId>, IQuery<string>;

internal sealed record TestChildGetOneQuery(TestTypedRootId RootId, TestTypedChildId Id)
    : IChildGetOneQuery<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, IQuery<string>;

internal sealed record TestAuditQuery(TestTypedRootId Id)
    : IAuditQuery<TestTypedRoot, TestTypedRootId>, IQuery<string>;

internal sealed record TestEntityOverviewQuery(TestTypedRootId Id)
    : IEntityOverviewQuery<TestTypedRoot, TestTypedRootId>, IQuery<string>;

internal sealed record TestEntityViewQuery(TestTypedRootId Id)
    : IEntityViewQuery<TestTypedRoot, TestTypedRootId>, IQuery<string>;

internal sealed record TestListOfQuery
    : ListQuery<string>, IListOf<TestTypedRoot>
{
    public TestListOfQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

internal sealed record TestTreeQuery : ITreeQuery<TestTypedRoot>, IQuery<string>;

internal sealed record TestOverviewQuery : IOverviewQuery<TestTypedRoot>, IQuery<string>;

internal sealed record TestChildListOfQuery(TestTypedRootId RootId)
    : IChildListOf<TestTypedRoot, TestTypedRootId, TestTypedChild>, IQuery<string>;

internal sealed record TestCriteriaCommand(Term? Term, Filtering? Filtering, int ExpectedCount)
    : ICriteriaCommand<TestTypedRoot, TestListQuery>, ICommand;

internal sealed record TestImportCommand(IReadOnlyList<string> Items)
    : IImportCommand<TestTypedRoot, string>, ICommand;

internal sealed record TestCloneCommand(TestTypedRootId Id)
    : ICloneCommand<TestTypedRoot, TestTypedRootId>, ICommand<TestCreatedResponse>;

internal sealed record TestRestoreCommand(TestTypedRootId Id)
    : IRestoreCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestPurgeCommand(TestTypedRootId Id)
    : IPurgeCommand<TestTypedRoot, TestTypedRootId>, ICommand;

internal sealed record TestReorderCommand(TestTypedRootId RootId, TestTypedChildId Id, int Position)
    : IReorderCommand<TestTypedRoot, TestTypedRootId, TestTypedChild, TestTypedChildId>, ICommand;

internal sealed record TestExportQuery(Term? Term, Filtering? Filtering)
    : IExportQuery<TestTypedRoot, TestListQuery>, IQuery<string>;

internal sealed record TestTwoRootsCommand(TestTypedRootId Id)
    : IDeleteCommand<TestTypedRoot, TestTypedRootId>, IDeleteCommand<TestOtherRoot, TestTypedRootId>, ICommand;

internal sealed record TestTwoKindsCommand(TestTypedRootId Id)
    : IDeleteCommand<TestTypedRoot, TestTypedRootId>, ICreateCommand<TestTypedRoot, TestTypedRootId>, ICommand;
