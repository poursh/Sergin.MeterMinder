# Administration Modules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put UserAccess and Platform under the `Sergin.Administration` name, rename Platform to Settings with
a new runtime identity (`st`), show both modules in one "Administration" drawer group, and replace every
module's migration history with one regenerated `InitialCreate`.

**Architecture:** Four repos change. SharedKernel gains a drawer group that several modules can share. The
two submodules are renamed and moved under `src/Modules/Administration/`. The host is rewired, and all three
module contexts get a fresh `InitialCreate`, checked against a schema dump of today's migration chain.

**Tech Stack:** .NET 10, EF Core + Npgsql, PostgreSQL 17, Blazor Server + MudBlazor 9, xUnit + Testcontainers,
git submodules, `gh` CLI.

**Spec:** `docs/superpowers/specs/2026-11-06-administration-modules-design.md`

## Global Constraints

- Work in a git worktree off branch `docs/administration-modules-spec`, which holds the spec and this plan (`.claude/worktrees/administration-modules`, branch `administration-modules`). Run `git submodule update --init --recursive` in it before the first build.
- In each submodule, work on a branch of the same name, `administration-modules`.
- `TreatWarningsAsErrors` is on: every build in this plan must pass with zero warnings.
- Settings runtime identity: module `Name` `"Settings"`, schema `st`, routes `/st/...`, text keys `settings.*`, permissions `permission.st.*`.
- UserAccess runtime identity is unchanged: `Name` `"UserAccess"`, schema `ua`, routes `/ua/...`, keys `user-access.*`, permissions `permission.ua.*`.
- Naming: `Sergin.Platform[.Layer]` → `Sergin.Administration.Settings[.Layer]`; `Sergin.UserAccess[.Layer]` → `Sergin.Administration.UserAccess[.Layer]`; every `Platform*` type → `Settings*` (for example `PlatformModule` → `SettingsModule`); `UserAccessModule` keeps its name.
- **Do not rename** `PermissionDefinition.AllPlatform`, `permission.sys.platform-all`, or prose that says "the Sergin platform". There "platform" means the product.
- Disk layout: `src/Modules/Administration/UserAccess`, `src/Modules/Administration/Settings`. SharedKernel references inside them go from `..\..\..\SharedKernel\` to `..\..\..\..\SharedKernel\`.
- Scaffolded migrations are converted to a file-scoped namespace by hand (IDE0161). `.Designer.cs` and the snapshot stay as generated.
- Commits are under the user's git identity, with no `Co-Authored-By` trailer (root CLAUDE.md rule).
- GitHub repo renames and every `git push` and PR are outward-facing: confirm each one with the user before running it.

## Review Focus

- **A user who can see only one module of the shared group:** the group shows, with only that module's entries. Test in Task 1 (`NavVisibilityTests`).
- **Navigation between the two modules:** the Administration group stays open moving from `/ua/users` to `/st/languages`, and the breadcrumb on both reads `Home > Administration > …`. Test in Task 3 (`BreadcrumbRenderingTests`).
- **Seed rows after the reset:** a fresh database has the `administrator` role holding `permission.sys.platform-all` and the Root scope, and the `viewer` role holding exactly today's permissions. Pinned by the Task 5 dump diff and existing `ExternalIdentityProvisioningTests`.
- **Old routes:** `/pf/languages` no longer resolves. There is no redirect, which is acceptable with no deployment. Test in Task 3 (`OldPlatformRoute_IsNotFound`).
- **A stale dev database:** the new `InitialCreate` fails against a database that already has tables. The README states the `docker compose down -v` step. Task 7.

---

### Task 1: SharedKernel shared drawer group

**Files:**
- Create: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Modules/SerginNavGroups.cs`
- Modify: `src/SharedKernel/Sergin.SharedKernel.Application/Localizations/Common/ShellTexts.cs` (`Navigation` class)
- Modify: `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Modules/SerginUiModuleCatalog.cs`
- Test: `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/NavSectionCatalogTests.cs`, `tests/Sergin.MeterMinder.IntegrationTests.All/Shell/NavVisibilityTests.cs`

**Interfaces:**
- Produces: `ShellTexts.Navigation.AdministrationGroup : TextDefinition` (key `common.navigation.administration-group`, default `"Administration"`).
- Produces: `public static class SerginNavGroups { public static SerginNavGroup Administration { get; } }` in namespace `Sergin.SharedKernel.Presentation.Blazor.Modules`: title `ShellTexts.Navigation.AdministrationGroup`, icon `Icons.Material.Filled.AdminPanelSettings`, `Order: 200`.
- Produces: `SerginUiModuleCatalog.NavSections` has one section per distinct `SerginNavGroup` (value equality), not per module.

- [ ] **Step 1: Write the failing tests** in `NavSectionCatalogTests`: add a second catalog with two modules (`"cc"`, `"dd"`) that return the same `SerginNavGroup` instance, with items at Order 210 (`/cc/b`) and 200 (`/dd/a`), plus a third module with its own group.

```csharp
[Fact]
public void ModulesSharingAGroup_FormOneSection_WithEveryModulesItemsInOrder()
{
    SerginNavSection shared = SharedCatalog.NavSections.Single(section => section.Group == SharedGroup);
    Assert.Equal(["/dd/a", "/cc/b"], shared.Items.Select(item => item.Href));
    Assert.Equal(2, SharedCatalog.NavSections.Count);
}

[Theory]
[InlineData("/cc/b")]
[InlineData("/dd/a")]
public void GroupOf_EitherModulesHref_IsTheSharedGroup(string href)
    => Assert.Same(SharedGroup, SharedCatalog.GroupOf(href));
```

  In `NavVisibilityTests`, add `SharedGroup_ShowsOnlyTheEntriesTheUserMaySee`: one module's entries require a permission the test user lacks, and the rendered drawer shows the shared group with only the other module's entries. Follow that file's existing render-and-assert shape.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~NavSectionCatalogTests|FullyQualifiedName~NavVisibilityTests"`
Expected: the new tests FAIL (two sections for the shared group); existing tests pass.

- [ ] **Step 3: Implement.** Add `AdministrationGroup` to `ShellTexts.Navigation` and create `SerginNavGroups`. In the catalog constructor, build `NavSections` with `modules.GroupBy(module => module.NavGroup)`, ordered by `group.Key.Order` then `group.Key.Title.Default` (ordinal), with each section's items being `InDrawerOrder(group.SelectMany(module => module.NavItems))`. Update the `NavSections` doc comment ("one section per group") and the `SerginNavMenu` class summary ("one collapsible group per nav group").

- [ ] **Step 4: Run the Shell tests**

Run: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All --filter "FullyQualifiedName~Shell"`
Expected: PASS, including the existing ordering tests.

- [ ] **Step 5: Commit in the SharedKernel submodule, then in the host**

```bash
git -C src/SharedKernel add -A && git -C src/SharedKernel commit -m "Nav groups shared by several modules; SerginNavGroups.Administration"
git add src/SharedKernel tests/Sergin.MeterMinder.IntegrationTests.All/Shell && git commit -m "Shared drawer group: SharedKernel bump and tests"
```

### Task 2: Capture the migration baseline

No code changes. This task produces the dumps Task 5 diffs against. Run it on the worktree **before** Task 3, while the old migration chains are still in place.

**Interfaces:**
- Produces: `<scratch>/baseline/schema.sql` and `<scratch>/baseline/seed.sql`, where `<scratch>` is outside the repo (the session scratchpad, or `%TEMP%\sergin-administration`).

- [ ] **Step 1: Start a throwaway database**

```bash
docker run -d --name sergin-baseline -e POSTGRES_PASSWORD=baseline -p 55432:5432 postgres:17
```

- [ ] **Step 2: Apply today's chains.** Run the host in Development with
  `Sergin__ConnectionStrings__Database="Host=localhost;Port=55432;Database=Sergin_DB;Username=postgres;Password=baseline"`
  via `dotnet run --project src/Hosts/Sergin.MeterMinder.Hosts.All`. Stop it once it logs `Now listening on`. Startup migrates all three modules.

- [ ] **Step 3: Dump**

```bash
docker exec sergin-baseline pg_dump -U postgres -d Sergin_DB --schema-only --no-owner > <scratch>/baseline/schema.sql
docker exec sergin-baseline pg_dump -U postgres -d Sergin_DB --data-only --no-owner -t ua.roles -t ua.role_permissions -t ua.scopes > <scratch>/baseline/seed.sql
docker rm -f sergin-baseline
```

  Expected: `schema.sql` contains `CREATE SCHEMA dm`, `ua`, `pf`; `seed.sql` contains the rows `administrator`, `viewer`, `Root`.

### Task 3: Platform becomes Sergin.Administration.Settings

**Files:**
- Move: `src/Modules/Platform/` → `src/Modules/Administration/Settings/` (a submodule move). Inside it, every `Sergin.Platform*` project directory and `.csproj` → `Sergin.Administration.Settings*`.
- Modify: `.gitmodules`, `Sergin.MeterMinder.slnx`, `src/Hosts/Sergin.MeterMinder.Hosts.All/Sergin.MeterMinder.Hosts.All.csproj` (two references and the comment), `src/Hosts/Sergin.MeterMinder.Hosts.All/Program.cs`.
- Modify inside the module: `SettingsModule.cs` (`Name`), `SettingsDbContext.cs` (`Schema = "st"`), `SettingsTexts.cs`, `LanguageTexts.cs`, `TranslationTexts.cs` (category module `"Settings"`), every `@page "/pf/` → `"/st/`, every `"permission.pf.` → `"permission.st.`, `SettingsNavigation.cs`.
- Delete then regenerate: `Sergin.Administration.Settings.Infrastructure.Data/Migrations/`.
- Test: every test file referencing `Sergin.Platform`, `PlatformModule`, `/pf/` or `permission.pf.` (about 15 files under `Localization/`, `Permissions/`, `Commands/`, `Shell/`). `PlatformModuleTests.cs` → `SettingsModuleTests.cs`, `PlatformPageRenderingTests.cs` → `SettingsPageRenderingTests.cs`.

**Interfaces:**
- Consumes: `SerginNavGroups.Administration` (Task 1).
- Produces: `Sergin.Administration.Settings.SettingsModule : ISerginWebApiModule, ISerginWebUiModule`; its Blazor RCL `Sergin.Administration.Settings.Presentation.Blazor`; `SettingsNavigation.Group => SerginNavGroups.Administration`, `SettingsNavigation.Languages` at `/st/languages`, `Order: 300`.

- [ ] **Step 1: Rename the GitHub repo.** Confirm with the user first, then run `gh repo rename Sergin.Administration.Settings -R poursh/Sergin.Platform`.

- [ ] **Step 2: Move the submodule**

```bash
mkdir -p src/Modules/Administration
git mv src/Modules/Platform src/Modules/Administration/Settings
git submodule set-url src/Modules/Administration/Settings https://github.com/poursh/Sergin.Administration.Settings.git
```

  Expected: `.gitmodules` shows `path = src/Modules/Administration/Settings` and the new URL.

- [ ] **Step 3: Rename projects, namespaces and types inside the submodule** (`git -C src/Modules/Administration/Settings switch -c administration-modules` first). Use `git mv` for every directory and `.csproj`, then replace `Sergin.Platform` → `Sergin.Administration.Settings` and the `Platform*` type names → `Settings*` in `.cs`, `.razor`, `.csproj` and `.md`, keeping the Global Constraints exclusions. Raise every SharedKernel `ProjectReference` by one `..\`. Set the runtime identity listed under Files.

- [ ] **Step 4: Join the shared group.** `SettingsNavigation.Group` returns `SerginNavGroups.Administration`; `Languages` gets `Order: 300`. Delete `SettingsTexts.MenuTitle`, which nothing reads any more. Keep `SettingsTexts.Category` if other texts use it; otherwise delete the class.

- [ ] **Step 5: Rewire the host.** Two `ProjectReference`s in the host csproj, `using Sergin.Administration.Settings;` plus `new SettingsModule()` in `Program.cs`, and `.slnx` entries under a new `/Modules/Administration/Settings/` folder (mirror the existing Platform folder's shape).

- [ ] **Step 6: Update the tests.** Apply the same renames across `tests/`, and change `/pf/` routes to `/st/` and `permission.pf.` to `permission.st.`. Add two tests to `Shell/BreadcrumbRenderingTests.cs`: `LanguagesPage_TrailNamesTheAdministrationGroup` (prerendered `/st/languages` contains the "Administration" crumb), and `OldPlatformRoute_IsNotFound` (`GET /pf/languages` returns 404).

- [ ] **Step 7: Reset the migrations**

```bash
git -C src/Modules/Administration/Settings rm -r -q Sergin.Administration.Settings.Infrastructure.Data/Migrations
dotnet ef migrations add InitialCreate \
  --project src/Modules/Administration/Settings/Sergin.Administration.Settings.Infrastructure.Data \
  --startup-project src/Hosts/Sergin.MeterMinder.Hosts.All
```

  Convert `<timestamp>_InitialCreate.cs` to a file-scoped namespace. Expected: it creates schema `st` with tables `language` and `translation`.

- [ ] **Step 8: Build and test**

Run: `dotnet build Sergin.MeterMinder.slnx` then `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All`
Expected: 0 warnings, all tests PASS, including `PseudoLocaleSweepTests` and `FieldTextGuardTests`.

- [ ] **Step 9: Commit** in the submodule (`"Rename to Sergin.Administration.Settings; schema st; one InitialCreate"`), then in the host (`"Platform becomes Sergin.Administration.Settings"`), including `.gitmodules`, the moved gitlink, the host, `.slnx` and `tests/`.

### Task 4: UserAccess becomes Sergin.Administration.UserAccess

**Files:**
- Move: `src/Modules/UserAccess/` → `src/Modules/Administration/UserAccess/`. Inside it, every `Sergin.UserAccess*` directory and `.csproj` → `Sergin.Administration.UserAccess*`.
- Move: `tests/Sergin.UserAccess.UnitTests/` → `tests/Sergin.Administration.UserAccess.UnitTests/`.
- Modify: `.gitmodules`, `Sergin.MeterMinder.slnx`, host csproj (two references), `Program.cs`, `UserAccessNavigation.cs`, every host or test file referencing `Sergin.UserAccess` (about 20 more files), and any DeviceManagement file that does (check with `git grep -l "Sergin\.UserAccess" -- src/Modules/DeviceManagement`).
- Delete then regenerate: `Sergin.Administration.UserAccess.Infrastructure.Data/Migrations/`.

**Interfaces:**
- Consumes: `SerginNavGroups.Administration` (Task 1); `<scratch>/baseline/seed.sql` (Task 2).
- Produces: namespace root `Sergin.Administration.UserAccess`; `UserAccessModule` (name unchanged); `UserAccessNavigation.Group => SerginNavGroups.Administration`, items keep Order 200 and 210.

- [ ] **Step 1: Rename the GitHub repo.** Confirm with the user, then run `gh repo rename Sergin.Administration.UserAccess -R poursh/Sergin.UserAccess`.

- [ ] **Step 2: Move the submodule** the same way as Task 3 Step 2, to `src/Modules/Administration/UserAccess`, with URL `https://github.com/poursh/Sergin.Administration.UserAccess.git`.

- [ ] **Step 3: Rename projects, namespaces and the unit test project.** Same method as Task 3 Step 3. Runtime identity stays the same: `Name`, `Schema`, text categories, `@page` routes and permission strings do not change. Also update `InternalsVisibleTo` in `.Domain` to `Sergin.Administration.UserAccess.UnitTests`.

- [ ] **Step 4: Join the shared group.** `UserAccessNavigation.Group` returns `SerginNavGroups.Administration`. Delete `UserAccessTexts.MenuTitle` if nothing else reads it (check the permission group headings first: `PermissionGroupTitleTests` shows what they use).

- [ ] **Step 5: Rewire the host, `.slnx` and tests** as in Task 3 Steps 5–6, without route changes.

- [ ] **Step 6: Reset the migrations with seed rows.** Delete `Migrations/` and run `dotnet ef migrations add InitialCreate` against the UserAccess `Infrastructure.Data` project. Convert to file-scoped. At the end of `Up`, append the seed rows exactly as `seed.sql` holds them (ids, names, `is_inheritable`, `owner_scope_id`), excluding audit timestamps and row versions:
  - `ua.scopes`: the Root row `01920000-0000-7000-8000-0000000000b1`, path `019200000000700080000000000000b1`, through `migrationBuilder.Sql` (the path is `ltree`), with `created_at_utc = now()`, `created_by = '01920000-0000-7000-8000-00000000000f'`, `row_version = gen_random_uuid()`.
  - `ua.roles`: `administrator` (`…00a1`) and `viewer` (`…00a2`), owned by Root, with stamps as above.
  - `ua.role_permissions`: every row in `seed.sql`, one `InsertData` per row (CA1814), with the same stamp columns if the table carries them.

  Keep the role ids and the system actor as `private static readonly`/`const` fields at the top of the class, as the old migration did.

- [ ] **Step 7: Build and test both projects**

Run: `dotnet build Sergin.MeterMinder.slnx`, `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All`, `dotnet test tests/Sergin.Administration.UserAccess.UnitTests`
Expected: 0 warnings, all PASS, including `ExternalIdentityProvisioningTests` (viewer grants at Root) and `ScopedRbacAcceptanceTests`.

- [ ] **Step 8: Commit** in the submodule (`"Rename to Sergin.Administration.UserAccess; one InitialCreate with seed rows"`), then in the host (`"UserAccess becomes Sergin.Administration.UserAccess"`).

### Task 5: DeviceManagement reset and the schema diff

**Files:**
- Delete then regenerate: `src/Modules/DeviceManagement/Sergin.MeterMinder.DeviceManagement.Infrastructure.Data/Migrations/`

**Interfaces:**
- Consumes: `<scratch>/baseline/*.sql` (Task 2); the new `ua` and `st` `InitialCreate`s (Tasks 3–4).

- [ ] **Step 1: Regenerate.** Delete `Migrations/`, run `dotnet ef migrations add InitialCreate` against the DeviceManagement `Infrastructure.Data` project, and convert to file-scoped. Expected: no `migrationBuilder.Sql` in it.

- [ ] **Step 2: Dump the new chain.** Repeat Task 2 Steps 1–3 on the worktree as it is now, writing `<scratch>/after/schema.sql` and `<scratch>/after/seed.sql`.

- [ ] **Step 3: Diff**

Run: `git diff --no-index <scratch>/baseline/schema.sql <scratch>/after/schema.sql` (same for `seed.sql`)
Expected: only (a) `pf` → `st` for the schema, its objects and its history table, (b) seed timestamps and `row_version` values. Any other difference (a column, an index, a constraint, a filter, a missing row) is a bug: fix the model or the seed in the owning task, then re-run Steps 2–3.

- [ ] **Step 4: Run every test**

Run: `dotnet build Sergin.MeterMinder.slnx`, then both `dotnet test` commands from Task 4 Step 7.
Expected: 0 warnings, all PASS.

- [ ] **Step 5: Commit** `"DeviceManagement: one InitialCreate"`.

### Task 6: Compose smoke test

No code changes unless something fails.

- [ ] **Step 1:** `docker compose -f docker-compose/docker-compose.yml down -v`, then `up --build`. Expected: the app starts and migrates all three schemas.
- [ ] **Step 2:** Run both seed scripts from the root CLAUDE.md Commands section. Expected: `ON_ERROR_STOP` passes; `dev-seed-users.sql` finds the roles and the Root scope.
- [ ] **Step 3:** Sign in as `admin.root` and as `south.viewer` (password = username). Expected: `admin.root` sees one "Administration" group holding Users, Scopes and Languages. `south.viewer` sees only the entries their grants allow, with the group still shown if any entry is visible.

### Task 7: Documentation and tooling

**Files:**
- Modify: `.claude/CLAUDE.md` (root), `src/Modules/Administration/Settings/.claude/CLAUDE.md`, `src/Modules/Administration/UserAccess/.claude/CLAUDE.md`, `src/SharedKernel/.claude/CLAUDE.md` (nav group paragraph), `.claude/skills/add-feature/SKILL.md`, `.claude/skills/add-module/SKILL.md`, `README.md`, each submodule's `README.md`.

- [ ] **Step 1: Paths and names.** Replace every module path, project name and `/pf/`/`pf`/`platform.*` mention with the new one. Then confirm nothing is left (excluding `AllPlatform`, `platform-all` and product prose):

```bash
git grep -n -i "Sergin\.Platform\|src/Modules/Platform\|src/Modules/UserAccess\|Sergin\.UserAccess\b\|/pf/\|permission\.pf\." -- . ':!docs/superpowers'
```

  Expected: no output (old specs and plans under `docs/superpowers` keep their history).

- [ ] **Step 2: Migration references.** Wherever CLAUDE.md or a skill names a deleted migration as an example (`AddAuditColumns`, `AddSoftDeleteColumns`, `AddRowVersionColumns`, `AddDeviceScope`, `AddOutbox`, `AddDeviceIdUniqueIndex`, `AddUserNameUniqueIndex`, `AddLanguages`, `AddTranslations`), replace the file reference with the pattern in words: add the columns nullable, backfill, then set `NOT NULL`, in the same PR as the feature. Cite `de82bd7` as the last commit that holds the examples. Make the new `InitialCreate` files the example of the file-scoped shape.

- [ ] **Step 3: Nav paragraph.** Root CLAUDE.md's `NavGroup` description: groups are per `SerginNavGroup`, not per module; `SerginNavGroups.Administration` is shared by UserAccess and Settings.

- [ ] **Step 4: README.** Add the dev-database reset to the setup steps: after pulling this change, run `docker compose -f docker-compose/docker-compose.yml down -v` once.

- [ ] **Step 5: Graph.** Run `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`. Expected: both exit 0.

- [ ] **Step 6: Commit** in each submodule touched, then in the host: `"Docs: Administration modules and migration reset"`. Check `git status --ignored` for nothing unexpected.

### Task 8: Pull requests and merge

- [ ] **Step 1:** With the user's confirmation, push each submodule branch and open its PR: SharedKernel, Administration.Settings, Administration.UserAccess.
- [ ] **Step 2:** Merge those three. Then point each submodule in the host branch at its merged `main` commit, rebuild, and run all tests again.
- [ ] **Step 3:** With confirmation, push the host branch and open its PR, linking the three submodule PRs and the spec. After it merges, remove the worktree and the branch (memory: worktree routine).
