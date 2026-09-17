# MudForm Client Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the four Blazor create pages from `EditForm` + DataAnnotations to `MudForm`, with each field validated live by the pipeline's own FluentValidation validator through a new scoped SharedKernel seam.

**Architecture:** `ISerginFormValidator` (SharedKernel, `Presentation.Blazor/Validation/`) opens a fresh DI scope per call — exactly as `ScopedSerginDispatcher` does — resolves `IValidator<TCommand>` and runs it for one property (`IncludeProperties`). Its `RulesFor(ToCommand)` adapter is what a page hands to `MudForm.Validation`. Pages keep their form model as the binding target, build the command from it in one `ToCommand()` used by both validation and submit, and hand the browser's `submit` event to Blazor by splatting an `EventCallback` as `onsubmit` onto MudForm (with `SuppressImplicitSubmission="false"` so Enter still submits). Form models lose their DataAnnotations; the pipeline validator stays the backstop.

**Tech Stack:** .NET 10, MudBlazor 9.9.0, FluentValidation 12.1.1, MediatR, ErrorOr, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-09-17-mudform-client-validation-design.md`

## Global Constraints

- `Directory.Build.props`: `TreatWarningsAsErrors=true`, `AnalysisMode=All`, SonarAnalyzer, `EnforceCodeStyleInBuild`. Any warning fails the build. File-scoped namespaces (IDE0161). IDE0007 wants `var` where the type is apparent (a cast, a `new`).
- `MudForm.Validate()` is obsolete (CS0618 fails the build) — call `ValidateAsync()`.
- `.razor` files are markup-only; all C# in the `.razor.cs` partial. Zero `@code` blocks.
- Inject `ISerginDispatcher` and `ISerginFormValidator` in Blazor, never `ISender` or `IValidator<T>` directly.
- `.Presentation.Blazor` references `.Application.Contracts`, never `.Application`.
- Three repos: `src/SharedKernel` and `src/Modules/UserAccess` are submodules. Commit inside them first, PR and merge there, then bump the pointer in the host. A pure pointer bump is committed directly.
- Work in the worktree `.claude/worktrees/mudform-validation` (branch `feature/mudform-client-validation`). Run `git submodule update --init --recursive` there before the first build.
- Commit messages: plain English, imperative, no `Co-Authored-By` trailer.
- Build: `dotnet build Sergin.MeterMinder.slnx`. Tests: `dotnet test tests/Sergin.MeterMinder.IntegrationTests.All/Sergin.MeterMinder.IntegrationTests.All.csproj` (Docker Desktop running).

## Tasks

### Task 1: Spike (done 2026-09-17, not committed)

- [x] `CreateManufacturerPage` converted with a stub validation func, host run on `:5555`, driven by headless Chromium. Enter and click both reached `SubmitAsync`, no navigation, `submitting` re-rendered, live errors displayed, button disabled while invalid. Findings folded into the spec: `SuppressImplicitSubmission` defaults to `true`, `Validate()` is obsolete, `IsValid` starts `true`.

### Task 2: SharedKernel seam

- [ ] `src/SharedKernel/Sergin.SharedKernel.Presentation.Blazor/Validation/ISerginFormValidator.cs`
- [ ] `.../Validation/SerginFormValidatorExtensions.cs` — `RulesFor<TRequest>(this ISerginFormValidator, Func<TRequest>)`
- [ ] `.../Validation/ScopedSerginFormValidator.cs` — `internal sealed`, primary constructor `(IServiceScopeFactory scopeFactory, IUserContext userContext)`
- [ ] `SerginBlazorKitExtensions.AddSerginBlazorKit`: `services.AddScoped<ISerginFormValidator, ScopedSerginFormValidator>();`
- [ ] `src/SharedKernel/.claude/CLAUDE.md`: `.Presentation.Blazor` bullet names the seam next to `ISerginDispatcher`
- [ ] `dotnet build Sergin.SharedKernel.slnx` inside `src/SharedKernel`
- [ ] Commit on a branch in the submodule, push, PR, merge; bump pointer in the host

### Task 3: Host tests (written before the pages change)

- [ ] `tests/Sergin.MeterMinder.IntegrationTests.All/Validation/FormValidatorTests.cs` with the four tests the spec lists
- [ ] `dotnet test` — green against the new SharedKernel pointer

### Task 4: DeviceManagement pages

- [ ] `Devices/Pages/CreateDevicePage.razor` + `.razor.cs`
- [ ] `Manufacturers/Pages/CreateManufacturerPage.razor` + `.razor.cs`
- [ ] `Manufacturers/Pages/AddDeviceModelPage.razor` + `.razor.cs`
- [ ] `Devices/Models/NewDeviceFormModel.cs`, `Manufacturers/Models/NewManufacturerFormModel.cs`, `Manufacturers/Models/NewDeviceModelFormModel.cs` — strip attributes
- [ ] `GlobalUsings.cs`: `global using Sergin.SharedKernel.Presentation.Blazor.Validation;`
- [ ] `src/Modules/DeviceManagement/CLAUDE.md` if it describes the form shape
- [ ] `dotnet build Sergin.MeterMinder.slnx`

### Task 5: UserAccess page

- [ ] `Users/Pages/CreateUserPage.razor` + `.razor.cs`, `Users/Models/NewUserFormModel.cs`, `GlobalUsings.cs`
- [ ] `src/Modules/UserAccess/.claude/CLAUDE.md`: validation bullet no longer says the form model carries `[StringLength]`
- [ ] Build through the host; commit on a branch in the submodule, push, PR, merge; bump pointer

### Task 6: Host docs

- [ ] `.claude/CLAUDE.md`: Validation bullet; Blazor UI conventions (form-model bullet, a new create/update-form bullet: MudForm, `onsubmit` splat, `SuppressImplicitSubmission="false"`, `IsValid` starts true, `ValidateAsync`); SharedKernel `.Presentation.Blazor` list
- [ ] `.claude/skills/add-feature/SKILL.md`: layout table row, form-model row, markup conventions paragraph
- [ ] `.claude/skills/add-module/SKILL.md` and `README.md` if they repeat the shape
- [ ] `docs/superpowers/specs/2026-09-15-fluent-validation-design.md`: one-line supersession pointer

### Task 7: Verify and finish

- [ ] `dotnet build Sergin.MeterMinder.slnx`, `dotnet test ...`
- [ ] `dotnet run` and walk all four pages: empty field → inline message; taken user name → "'User Name' is already in use." while typing; unknown model impossible from the picker but the empty-model message shows; Create disabled while invalid; Enter submits; Cancel; no reload
- [ ] `graphify update .` then `python .claude/skills/graphify/scripts/graphify_repair.py`
- [ ] PR into host `main` with both submodule bumps; after merge remove the worktree and branches in all three repos
