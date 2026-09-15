# Repository-backed existence and uniqueness checks

## Motivation

The FluentValidation work (`2026-09-15-fluent-validation-design.md`) gave every write command a
validator, and left one gap on record: manufacturer *existence* on `CreateDevice`.
`CreateDeviceCommandValidator` refused `Guid.Empty`, but any other unknown `ManufacturerId` went
through to the handler, whose `SaveChangesAsync` then failed on the Postgres foreign key. The caller
got a `DbUpdateException` out of MediatR — a 500 from an API, an unhandled circuit exception in
Blazor — for what is a plain input error, and the only cross-aggregate reference in the codebase had
no worked example of doing better.

Two more gaps sat next to it and were worse, because they were silent:

1. Nothing stopped two devices from sharing a `DeviceId`, or two users a `UserName`. Neither column
   had a unique index, and no command checked. Duplicates would simply accumulate.
2. `DeviceRepository.GetByDeviceId` and `UserRepository.GetByUserName` were written as
   `SingleOrDefaultAsync`, which throws on the second match. Every duplicate that got in was a
   future `InvalidOperationException` waiting on the first lookup of that value —
   `ProvisionExternalUserCommandHandler` runs `GetByUserName` inside the OIDC callback, so the
   failure would have surfaced as a broken sign-in.

The three belong together: the fix for the first is a validator rule over `ExistsAsync`; the fix for
the second is a validator rule over "is this value taken", plus the index that makes the answer
hold under concurrency; and the index is what retires the third.

## Scope

In:

- `IRepository<TAggregateRoot, TId>.ExistsAsync(TId)` — a `SELECT EXISTS`, loading nothing.
- `IUniqueKeyRepository<TKey>` — a repository interface declares a value object as an alternate
  key by implementing it, once per key.
- `EfRepository<TAggregateRoot, TId>` — the abstract EF implementation module repositories derive
  from, so that the next `IRepository` member is a SharedKernel-only change.
- `MustExistIn` and `MustBeUniqueIn` — FluentValidation rule-builder extensions over the two.
- The two rules applied to `CreateDevice` (manufacturer existence, device-id uniqueness) and the
  uniqueness rule to `CreateUser`.
- Unique indexes on `dm.device.device_id` and `ua.users.user_name`, each in the same PR as the
  interface that declares the key.
- Integration tests for both rules, the not-loading guarantee of `ExistsAsync`, the index as the
  fallback, and the deliberate absence of a rule on provisioning.

Out, recorded so nobody reaches for them by accident:

- **Translating Postgres `SqlState`s into `ErrorOr`.** A race between the validator's query and
  `SaveChangesAsync` — the manufacturer deleted in between, two concurrent creates with the same
  id — still surfaces as `DbUpdateException` over 23503 / 23505. Turning that into a validation
  error is a cross-cutting concern (an interceptor or a pipeline behavior over every handler, and a
  mapping table from constraint name to property name), not something to bolt onto one handler
  with a try/catch. Its own slice.
- **Optimistic concurrency.** `RowVersion` exists in SharedKernel and no aggregate carries it. Not
  touched.
- **A uniqueness rule on `ProvisionExternalUser`.** Deliberately absent; see Decisions.
- **`ManufacturerName` as an alternate key.** Two manufacturers may share a name today. Declaring
  it unique is a product decision, and it would need its own index and data check.
- **Roles.** `ua.roles.name` is already unique and no command creates roles, so `IRoleRepository`
  gains no key capability; `RoleRepository` only moves onto the base class.

## Decisions

Settled in the discussion that produced this, before implementation:

| Question | Decision |
|---|---|
| A `DeviceService` holding the checks | Rejected. The command handler already *is* the application service for its use case; a second home for the same use case's logic gives no rule for what goes where. |
| Validator or handler | Validator. The checks are database-backed preconditions — neither shape validation nor aggregate behaviour — and putting them in the validator means both failures report together, `Code` groups them per property, and the rendering path built for validation (`ValidationProblem` on the API, one snackbar each in Blazor) applies as is. A handler could only return one bare `Error.NotFound()` with generic text. |
| `ExistsAsync` or `GetAsync(...) is not null` | `ExistsAsync`. `GetAsync` loads and tracks the aggregate for a yes/no question; `AnyAsync` plans as `SELECT EXISTS` and leaves the change tracker empty. |
| `IUniqueKeyRepository<TKey>` or `<TAggregate, TKey>` | `TKey` only. A value-object type is already specific to one aggregate, and a repository implementing the interface twice (two keys) breaks C# type inference in `MustBeUniqueIn` the moment a second type parameter exists. |
| Base class or interface default methods | Base class. Default interface members can't reach the `DbContext`; a base class can, and it makes the next `IRepository` member a SharedKernel-only change — this one forced a UserAccess PR because four repositories each implemented the interface by hand. |
| Advisory check or database guarantee | Both, always together. The rule is advisory and the index / FK is the guarantee; a PR declaring `IUniqueKeyRepository<TKey>` must add the index. |
| The race | Out of scope. Still `DbUpdateException`; documented, tested, and left for a SqlState-translation slice. |
| Provisioning | Exempt. Find-or-create inside the OIDC callback; a uniqueness rule there refuses returning users. |

## Design

### `Sergin.SharedKernel.Domain` — the contracts

`IRepository<TAggregateRoot, TId>` gains one member:

```csharp
Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);
```

`IUniqueKeyRepository<in TKey>` (`where TKey : notnull`) is new:

```csharp
Task<bool> IsTakenAsync(TKey key, CancellationToken cancellationToken = default);
```

A repository interface implements it once per alternate key —
`IDeviceRepository : IRepository<Device, DeviceIntenralId>, IUniqueKeyRepository<DeviceId>`,
`IUserRepository : IRepository<User, UserInternalId>, IUniqueKeyRepository<UserName>`. The
`in` variance is free and harmless. The XML doc on the interface carries the two rules that matter:
the key alone is a type parameter, and declaring the key is a promise that the same PR adds the index.

### `Sergin.SharedKernel.Infrastructure.Data.EFCore` — the base class

```csharp
public abstract class EfRepository<TAggregateRoot, TId>(IDbContext dbContext) : IRepository<TAggregateRoot, TId>
{
    protected DbSet<TAggregateRoot> Set => dbContext.Set<TAggregateRoot>();
    public ValueTask<TAggregateRoot?> GetAsync(TId id, ct) => Set.FindAsync([id], ct);
    public Task<bool> ExistsAsync(TId id, ct) => Set.AnyAsync(entity => entity.Id.Equals(id), ct);
    protected Task<bool> AnyAsync(Expression<Func<TAggregateRoot, bool>> predicate, ct) => Set.AnyAsync(predicate, ct);
    public void Insert(TAggregateRoot entity) => Set.Add(entity);
    public void Remove(TAggregateRoot entity) => Set.Remove(entity);
}
```

Three things worth knowing about it. `GetAsync` passes `[id]` to `FindAsync` — the module
repositories used to pass `[id, cancellationToken]`, which EF tolerated but which is not a key
value. `ExistsAsync` compares through `Equals` because `TId` is only constrained to `notnull`;
EF translates it through the id's value converter to `WHERE id = @p`, wrapped in `EXISTS`, and the
integration test proves it against Postgres. `AnyAsync` is `protected` because it exists for one
caller: a derived class's `IsTakenAsync`, which is then one line —
`AnyAsync(d => d.DeviceId == key, cancellationToken)`.

All four module repositories derive from it and shrink to their named lookups:
`DeviceRepository` (`GetByDeviceId`, `IsTakenAsync`), `UserRepository` (`GetByUserName`,
`GetByExternalId`, `IsTakenAsync`), `RoleRepository` (`GetByName`), and `ManufacturerRepository`,
which is now a one-line declaration with no body.

### `Sergin.SharedKernel.Application` — the rules

`Validations/RepositoryRuleBuilderExtensions.cs`:

```csharp
public static IRuleBuilderOptions<T, TId> MustExistIn<T, TAggregateRoot, TId>(
    this IRuleBuilder<T, TId> ruleBuilder, IRepository<TAggregateRoot, TId> repository)
    => ruleBuilder.MustAsync(repository.ExistsAsync)
                  .WithMessage($"'{{PropertyName}}' must refer to an existing {typeof(TAggregateRoot).Name}.");

public static IRuleBuilderOptions<T, TKey> MustBeUniqueIn<T, TKey>(
    this IRuleBuilder<T, TKey> ruleBuilder, IUniqueKeyRepository<TKey> repository)
    => ruleBuilder.MustAsync(async (key, ct) => !await repository.IsTakenAsync(key, ct))
                  .WithMessage("'{PropertyName}' is already in use.");
```

Both are `MustAsync`, which is fine because `ValidationPipelineBehavior` already calls
`ValidateAsync`. The messages are `.WithMessage` strings, so they are English until
`DefaultLocalizer` grows resources — the same state the fluent-validation spec recorded for any
custom message. `{PropertyName}` is FluentValidation's display name, so `DeviceId` renders as
`'Device Id'`.

Two conventions for the call site, both visible in `CreateDeviceCommandValidator`:

- The rule targets the **wrapper**, not `.Value`: `RuleFor(x => x.ManufacturerId).MustExistIn(manufacturers)`.
  The property name is then already the command property, so there is no `OverridePropertyName`,
  and `Error.Code` groups with the shape rule's errors on the same property.
- It is guarded with `.When(...)` on its own shape rule —
  `.When(x => x.ManufacturerId.Value != Guid.Empty)`,
  `.When(x => !string.IsNullOrWhiteSpace(x.DeviceId.Value))` — so no query runs for a value the
  shape rule already refused, and an empty id does not also report "already in use".

The validator takes its repositories through the constructor. That works without any lifetime
change: the fluent-validation spec chose scoped for validators precisely so they could take a
scoped dependency later, the repositories are transient, and the `DbContext` is scoped, so the
validator, its repositories and the handler all share the request scope `ValidationPipelineBehavior`
runs in. `AddSerginCore`'s scan registers the validator class as it is; DI fills the constructor.

### Modules

| Where | Change |
|---|---|
| `IDeviceRepository` | `+ IUniqueKeyRepository<DeviceId>` |
| `DeviceRepository` | derives from `EfRepository`; keeps `GetByDeviceId`; `IsTakenAsync => AnyAsync(d => d.DeviceId == key, ct)` |
| `ManufacturerRepository` | derives from `EfRepository`; no body |
| `CreateDeviceCommandValidator` | `(IDeviceRepository devices, IManufacturerRepository manufacturers)`; `MustBeUniqueIn(devices)` on `DeviceId`, `MustExistIn(manufacturers)` on `ManufacturerId`, each behind `.When` |
| `DeviceEntityTypeConfiguration` | `HasIndex(d => d.DeviceId).IsUnique()` |
| migration `20260915140708_AddDeviceIdUniqueIndex` | `ix_device_device_id` on `dm.device.device_id` |
| `IUserRepository` | `+ IUniqueKeyRepository<UserName>` |
| `UserRepository`, `RoleRepository` | derive from `EfRepository`; `UserRepository.IsTakenAsync => AnyAsync(u => u.UserName == key, ct)` |
| `CreateUserCommandValidator` | `(IUserRepository users)`; `MustBeUniqueIn(users)` on `UserName` behind `.When` |
| `UserEntityTypeConfiguration` | `HasIndex(u => u.UserName).IsUnique()` |
| migration `20260915140555_AddUserNameUniqueIndex` | `ix_users_user_name` on `ua.users.user_name` |

`ProvisionExternalUserCommandValidator` is unchanged. The handler looks the user up by
`ExternalId`, then by `UserName`, and links the provider subject to a pre-existing local user
rather than creating a second account for the same person. The username is therefore "taken" by
the very row the handler is about to find; a uniqueness rule there would refuse every returning
user whose account predates external sign-in.

### What the caller now sees

`CreateDeviceCommand(new DeviceId("x"), new ManufacturerId(<unknown>))` answers one validation
error, `Code == "ManufacturerId"`, `Description == "'Manufacturer Id' must refer to an existing Manufacturer."`,
and nothing is written — the handler never ran. The same command with a taken device id answers
`Code == "DeviceId"`, `"'Device Id' is already in use."`. Both errors, when both apply, come back in
one result and render as one `ValidationProblem` on the API or one snackbar each in Blazor. The
`CreateDevicePage` form cannot repeat these rules client-side (they need the database), so unlike
the shape rules they are reachable from the UI.

## Testing

`tests/Sergin.MeterMinder.IntegrationTests.All/Validation/RepositoryRuleTests.cs`, on the shared
fixture, dispatching through `ISerginDispatcher` from a scope exactly as a Blazor page does:

- `CreateDevice_UnknownManufacturer_IsRefusedNotThrown` — a well-formed random manufacturer id
  yields exactly one validation error on `ManufacturerId` with the existence message, and the
  device is absent from the list afterwards. Before the rule this send threw `DbUpdateException`.
- `CreateDevice_DuplicateDeviceId_IsRefused` — the second create of the same `DeviceId` yields one
  validation error on `DeviceId` with the in-use message.
- `CreateDevice_EmptyDeviceIdAndUnknownManufacturer_ReportsShapeAndExistenceNotUniqueness` — an
  empty device id reports its shape error and no in-use error, while the manufacturer rule, whose
  shape rule passed, still reports. This pins the errors a caller sees; it cannot observe that the
  uniqueness query was skipped, since `IsTakenAsync("")` answers false either way.
- `CreateUser_DuplicateUserName_IsRefused` — the UserAccess mirror.
- `ProvisionExternalUser_ExistingUserName_IsNotRefusedAndLinksTheUser` — a user created by
  `CreateUserCommand` is then provisioned with the same username and a fresh subject; the send
  succeeds and returns the existing user's id. The test that fails first if a uniqueness rule is
  ever added to provisioning.
- `ExistsAsync_AnswersWithoutTracking` — false for a random id, true after a create, and the
  scope's `ChangeTracker.Entries()` is empty after the call. Proves the `Equals` translation
  against real Postgres, not just that it compiles.
- `UniqueIndex_IsTheGuaranteeWhenTheValidatorIsBypassed` — a device inserted straight through
  `IDeviceRepository` + `IDeviceManagementUnitOfWork`, no pipeline, with a taken id: `SaveChangesAsync`
  throws `DbUpdateException` whose inner `PostgresException` carries `SqlState` 23505. This is the
  race, made deterministic.

The existing `CommandValidationTests` and `ExternalIdentityProvisioningTests` are unchanged and
still pass — the second matters, since provisioning now runs alongside a unique index on the
column it upserts by name.

## Documentation

Root `.claude/CLAUDE.md` (the test inventory, the migrations gotcha, the `.Domain` and
`.Infrastructure` layering bullets, the former "No FK-existence check" gotcha, the "Validation"
convention, the "Database schema" bullet), `.claude/skills/add-feature/SKILL.md` (step 3b, step 7,
the migration step), `src/Modules/DeviceManagement/CLAUDE.md` (Validators, Repositories), the two
submodule CLAUDE.md files in their own PRs, and this spec with its plan.

## Decisions recorded

- **The handler is the application service.** A `DeviceService` was proposed and rejected: a
  command handler already owns one use case end to end, and a second class for the same use case
  is a second place to look with no rule for which logic goes where.
- **Database preconditions live in the validator.** They are not shape checks and not aggregate
  behaviour; they are questions only the database can answer, asked before the handler runs so the
  answers report together with everything else that is wrong with the request.
- **`ExistsAsync` is a separate member, not a helper over `GetAsync`.** The difference is a loaded,
  tracked aggregate versus a boolean; on a hot create path that is the difference that matters.
- **`TKey` alone.** Type inference is the reason, and the value-object-per-aggregate convention is
  what makes it safe.
- **A base class now, before there are eight repositories.** Four hand-written implementations was
  already one too many for a one-member interface change.
- **Advisory plus index, never advisory alone.** The rule is the friendly path; the index is the
  truth. A `IUniqueKeyRepository<TKey>` without an index is a lie the first concurrent request
  exposes.
- **The race is documented, not hidden.** A test proves the index throws when the validator is
  bypassed, so nobody mistakes the rule for the guarantee.
- **Provisioning stays lenient.** Same reasoning as the fluent-validation spec, one step further:
  the rule that protects `CreateUser` is the rule that would lock returning users out.
