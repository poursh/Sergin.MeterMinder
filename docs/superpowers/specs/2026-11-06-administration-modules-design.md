# Administration modules: rename Platform and UserAccess, reset migrations

Date: 2026-11-06. Status: proposed.

## Problem

"Platform" means three things in this codebase. It is the product (the Sergin platform), SharedKernel's
reserved permission vocabulary (`PermissionDefinition.AllPlatform`, `permission.sys.platform-all`, "platform
behaviour" for aggregate features), and the module that holds languages and translations. The module's drawer
group is titled "Platform", which tells an administrator nothing about what is inside it.

UserAccess and Platform are both administration: one administers who may do what, the other how the system
is set up. Today they read as two unrelated top-level modules, and the drawer shows them as two unrelated
groups.

There is no production environment yet. Every database is a dev or test database, so stored keys, schema
names and the migration history can all change now at no data cost. That stops being true at the first
deployment.

## Decision

1. **`Sergin.Administration` is a naming umbrella, not a module.** It has no project, schema, `DbContext` or
   repo of its own. Two modules live under it:
   - `Sergin.Administration.UserAccess`: today's UserAccess, renamed.
   - `Sergin.Administration.Settings`: today's Platform, renamed. Localization is a feature folder inside it
     (`Localization/Languages/...`, `Localization/Translations/...`), which the Platform CLAUDE.md already allows.
     It is not a module of its own: one module per settings feature would multiply schemas, contexts and repos.
2. **No merge.** The two modules keep separate schemas, `DbContext`s, migrations and repos. UserAccess is large,
   SharedKernel depends on it through seams (`IExternalIdentityResolver`, `IDevUserSeeder`), and a merge would
   gain nothing.
3. **Settings changes its runtime identity; UserAccess keeps its own.**

   | | UserAccess (unchanged) | Settings (was Platform) |
   |---|---|---|
   | Module `Name` | `UserAccess` | `Settings` (was `Platform`) |
   | Schema | `ua` | `st` (was `pf`) |
   | Routes | `/ua/...` | `/st/...` (was `/pf/...`) |
   | Text keys | `user-access.*` | `settings.*` (was `platform.*`) |
   | Permissions | `permission.ua.*` | `permission.st.*` (was `permission.pf.*`) |

   For UserAccess, only projects, namespaces, the repo and the disk path change, so nothing stored and no URL
   moves.
4. **Nested disk layout.** The submodules move to `src/Modules/Administration/UserAccess` and
   `src/Modules/Administration/Settings`. The `.slnx` groups them in an `Administration` solution folder.
   DeviceManagement stays at `src/Modules/DeviceManagement`.
5. **One shared drawer group.** Both modules show under one "Administration" group in the drawer and the
   breadcrumb strip (`Home > Administration > Users`). This needs a SharedKernel change, see below.
6. **Migration reset.** Every module (`dm`, `ua`, `st`) drops its migration history and gets one regenerated
   `InitialCreate`. 18 migrations become 3.
7. **Later work goes in siblings.** Operational monitors, planned for Platform, go in a future
   `Sergin.Administration.Monitoring` module, not in Settings.

## Naming

| Today | After |
|---|---|
| GitHub `poursh/Sergin.UserAccess` | `poursh/Sergin.Administration.UserAccess` |
| GitHub `poursh/Sergin.Platform` | `poursh/Sergin.Administration.Settings` |
| `src/Modules/UserAccess/` | `src/Modules/Administration/UserAccess/` |
| `src/Modules/Platform/` | `src/Modules/Administration/Settings/` |
| `Sergin.UserAccess[.Layer]` | `Sergin.Administration.UserAccess[.Layer]` |
| `Sergin.Platform[.Layer]` | `Sergin.Administration.Settings[.Layer]` |
| `UserAccessModule` | unchanged |
| `PlatformModule`, `PlatformDbContext`, `PlatformTexts`, `PlatformNavigation`, `PlatformTranslationStore`, `Platform*AssemblyReference` | `Settings*` equivalents |
| `tests/Sergin.UserAccess.UnitTests` | `tests/Sergin.Administration.UserAccess.UnitTests` |

GitHub redirects a renamed repo's old URL, but `.gitmodules` is updated to the new URLs anyway.

## SharedKernel: a drawer group shared by several modules

Today `ISerginWebUiModule.NavGroup` is one `SerginNavGroup(Title, Icon, Order)` per module, and
`SerginUiModuleCatalog` builds one `SerginNavSection` per module. `SerginNavGroup` is a record, so two modules
that return an *equal* group are already equal by value. What is missing:

- **Where the shared title is declared.** The text catalog refuses a key outside its module, so neither module
  can declare `administration.*` for the other. SharedKernel declares the group once, as a well-known
  definition (`SerginNavGroups.Administration`: title `common.nav-groups.administration` "Administration", an
  icon, Order 200). Both modules return it from `NavGroup`. "Administration" is generic to any Sergin host, so
  it fits SharedKernel; a host that does not want it simply has no module returning it.
- **Merging by group.** `SerginUiModuleCatalog` groups sections by `SerginNavGroup` (value equality) instead of
  by module. The items of every module returning the group share one section, ordered by `SerginNavItem.Order`
  across modules. UserAccess takes items 10–90 and Settings 100 onward, so the order is stable.
- **Breadcrumbs.** `GroupOf(href)` already returns a group, not a module, so it works unchanged once sections
  merge.
- **Expansion state.** `SerginNavMenu` already keys expansion by `SerginNavGroup`, so a merged group opens and
  stays open as one.
- **Guard.** The route-prefix guard walks `catalog.Modules`, not sections, so each module still has its own
  `/{schema}/` rule.
- **Tests.** Extend `Shell/` nav tests: two modules with one group render one `MudNavGroup` with both modules'
  entries in order; a breadcrumb names the shared group; a user who can see entries of only one module still
  sees the group.

## Migration reset

### What regenerating keeps

The EF model already declares the `ltree` extension (`HasPostgresExtension`), the partial unique indexes, the
soft-delete CHECKs, the query filters, the outbox tables and every index name, so these regenerate correctly.

### What must be carried over by hand

UserAccess seed rows exist only in migrations, not in the model:

- Roles `administrator` and `viewer` (fixed ids) and their `role_permissions`, from
  `AddRolesAndExternalIdentity`: the administrator gets `permission.sys.platform-all`, and the viewer gets the
  `ViewerPermissions` list.
- The Root scope `01920000-0000-7000-8000-0000000000b1` with path `019200000000700080000000000000b1`, from
  `AddScopedRbac`, plus the roles' owner scope, `is_inheritable`, audit stamps and row versions set to match.

Append these to the new `ua` `InitialCreate`, with one `InsertData` per row (CA1814 refuses the scaffolder's
rectangular array, as the old migration notes) and a `Sql` call for the ltree root. Seed values must match
today's final state, not the first migration's.

### What is dropped

Every data step that only made sense for an existing database: `DELETE FROM dm.device` in `AddDeviceModels`,
the audit, row-version and scope backfills, and `AddScopedRbac`'s role and assignment reshuffle.

### Procedure, per module

1. On a fresh postgres:17, apply today's chain. Run `pg_dump --schema-only` for the schema and a data dump of
   `ua.roles`, `ua.role_permissions`, `ua.scopes`. This is the baseline.
2. Delete the `Migrations/` folder and run `dotnet ef migrations add InitialCreate` (see CLAUDE.md for
   `--project`/`--startup-project`). Convert the scaffolded file to a file-scoped namespace (IDE0161).
3. For `ua`, append the seed rows.
4. On another fresh database, apply the new chain, dump it the same way, and diff it against the baseline.
   Accepted differences: the `__EFMigrationsHistory` rows, and for `st` the schema name. Anything else is a
   bug in the regenerated model or the seed.

### Consequences

- Every existing dev database must be dropped (`docker compose -f docker-compose/docker-compose.yml down -v`).
  Otherwise the history table does not match and `InitialCreate` fails on tables that already exist.
  Integration tests use a fresh Testcontainers database and are unaffected.
- CLAUDE.md names deleted migrations as reference examples (`AddAuditColumns`, `AddSoftDeleteColumns`,
  `AddRowVersionColumns`, `AddDeviceScope`, `AddOutbox`, `AddDeviceIdUniqueIndex`, `AddUserNameUniqueIndex`).
  Once production exists, their pattern matters again for every feature turned on for an existing table: add
  the columns nullable, backfill, then set `NOT NULL`. Describe that pattern in words in CLAUDE.md and the
  `/add-feature` skill, and cite the commit hash that last held the examples. The new `InitialCreate` files
  become the example of the converted file-scoped shape.

## Rollout

Three repos change together, so the host never points at a half-renamed submodule.

1. **SharedKernel PR:** shared nav group (`SerginNavGroups.Administration`, catalog merge, tests).
2. **Settings repo** (renamed on GitHub first): rename projects and namespaces, `Name`/schema/routes/text
   keys/permissions, nav group, SharedKernel paths `..\..\..\` → `..\..\..\..\`, migration reset, its CLAUDE.md.
3. **UserAccess repo** (renamed on GitHub first): rename projects and namespaces, nav group, SharedKernel
   paths, migration reset with seed rows, its CLAUDE.md.
4. **Host PR:**
   - Move the submodules (`git mv src/Modules/UserAccess src/Modules/Administration/UserAccess`, same for
     Platform → Settings) and update `.gitmodules` URLs.
   - Update host csproj references (four), `Program.cs`, `Sergin.MeterMinder.slnx` (`Administration` folder).
   - Rename the unit test project, and update namespaces and `/pf/` routes in the integration tests (about 35
     files reference the namespaces, 17 the routes).
   - Reset the DeviceManagement migrations.
   - Update root CLAUDE.md, the `/add-feature` and `/add-module` skills (they cite
     `src/Modules/UserAccess/**/Users/**` as the reference slice), README, and the memory notes if they name
     paths.
   - Rebuild graphify (`graphify update .`, then the repair script).

Merge the SharedKernel, Settings and UserAccess PRs first, then the host PR with all three pointers bumped.

## Verification

- `dotnet build Sergin.MeterMinder.slnx` passes with warnings as errors.
- Both test projects pass, including `PseudoLocaleSweepTests` with the `/st/` routes and the new nav tests.
- The schema-dump diff from the migration procedure shows only the accepted differences.
- `docker compose ... up --build` on an empty volume starts, both seed scripts run, and each mock user signs
  in to their slice of the tree. The drawer shows one Administration group holding Users, Scopes and Languages.
- `git status --ignored` shows nothing unexpected after the moves (the anchored `/publish/` rule).

## Out of scope and open questions

- **`permission.sys.platform-all` and `PermissionDefinition.AllPlatform`** use "platform" in its product sense,
  which is correct, so they stay. Renaming them would be a SharedKernel decision of its own.
- **Other hosts.** If any host other than Sergin.MeterMinder mounts `Sergin.UserAccess`, it must adopt the
  nested path and the new repo name at the same time. This is not checked yet.
- **Restore of the old history.** Not needed: no deployed database exists. The first real deployment starts
  from these three `InitialCreate`s.
