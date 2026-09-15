# Repository Existence and Uniqueness Implementation Plan

Companion to `docs/superpowers/specs/2026-09-15-repository-existence-and-uniqueness-design.md`.
Records how the change was rolled out across the three repositories, in the same sequence as the
FluentValidation change before it, so the next cross-repo change can copy it.

## Global constraints

- Warnings are errors, `AnalysisMode=All`, Sonar, `EnforceCodeStyleInBuild` — every file must
  analyse cleanly on first build, scaffolded migrations included (see Gotchas).
- `src/SharedKernel` and `src/Modules/UserAccess` are submodules. Their changes land as PRs in
  their own repositories; this repository points at the feature branches until they merge and
  bumps to the merged commits afterwards.
- No new package. `FluentValidation` and EF Core were already referenced where the new code
  lives; the test project reaches `Microsoft.EntityFrameworkCore` and `Npgsql` transitively.
- Commit under the user's identity only.

## Sequence

### Task 0: Worktree

`git worktree add .claude/worktrees/repo-existence -b feat/repository-existence-uniqueness main`,
then `git submodule update --init --recursive` inside it. Branch
`feat/repository-existence-uniqueness` inside each submodule directory.

### Task 1: SharedKernel — [PR #14](https://github.com/poursh/Sergin.SharedKernel/pull/14), commit `c7ed252`

The half every other task depends on.

- `Sergin.SharedKernel.Domain/Repositories/IRepository.cs`: `ExistsAsync(TId, ct)`.
- `Sergin.SharedKernel.Domain/Repositories/IUniqueKeyRepository.cs`: new, `IsTakenAsync(TKey, ct)`.
- `Sergin.SharedKernel.Infrastructure.Data.EFCore/Repositories/EfRepository.cs`: new, the
  abstract base — `GetAsync`, `ExistsAsync`, `protected AnyAsync`, `Insert`, `Remove`.
- `Sergin.SharedKernel.Application/Validations/RepositoryRuleBuilderExtensions.cs`: new,
  `MustExistIn` and `MustBeUniqueIn`.
- `.claude/CLAUDE.md`.
- Verified: `dotnet build Sergin.SharedKernel.slnx` standalone. The full solution does not build
  at this point — the four module repositories still implement `IRepository` by hand and lack
  `ExistsAsync` — which is what forces Tasks 2 and 3 to follow, and why the base class exists.

### Task 2: UserAccess — [PR #8](https://github.com/poursh/Sergin.UserAccess/pull/8), commit `cdc087e`

Depends on Task 1 for everything.

- `Sergin.UserAccess.Domain/Users/IUserRepository.cs`: `+ IUniqueKeyRepository<UserName>`.
- `Sergin.UserAccess.Infrastructure/Users/Repositories/UserRepository.cs` and
  `Roles/Repositories/RoleRepository.cs`: derive from `EfRepository`; `UserRepository` adds
  `IsTakenAsync`.
- `Sergin.UserAccess.Application/Users/Commands/Create/CreateUserCommandValidator.cs`:
  `(IUserRepository users)`, `MustBeUniqueIn(users)` on `UserName` behind `.When`.
- `Sergin.UserAccess.Infrastructure.Data/Users/UserEntityTypeConfiguration.cs`:
  `HasIndex(u => u.UserName).IsUnique()`; migration `20260915140555_AddUserNameUniqueIndex`
  (`ix_users_user_name`).
- `ProvisionExternalUserCommandValidator`: untouched, on purpose.
- `.claude/CLAUDE.md`.

### Task 3: This repository — commit `9e36d59`, then the tests and docs commit

- `src/Modules/DeviceManagement/…Domain/Devices/IDeviceRepository.cs`:
  `+ IUniqueKeyRepository<DeviceId>`.
- `…Infrastructure/Devices/Repositories/DeviceRepository.cs` and
  `…Manufacturers/Repositories/ManufacturerRepository.cs`: derive from `EfRepository`;
  `DeviceRepository` adds `IsTakenAsync`, `ManufacturerRepository` loses its body.
- `…Application/Devices/Commands/Create/CreateDeviceCommandValidator.cs`:
  `(IDeviceRepository devices, IManufacturerRepository manufacturers)`, `MustBeUniqueIn(devices)`
  on `DeviceId` and `MustExistIn(manufacturers)` on `ManufacturerId`, each behind `.When`.
- `…Infrastructure.Data/Devices/DeviceEntityTypeConfiguration.cs`:
  `HasIndex(d => d.DeviceId).IsUnique()`; migration `20260915140708_AddDeviceIdUniqueIndex`
  (`ix_device_device_id`).
- Submodule pointers to `c7ed252` and `cdc087e` (the feature branches; re-bumped after the two
  PRs merge).
- `tests/Sergin.MeterMinder.IntegrationTests.All/Validation/RepositoryRuleTests.cs` — seven tests,
  listed in the spec.
- `.claude/CLAUDE.md`, `.claude/skills/add-feature/SKILL.md`,
  `src/Modules/DeviceManagement/CLAUDE.md`, this spec and plan.
- `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`.

### Task 4: After the submodule PRs merge

Bump `src/SharedKernel` and `src/Modules/UserAccess` to the merged `main` commits, commit the
pointer move on its own (the "pure submodule pointer drift" case), and merge the host PR.

## Verification

1. `dotnet build Sergin.MeterMinder.slnx` — clean, 0 warnings.
2. Docker Desktop running, then
   `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj`
   — 57 tests, the seven new ones included, all green. `ExternalIdentityProvisioningTests` is the
   one to watch: it upserts by username against a column that is now uniquely indexed.
3. By hand, `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`: on `/dm/devices/new`,
   pick a manufacturer and submit a device id that already exists — expect one snackbar reading
   `'Device Id' is already in use.` and no navigation. This is the first server-side validation
   rule reachable from the UI, since the form cannot repeat it client-side; the tests are its
   automated proof.

## Gotchas met on the way

- **IDE0161 on scaffolded migrations.** `dotnet ef migrations add` writes the migration `.cs`
  with a block-scoped namespace, and `.editorconfig` makes file-scoped an error, so the build
  fails on the file the tool just wrote. Convert the `<Timestamp>_<Name>.cs` to `namespace X;`
  by hand; the `.Designer.cs` and the snapshot are `<auto-generated/>` and exempt. Both migrations
  in this change went through it, and the root CLAUDE.md now says so under "EF Core migrations".
- **`RoleRepository` was the fourth repository.** The estimate was three (`Device`,
  `Manufacturer`, `User`); `IRoleRepository : IRepository<Role, RoleId>` also lost `ExistsAsync`
  the moment Task 1 landed, so UserAccess's PR moved it onto the base class too, without giving it
  a key capability (`ua.roles.name` is already unique and no command creates roles).
- **`FindAsync([id], ct)`, not `FindAsync([id, ct], cancellationToken: ct)`.** Every hand-written
  repository had put the cancellation token into the key-values array. EF tolerated it, but it is
  not a key value; the base class passes the key alone, and the root CLAUDE.md line that used to
  recommend the old shape is rewritten.
- **`TKey` alone on `IUniqueKeyRepository`.** The obvious shape, `<TAggregate, TKey>`, breaks
  C# type inference in `MustBeUniqueIn(users)` as soon as one repository implements the interface
  for two keys — the compiler cannot pick the aggregate argument. Dropping it costs nothing, since a
  value-object type already names its aggregate.
- **What the empty-id test can and cannot prove.** The `.When` guard is meant to keep the
  uniqueness query from running for an empty id, but `IsTakenAsync("")` answers false either way,
  so a test cannot see the skipped query from the outside. `RepositoryRuleTests` pins the errors a
  caller observes — shape on `DeviceId`, existence on `ManufacturerId`, no in-use error — and its
  name says so.
