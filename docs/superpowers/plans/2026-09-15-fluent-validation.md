# FluentValidation Implementation Plan

Companion to `docs/superpowers/specs/2026-09-15-fluent-validation-design.md`. Records how the
change was rolled out across the three repositories, so the next cross-repo change can copy the
sequence.

## Global constraints

- Warnings are errors, `AnalysisMode=All`, Sonar — every file must analyse cleanly on first build.
- `src/SharedKernel` and `src/Modules/UserAccess` are submodules. Their changes land as PRs in
  their own repositories; this repository bumps the pointers afterwards.
- No new package. `FluentValidation` 12.1.1 was already referenced by
  `Sergin.SharedKernel.Application` and flows transitively into every `.Application` project.
- Commit under the user's identity only.

## Sequence

### Task 0: Worktree

`git worktree add .claude/worktrees/fluent-validation -b feat/fluent-validation main`, then
`git submodule update --init --recursive` inside it (a fresh worktree checks both submodules out
empty). Branch `feat/fluent-validation` inside each submodule directory.

### Task 1: SharedKernel — [PR #13](https://github.com/poursh/Sergin.SharedKernel/pull/13)

- `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`: `AddIntegrationEventTranslators` →
  `AddClosedGenericImplementations(services, assembly, openInterface, lifetime)`; called for
  `IIntegrationEventTranslator<>` (transient) and `IValidator<>` (scoped) per local module.
- `Sergin.SharedKernel.Presentation/Errors/SerginProblemFactory.cs`: `ValidationTitleKey`;
  `Validation` detail is `error.Description`.
- `Sergin.SharedKernel.Presentation.WebApi/Endpoints/Results/ApiProblemResults.cs`: whole-list
  `Problem` overload answering `Results.ValidationProblem`; `ResultExtensions.ToApiResult` calls it.
- `Sergin.SharedKernel.Presentation.Blazor/Errors/IUiErrorPresenter.cs` + `MudUiErrorPresenter.cs`:
  `Notify(IReadOnlyList<Error>)`.
- `.claude/CLAUDE.md`.
- Verified: `dotnet build Sergin.SharedKernel.slnx` standalone, then the full
  `Sergin.MeterMinder.slnx` with the branch mounted.

### Task 2: UserAccess — [PR #7](https://github.com/poursh/Sergin.UserAccess/pull/7)

Depends on Task 1 for the `Notify` overload.

- `Sergin.UserAccess.Domain/Users/User.cs`: `UserName.MaxLength` (100),
  `ExternalUserId.MaxLength` (200), `EmailAddress.MaxLength` (320), `User.NameMaxLength` (200).
- `Sergin.UserAccess.Application/Users/Commands/{Create,DeactivateUser,ProvisionExternalUser}/
  <Feature>CommandValidator.cs`.
- `Sergin.UserAccess.Infrastructure.Data/Users/UserEntityTypeConfiguration.cs`: `HasMaxLength`
  reads the constants (values unchanged, no migration).
- `Sergin.UserAccess.Presentation.Blazor/Users/Models/NewUserFormModel.cs`:
  `[StringLength(Domain.Users.UserName.MaxLength, MinimumLength = 1)]`.
- `Sergin.UserAccess.Presentation.Blazor/Users/Pages/CreateUserPage.razor.cs`:
  `Notify(result.Errors)`.
- `.claude/CLAUDE.md`.

### Task 3: This repository

- `src/Modules/DeviceManagement/…Domain/Devices/Device.cs`: `DeviceId.MaxLength` (100);
  `…Domain/Manufacturers/Manufacturer.cs`: `ManufacturerName.MaxLength` (200),
  `ManufacturerAddress.MaxLength` (500).
- `…Application/Devices/Commands/Create/CreateDeviceCommandValidator.cs`,
  `…Application/Manufacturers/Commands/Create/CreateManufacturerCommandValidator.cs`.
- `…Presentation.Blazor/Devices/Models/NewDeviceFormModel.cs`: reads
  `Domain.Devices.DeviceId.MaxLength`; `…Pages/CreateDevicePage.razor.cs`: `Notify(result.Errors)`.
- `tests/Sergin.MeterMinder.IntegrationTests.All/Validation/CommandValidationTests.cs` and
  `ValidationProblemRenderingTests.cs`.
- `.claude/CLAUDE.md`, `.claude/skills/add-feature/SKILL.md`,
  `src/Modules/DeviceManagement/CLAUDE.md`, this spec and plan.
- Submodule pointers bumped to the merged commits of Tasks 1 and 2.
- `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`.

## Verification

1. `dotnet build Sergin.MeterMinder.slnx` — clean.
2. Docker Desktop running, then
   `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
   — 50 tests, the seven new ones included, all green.
3. `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`: `/ua/users/new` and
   `/dm/devices/new` render, DataAnnotations still block an empty submit, a valid submit still
   navigates to the detail page. Server-side validation is not reachable from the UI while the
   form rules repeat the pipeline rules — the tests are its proof.

## Gotchas met on the way

- `Results.ValidationProblem` returns a `ProblemHttpResult` whose `ProblemDetails` is
  `HttpValidationProblemDetails`; only `TypedResults.ValidationProblem` returns the
  `ValidationProblem` result type. The test asserts the former.
- Inside `NewUserFormModel`, `UserName.MaxLength` resolves to the model's own `UserName` property
  (CS0120); qualify the type as `Domain.Users.UserName.MaxLength`. Same for `NewDeviceFormModel`.
- IDE0007 (`var` over an explicit type) is enforced in SharedKernel for a local whose type is
  apparent from the right-hand side — `var errorsByProperty = errors.GroupBy(...).ToDictionary(...)`.
