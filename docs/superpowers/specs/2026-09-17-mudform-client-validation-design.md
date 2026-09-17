# MudForm with client-side rules from the pipeline validators

## Motivation

Every Blazor create page — `CreateDevicePage`, `CreateManufacturerPage` and `AddDeviceModelPage`
in DeviceManagement, `CreateUserPage` in UserAccess — is an `EditForm` with a
`DataAnnotationsValidator` and MudBlazor inputs bound through `For=`. Its rules therefore exist
twice: `[Required]`/`[StringLength]` on the form model for the browser, and the real
`<Feature>CommandValidator` (FluentValidation, in the module's `.Application`) for the pipeline.
The browser copy is shape-only. A taken user name or device id, or a device model that does not
exist, is reported only after submit, as a snackbar — the pipeline validator's message, arriving
one round trip late.

The 2026-09-15 fluent-validation spec kept it that way on purpose ("the forms keep DataAnnotations
client-side; the pipeline validator is the server-side backstop"). This spec supersedes that one
line: the pipeline validator is still the backstop, but it now also serves the form, field by
field, as the user types.

## Scope

In:

- The four create pages move from `EditForm` to `MudForm`: validation as the user types, the
  Create button disabled while the form is invalid, one idiom that future update pages copy.
- A new SharedKernel seam, `ISerginFormValidator`, that runs the pipeline's own
  `IValidator<TCommand>` for one property in a fresh DI scope, and a `RulesFor` adapter shaped
  for MudForm's `Validation` parameter.
- The four form models lose their DataAnnotations.
- Integration tests for the seam; docs and the `/add-feature` skill updated to the new shape.

Out:

- Any update/edit feature slice. There is none in the repo; this spec fixes the form convention
  an update page will use, nothing more.
- A shared `SerginForm` wrapper component. Four pages of about ten lines each do not earn one.
- Client-side validators of their own for the form models. The whole point is one rule source.
- Sort/filter/search on list pages, or anything else about `MudTable` — unchanged.

## Decisions

Settled with the user before implementation:

| Question | Decision |
|---|---|
| Form component | `MudForm`, written inline per page; no wrapper |
| Rule source | The pipeline's `IValidator<TCommand>`, reused through a scoped seam — not a duplicate |
| Enter key | Keeps submitting the form |
| Update pages | Convention only, documented for `/add-feature`; no slice built |
| Create button | Disabled while `submitting` or while the form is invalid |

## Verified before designing

These were checked against MudBlazor 9.9.0 source and FluentValidation 12.1.1, and by a
throwaway spike on `CreateManufacturerPage` driven through a headless browser.

- `MudForm` renders a bare `<form>` with no submit handler of its own. A `ButtonType.Submit`
  button, or Enter, inside it would native-POST the page unless something handles `submit`.
  `SuppressImplicitSubmission` **defaults to `true`** in 9.x and renders a hidden disabled submit
  button, which is why Enter does nothing in a stock MudForm.
- The `submit` event can be handed to Blazor by splatting an `onsubmit` attribute through MudForm's
  `UserAttributes` onto that `<form>`. Blazor always `preventDefault`s a handled `submit`, so neither
  the button nor Enter reloads the page. The value must be an `EventCallback` (not a bare
  delegate) so the render tree gets an event frame with a receiver and the page re-renders after
  the handler. With `SuppressImplicitSubmission="false"` the page's real submit button is the
  form's default button and Enter submits with any number of fields. The spike confirmed all of
  this: Enter and click both reached the handler, no navigation, `submitting` re-rendered.
- MudBlazor's documented FluentValidation shape is `<MudForm Model Validation="@func">` plus
  `For=` and `Immediate="true"` on each input. The func is
  `Func<object, string, Task<IEnumerable<string>>>` and receives `(Form.Model, For member path)`.
  A field whose `For` property carries DataAnnotations runs those **as well**, so the attributes
  have to go or every message appears twice.
- `MudForm.IsValid` is evaluated once on first render from the fields' current error state, and
  untouched fields have none — so it starts `true`. There is no silent first validation. The
  Create button is therefore enabled on an empty form; clicking it (or pressing Enter) runs
  `ValidateAsync`, shows every field's errors, and sends nothing. From then on the button is
  disabled whenever any field is invalid. `MudForm.Validate()` is obsolete; `ValidateAsync()` is
  the call.
- FluentValidation's `IncludeProperties(name)` selects rules by their effective `PropertyName`,
  which `OverridePropertyName` sets. Every create validator already writes
  `.OverridePropertyName(nameof(Command.X))` on its `.Value` rule, and its repository rule
  (`MustBeUniqueIn`, the local `MustAsync(ModelExistsAsync)`) targets the wrapper whose member
  name is that same `X`. On all four pages the command's property names equal the form model's:
  `DeviceId`/`DeviceModelId`, `Name`/`Address`, `Name`, `UserName`.
- Validators are `internal`, registered **scoped** against `IValidator<TCommand>` by
  `AddSerginCore`'s assembly scan, and take repositories. They must run in a fresh root-provider
  scope — the same hazard `ScopedSerginDispatcher` exists for: in Blazor Server, "scoped" is the
  circuit's lifetime, and a validator resolved off the circuit would share one `DbContext` for as
  long as the tab is open. `FluentValidation` reaches `Sergin.SharedKernel.Presentation.Blazor`
  transitively through `Sergin.SharedKernel.Application`.

## Design

### The seam: `ISerginFormValidator`

New in `Sergin.SharedKernel.Presentation.Blazor/Validation/`:

```csharp
public interface ISerginFormValidator
{
    /// Runs only the rules the pipeline's IValidator<TRequest> declares for propertyName, in a
    /// fresh scope. No validator registered for TRequest → empty.
    Task<IReadOnlyCollection<string>> ValidateAsync<TRequest>(
        TRequest request, string propertyName, CancellationToken cancellationToken = default);
}

public static class SerginFormValidatorExtensions
{
    /// MudForm-shaped adapter. The form model MudForm passes in is ignored — the page builds the
    /// command itself, from the same ToCommand() it submits.
    public static Func<object, string, Task<IEnumerable<string>>> RulesFor<TRequest>(
        this ISerginFormValidator validator, Func<TRequest> request);
}
```

`ScopedSerginFormValidator(IServiceScopeFactory, IUserContext)` implements it the way
`ScopedSerginDispatcher` sends: `CreateAsyncScope()`, seed `UserContextAccessor.Current` with the
caller's user (no validator reads it today; a future user-aware rule then behaves identically here
and in the pipeline), `GetService<IValidator<TRequest>>()`, and
`ValidateAsync(ValidationContext<TRequest>.CreateWithOptions(request, o => o.IncludeProperties(propertyName)))`,
returning the error messages. It is registered **scoped** in `AddSerginBlazorKit`, beside
`ISerginDispatcher` and for the same reason: it carries the circuit's `IUserContext` into the
scope it opens, and a singleton would strip it.

Why messages and not `ErrorOr`: MudForm wants strings per field. The pipeline's
`ValidationPipelineBehavior` still produces the `Error.Validation` list on submit, so the
`Notify(result.Errors)` path on every create page is unchanged and remains the backstop for
anything the form does not cover (a race on a unique key, a rule on a property the form has no
field for).

### The page shape

Markup, using `CreateUserPage` as the reference:

```razor
<MudForm @ref="form" Model="model" Validation="@validation" @bind-IsValid="isValid"
         SuppressImplicitSubmission="false" onsubmit="@OnSubmit">
    <MudCard>
        <MudCardContent>
            <MudTextField @bind-Value="model.UserName" Label="User name" For="@(() => model.UserName)"
                          Immediate="true" />
        </MudCardContent>
        <MudCardActions>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
                       Disabled="@(submitting || !isValid)">Create</MudButton>
            <MudButton Href="/ua/users">Cancel</MudButton>
        </MudCardActions>
    </MudCard>
</MudForm>
```

Code-behind additions; everything after the send is as before:

```csharp
private MudForm form = default!;
private bool isValid;
private Func<object, string, Task<IEnumerable<string>>> validation = default!;

[Inject]
private ISerginFormValidator FormValidator { get; set; } = default!;

// An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so
// the page re-renders after SubmitAsync.
private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

private CreateUserCommand ToCommand() => new(new UserName(model.UserName));

private async Task SubmitAsync()
{
    await form.ValidateAsync();

    if (!form.IsValid)
    {
        return;
    }

    submitting = true;
    ErrorOr<CreateUserCommandResponse> result = await Dispatcher.SendAsync(ToCommand());
    submitting = false;
    // unchanged: Notify(result.Errors) / NavigateTo
}
```

Per-page notes:

- `MudSelect` inputs keep `For=` and need no `Immediate` — they validate on change.
- The manufacturer picker on `CreateDevicePage` is page state that narrows the model picker; it
  is not on the command. It gets `For="@(() => selectedManufacturerId)"` so the form-level func
  receives a member path, finds no rule for it, and returns nothing.
- `CreateManufacturerPage.ToCommand()` keeps the blank-address→`null` mapping that used to sit
  in `SubmitAsync`. `AddDeviceModelPage.ToCommand()` uses the route's `ManufacturerId`.
  `CreateDevicePage.OnInitializedAsync` sets `validation` before it loads the manufacturer list.
- `onsubmit` is lowercase and carries no `@`: on a component that is an unmatched attribute, and
  MudBlazor's MUD0002 analyzer admits lowercase unmatched attributes by default.

### The form models

`NewUserFormModel`, `NewDeviceFormModel`, `NewManufacturerFormModel` and
`NewDeviceModelFormModel` drop `[Required]`, `[StringLength]` and the
`System.ComponentModel.DataAnnotations` using. They stay `public sealed class` POCOs — the
binding target for `@bind-Value` and `For`. The `MaxLength` constants on the value objects are
now read by the validator and the EF configuration only.

### Cost of live repository rules

`Immediate="true"` plus `MustBeUniqueIn` means one `SELECT EXISTS` per debounced change of a
unique field. MudForm's `ValidationDelay` defaults to 300 ms and every repository rule is guarded
with `.When(not blank)`, so an empty field costs nothing. Accepted.

### What still catches a missed rule

If a future validator forgets `OverridePropertyName` on a `.Value` rule, that rule's
`PropertyName` is `X.Value`, `IncludeProperties("X")` does not select it, and the field shows
nothing while typing. The pipeline still refuses the command on submit and the page still shows
the snackbar — the form goes quiet, the system does not. `FormValidatorTests` pins the matching
for one command so the convention has a test behind it.

## Testing

`tests/Sergin.MeterMinder.IntegrationTests.All/Validation/FormValidatorTests.cs`, resolving
`ISerginFormValidator` from a scope the way `RepositoryRuleTests` resolves the dispatcher:

- `ValidateAsync_RunsOnlyTheNamedPropertysRules` — an empty `DeviceId` with an empty
  `DeviceModelId`, asked about `DeviceId`: one message about the device id, none about the model.
- `ValidateAsync_RepositoryRule_SeesCommittedRows` — create a user through the dispatcher, then
  validate a `CreateUserCommand` with the same name for `UserName`: `'User Name' is already in use.`
  Proves the fresh scope reads Postgres, not a stale change tracker.
- `ValidateAsync_RequestWithoutValidator_IsEmpty` — a query type has no validator and yields
  nothing.
- `RulesFor_IgnoresTheFormModelAndCallsThrough` — the adapter hands MudForm's `(model, property)`
  to `ValidateAsync` with the page's command.

The existing page-rendering tests keep asserting the field labels; they pass unchanged. The
spike's browser check (Enter, click, no reload, re-render) is not automated — it needs a real
circuit — and is repeated by hand on all four pages before the branch is finished.

## Files

| Repo | Path | Change |
|---|---|---|
| SharedKernel | `Sergin.SharedKernel.Presentation.Blazor/Validation/ISerginFormValidator.cs`, `SerginFormValidatorExtensions.cs`, `ScopedSerginFormValidator.cs` | new |
| SharedKernel | `Sergin.SharedKernel.Presentation.Blazor/SerginBlazorKitExtensions.cs` | register scoped |
| SharedKernel | `.claude/CLAUDE.md` | doc |
| UserAccess | `Users/Pages/CreateUserPage.razor{,.cs}`, `Users/Models/NewUserFormModel.cs`, `GlobalUsings.cs`, `.claude/CLAUDE.md` | convert |
| host | `Devices/Pages/CreateDevicePage.razor{,.cs}`, `Manufacturers/Pages/{CreateManufacturerPage,AddDeviceModelPage}.razor{,.cs}` | convert |
| host | `Devices/Models/NewDeviceFormModel.cs`, `Manufacturers/Models/{NewManufacturerFormModel,NewDeviceModelFormModel}.cs`, `GlobalUsings.cs` | strip attributes, global using |
| host | `tests/.../Validation/FormValidatorTests.cs` | new |
| host | `.claude/CLAUDE.md`, `.claude/skills/add-feature/SKILL.md`, `docs/superpowers/specs/2026-09-15-fluent-validation-design.md` (pointer) | doc |
