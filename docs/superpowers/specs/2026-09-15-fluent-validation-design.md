# FluentValidation on the write path

## Motivation

`ValidationPipelineBehavior` has been the second MediatR open behavior in `AddSerginCore` since
the pipeline existed. It takes an optional `IValidator<TRequest>` and turns every failure into an
`Error.Validation(code: PropertyName, description: ErrorMessage)`. Nothing ever registered a
validator and no `AbstractValidator<T>` existed, so the behavior was a no-op: every write command
reached its handler unchecked. `CreateUserCommand(new UserName(""))` inserted a blank user, and
`CreateDeviceCommand` with `Guid.Empty` failed on a Postgres foreign key.

Two latent defects in the presentation layer meant that adding a validator alone would have
produced a worse experience than none:

1. `SerginProblemFactory` rendered an `ErrorType.Validation` error's detail as
   `localizer[error.Code]`. The behavior puts the offending *property name* in `Code`, and
   `DefaultLocalizer` echoes its key, so a user would have read `UserName` as the whole error while
   FluentValidation's message was discarded.
2. Both fronts rendered only the first error — `ToApiResult` passed `r[0]` to
   `ApiProblemResults.Problem`, and every Blazor page called `Notify(result.FirstError)`.
   Validation is the one error type that routinely yields several at once.

## Scope

In:

- Validator discovery in `AddSerginCore`, by assembly scan, no new package.
- The rendering fix for validation errors and multi-error rendering on the API and the UI.
- One validator per write command across both modules — five in total.
- `MaxLength` constants on value objects as the single source for validator, form model and EF
  column width.
- Integration tests proving discovery, multi-error results, the not-persisted guarantee and the
  rendering rule.

Out, recorded so nobody reaches for them by accident:

- Manufacturer *existence* on `CreateDevice`. Still a Postgres FK violation. Converting it needs a
  validator with a repository dependency (the scoped lifetime chosen below allows it) or a handler
  check; a slice of its own.
- `Paggination`/`Term`/`Filtering`/`Sorting` guard themselves with `Guard.Against` at construction
  and throw `ArgumentException`. Not ErrorOr-shaped, not touched.
- Localising FluentValidation messages through Sergin's `ILocalizer`. FluentValidation's own
  `LanguageManager` localises the built-in messages from `CultureInfo.CurrentUICulture`; a custom
  `.WithMessage` string would be English until `DefaultLocalizer` grows real resources.
- Moving Blazor forms from `DataAnnotationsValidator` to FluentValidation. The forms keep
  DataAnnotations client-side; the pipeline validator is the server-side backstop.
  *Superseded 2026-09-17: the forms now run the pipeline's validators field by field through
  `ISerginFormValidator` — see `2026-09-17-mudform-client-validation-design.md`.*

## Decisions

Settled with the user before implementation:

| Question | Decision |
|---|---|
| Which commands | All five write commands, `ProvisionExternalUser` included |
| Blazor forms | Keep DataAnnotations; the pipeline is the backstop |
| Multiple errors | Surface all of them on both fronts |
| Discovery | Hand-rolled scan, generalising the translator scanner; no `FluentValidation.DependencyInjectionExtensions` |

## Design

### Discovery — `Sergin.SharedKernel.Hosts/SerginCoreExtensions.cs`

The private `AddIntegrationEventTranslators(services, assembly)` became
`AddClosedGenericImplementations(services, assembly, openInterface, lifetime)`: every non-abstract
class in the assembly is registered against each closed form of `openInterface` it implements.
`AddSerginCore` calls it twice per `localModules` entry:

- `IIntegrationEventTranslator<>`, transient — unchanged behaviour.
- `IValidator<>`, **scoped** — FluentValidation's own default lifetime, and it lets a validator
  take a scoped dependency (a query repository for a uniqueness rule) without a lifetime mismatch.

`ApplicationAssembly` only. A `remoteModules` entry ships no `.Application` project, so a gateway
host never validates a remote request itself; the remote server host runs the real pipeline,
validators included, exactly as it would for a local call.

`ValidationPipelineBehavior` is unchanged. Its optional constructor parameter
(`IValidator<TRequest>? validator = null`) resolves to null for a request with no validator, which
skips straight to the handler.

### Rendering

`SerginProblemFactory` treats `ErrorType.Validation` as the one type whose `Detail` is
`error.Description` rather than `localizer[error.Code]`. The description is FluentValidation's
message, human text already localised by its `LanguageManager`; the code is the property name, which
is what the API groups a `ValidationProblem` by. The title is the fixed
`SerginProblemFactory.ValidationTitleKey` (`General.Validation.title`), mirroring ErrorOr's own
default validation code in the `<code>.title` shape every other type uses. Status stays 400.

`ApiProblemResults` gains `Problem(IReadOnlyList<Error>, ILocalizer)`. When every error is a
validation error it answers `Results.ValidationProblem` with the errors grouped by code — the
standard `HttpValidationProblemDetails` shape, one entry per property, every message under it. Any
other mix falls back to the first error as before. `ResultExtensions.ToApiResult` hands the whole
list over instead of `r[0]`.

`IUiErrorPresenter` gains `Notify(IReadOnlyList<Error>)`; `MudUiErrorPresenter` raises one snackbar
per error (MudBlazor stacks them, its default cap of five visible is enough for a form). `Present`
is unchanged — a detail page only ever gets one error. Create pages call `Notify(result.Errors)`;
list and detail pages and mutate actions keep `FirstError`, since those sends can only yield one.

### Validators

One `internal sealed class <Feature>CommandValidator : AbstractValidator<<Feature>Command>` per
write command, in the module's `.Application` project, in the feature folder next to the handler.
`using FluentValidation;` per file rather than in `GlobalUsings.cs`, since only validators need it.
Commands carry domain value objects, so rules target `.Value` and call
`OverridePropertyName(nameof(Command.Property))` to restore the command property name — the wrapper
record is never null (every caller constructs it), only its `Value` can be, which `NotEmpty`
catches.

| Command | Rules |
|---|---|
| `CreateUserCommand` | `UserName.Value`: NotEmpty, MaximumLength(`UserName.MaxLength` = 100) |
| `DeactivateUserCommand` | `Id`: NotEmpty (rejects `Guid.Empty`) |
| `ProvisionExternalUserCommand` | `ExternalId.Value`: NotEmpty, MaximumLength(200, the column). `UserName.Value`: NotEmpty only — the column is unbounded and Keycloak usernames can be emails. `Email.Value` when present: EmailAddress, MaximumLength(320, the column). `FirstName`/`LastName`: MaximumLength(200, the column), no NotEmpty — the provider may omit them. **Deliberately lenient**: it runs inside the OIDC callback, where a failure fails sign-in. It refuses only what the database would refuse anyway, so a bad profile now fails with FluentValidation's message (through `ExternalIdentityResolver`'s `InvalidOperationException`) instead of a Postgres error. |
| `CreateDeviceCommand` | `DeviceId.Value`: NotEmpty, MaximumLength(`DeviceId.MaxLength` = 100). `ManufacturerId.Value`: NotEmpty. |
| `CreateManufacturerCommand` | `Name.Value`: NotEmpty, MaximumLength(`ManufacturerName.MaxLength` = 200). `Address.Value` when present: NotEmpty, MaximumLength(`ManufacturerAddress.MaxLength` = 500). |

### `MaxLength` constants

A length limit lives once, as `public const int MaxLength` on the value object in `.Domain`
(`UserName`, `ExternalUserId`, `EmailAddress`, `DeviceId`, `ManufacturerName`,
`ManufacturerAddress`; `User.NameMaxLength` for the two plain-string name properties). The
validator, the Blazor form model's `[StringLength]` and `UserEntityTypeConfiguration`'s
`HasMaxLength` all read it. The column widths are unchanged, so there is no migration. Values were
taken from what already existed — the form models said 100, the EF columns said 200 and 320 — and
the manufacturer limits are new, since those columns are unbounded text.

One wrinkle: inside a form model attribute the type must be qualified
(`[StringLength(Domain.Users.UserName.MaxLength, …)]`), because the model's own `UserName` property
shadows the type name there.

## Testing

`tests/Sergin.MeterMinder.IntegrationTests.All/Validation/`, on the shared fixture, dispatching
through `ISerginDispatcher` from a scope exactly as a Blazor page does:

- `CommandValidationTests` — an over-long username is refused with `Code == "UserName"` and does
  not appear in the list afterwards; an empty username is refused with its message intact; an empty
  device id and an empty manufacturer are reported together, each naming the command property, not
  `DeviceId.Value`; `IValidator<CreateUserCommand>` and `IValidator<CreateDeviceCommand>` both
  resolve, proving the scan covers both modules.
- `ValidationProblemRenderingTests` — `SerginProblemFactory` renders the message as detail under the
  validation title; `ApiProblemResults` answers a `ProblemHttpResult` carrying
  `HttpValidationProblemDetails` with two messages under `UserName` and one under `ManufacturerId`;
  a mixed list falls back to the first error's status.

There is no permission-before-validation test because no write command carries
`[RequiredPermissions]` today. `CreateAndGetUserTests` doubles as the valid-command case, and
`ExternalIdentityProvisioningTests` proves a normal profile passes the lenient provisioning
validator.

## Documentation

Root `.claude/CLAUDE.md` (pipeline bullet, the "Validation" convention, the create-submit
rendering shape, the FK note), `.claude/skills/add-feature/SKILL.md` (step 3b, the validator file;
create submit notifies every error; the form model reads the constant), the three module/kernel
CLAUDE.md files, and this spec with its plan.

## Decisions recorded

- **Scan, not `AddValidatorsFromAssembly`.** The scanner already existed for translators;
  generalising it costs one parameter and registers `internal sealed` validators without an
  `includeInternalTypes` flag or a new package in two `Directory.Packages.props` files.
- **Scoped, not transient or singleton.** Transient would match the translators but rebuild the rule
  tree per request; singleton would forbid a scoped dependency later. Scoped is also
  FluentValidation's own default.
- **Description as detail only for `Validation`.** Every other error type keeps
  `localizer[error.Code]`. No module hand-builds `Error.Validation(...)` today, so the change has
  exactly one producer.
- **`ValidationProblem` only when every error is a validation error.** A mixed list means a handler
  returned something other than validation, and the first error's status is the right answer.
- **`ProvisionExternalUser` is lenient on purpose.** A strict rule there is a way to lock users out
  of the product from a change in Keycloak.
