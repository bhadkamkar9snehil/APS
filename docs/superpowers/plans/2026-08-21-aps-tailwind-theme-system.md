# APS Tailwind Theme System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a complete Tailwind 4 light/dark/system theme system for APS with persisted curated and custom accents, new in-app branding, full active-page coverage, version 0.2.5, and a verified database-safe desktop installation.

**Architecture:** A focused theme domain and scoped Blazor service own appearance state. A small JavaScript module applies the effective theme before and after Blazor startup, observes Windows theme changes, and persists preferences. Tailwind semantic tokens provide the only active styling layer, so every Razor component consumes roles rather than fixed palette colors.

**Tech Stack:** .NET 10, WPF BlazorWebView, Razor components, Tailwind CSS 4.1.14 standalone CLI, JavaScript interop, xUnit, Velopack 1.2.0, SQLite/EF Core.

**Spec:** `docs/superpowers/specs/2026-08-21-aps-tailwind-theme-system-design.md`

## Global Constraints

- Desktop release version is `0.2.5`; restore the legacy root `VERSION` file to `0.10.0` because it is not the desktop version authority.
- In-app brand is the tactile icon plus `APS`, with no subtitle.
- Modes are exactly `System`, `Light`, and `Dark`; default is System.
- Accent model is six curated presets plus an accessibility-validated custom color.
- Selection uses a complete background and/or full outline; never a left-edge-only stripe.
- Semantic status and manufacturing colors are independent from the user accent.
- Tailwind is the single active styling system; do not introduce a second override stylesheet.
- Preserve `%LocalAppData%\APS-Data\Data\aps.db` through an online backup and post-install comparison.
- Do not publish a GitHub release.

---

## File map

- `src/APS.UI/Theme/ThemeMode.cs`: appearance-mode enum.
- `src/APS.UI/Theme/ThemePreference.cs`: versioned persisted preference value.
- `src/APS.UI/Theme/ThemeAccent.cs`: preset identifiers and custom accent representation.
- `src/APS.UI/Theme/ThemeColor.cs`: strict hex parsing, contrast calculation, and accessible foreground selection.
- `src/APS.UI/Theme/ThemeService.cs`: scoped state, JS initialization, mutation API, and change notification.
- `src/APS.UI/wwwroot/theme.js`: early/runtime application, storage, media-query observation, and document token updates.
- `src/APS.UI/Components/Layout/AppearancePopover.razor`: accessible sidebar theme controls.
- `src/APS.UI/wwwroot/app-icon.png`: static in-app brand asset.
- `src/APS.UI/wwwroot/tailwind-input.css`: authoritative semantic Tailwind token system.
- `tests/APS.UI.Tests/*`: focused theme-domain and source-contract tests.
- Active `.razor` files under `src/APS.UI/Components`: semantic utility migration.
- `src/APS.DesktopHost/wwwroot/index.html`: no-flash bootstrap.
- `src/APS.DesktopHost/App.xaml.cs` and `src/APS.Service/Program.cs`: theme service registration for both hosts.
- `VERSION` and `src/APS.DesktopHost/APS.DesktopHost.csproj`: correct version split.

---

### Task 1: Theme domain and test project

**Files:**
- Create: `tests/APS.UI.Tests/APS.UI.Tests.csproj`
- Create: `tests/APS.UI.Tests/ThemePreferenceTests.cs`
- Create: `tests/APS.UI.Tests/ThemeColorTests.cs`
- Create: `src/APS.UI/Theme/ThemeMode.cs`
- Create: `src/APS.UI/Theme/ThemeAccent.cs`
- Create: `src/APS.UI/Theme/ThemePreference.cs`
- Create: `src/APS.UI/Theme/ThemeColor.cs`
- Modify: `APS.slnx`

**Interfaces:**
- Produces: `ThemeMode`, `ThemeAccentKind`, `ThemePreference`, `ThemeColor.TryParseHex(string?, out ThemeColor)`, `ThemeColor.RelativeLuminance`, and `ThemeColor.BestForeground()`.

- [ ] **Step 1: Write failing domain tests**

```csharp
[Fact]
public void Default_preference_follows_system_with_amber_accent()
{
    Assert.Equal(ThemeMode.System, ThemePreference.Default.Mode);
    Assert.Equal(ThemeAccentKind.Amber, ThemePreference.Default.Accent.Kind);
}

[Theory]
[InlineData("#7c3aed")]
[InlineData("#7C3AED")]
public void Strict_hex_parser_accepts_six_digit_rgb(string input) =>
    Assert.True(ThemeColor.TryParseHex(input, out _));

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("7c3aed")]
[InlineData("#fff")]
[InlineData("#zzzzzz")]
public void Strict_hex_parser_rejects_invalid_values(string? input) =>
    Assert.False(ThemeColor.TryParseHex(input, out _));

[Fact]
public void Foreground_selection_meets_aa_for_representative_custom_accent()
{
    ThemeColor.TryParseHex("#7C3AED", out var color);
    Assert.True(color.ContrastRatio(color.BestForeground()) >= 4.5);
}
```

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter "ThemePreferenceTests|ThemeColorTests"`

Expected: FAIL because the project/theme types do not exist.

- [ ] **Step 3: Implement the minimal immutable domain types**

Use a versioned preference record with `CurrentVersion = 1`, enum-backed preset accents, and strict `#RRGGBB` parsing. Compute WCAG relative luminance by converting sRGB channels to linear light; `BestForeground()` chooses `#171411` or `#FFFFFF` by higher contrast.

- [ ] **Step 4: Add the test project to `APS.slnx` and verify GREEN**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj`

Expected: all theme-domain tests pass.

- [ ] **Step 5: Commit**

```powershell
git add APS.slnx src/APS.UI/Theme tests/APS.UI.Tests
git commit -m "feat: add APS theme domain"
```

### Task 2: Browser bridge, persistence, and system-mode behavior

**Files:**
- Create: `src/APS.UI/Theme/ThemeService.cs`
- Create: `src/APS.UI/wwwroot/theme.js`
- Create: `tests/APS.UI.Tests/ThemeServiceTests.cs`
- Modify: `src/APS.DesktopHost/wwwroot/index.html`
- Modify: `src/APS.DesktopHost/App.xaml.cs`
- Modify: `src/APS.Service/Program.cs`

**Interfaces:**
- Consumes: Task 1 theme-domain types.
- Produces: `ThemeService.InitializeAsync()`, `SetModeAsync(ThemeMode)`, `SetPresetAsync(ThemeAccentKind)`, `SetCustomAccentAsync(string)`, `ResetAsync()`, `Preference`, `EffectiveTheme`, and `Changed`.
- JavaScript exports: `initialize(dotNetRef)`, `apply(preference)`, `load()`, `reset()`, and `dispose()`.

- [ ] **Step 1: Write failing service tests with a recording `IJSRuntime`**

```csharp
[Fact]
public async Task Invalid_custom_accent_preserves_previous_preference()
{
    var service = new ThemeService(new RecordingJsRuntime());
    await service.SetPresetAsync(ThemeAccentKind.Forest);
    var changed = await service.SetCustomAccentAsync("not-a-color");
    Assert.False(changed);
    Assert.Equal(ThemeAccentKind.Forest, service.Preference.Accent.Kind);
}

[Fact]
public async Task Reset_restores_system_and_amber()
{
    var service = new ThemeService(new RecordingJsRuntime());
    await service.SetModeAsync(ThemeMode.Dark);
    await service.ResetAsync();
    Assert.Equal(ThemePreference.Default, service.Preference);
}
```

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ThemeServiceTests`

Expected: FAIL because `ThemeService` does not exist.

- [ ] **Step 3: Implement `ThemeService` and `theme.js`**

Use storage key `aps.appearance.v1`. The JS module applies `data-theme`, `data-theme-mode`, and accent custom properties on `document.documentElement`; it observes `matchMedia('(prefers-color-scheme: dark)')` and invokes `[JSInvokable] OnSystemThemeChanged(bool dark)` only while initialized. The C# service validates custom colors before invoking JS and never mutates state after disposal.

- [ ] **Step 4: Add a no-flash inline bootstrap before styles render**

The host document must read `aps.appearance.v1`, validate its shape defensively, resolve System through `matchMedia`, and set the document attributes before loading `tailwind.css`. Fallback is System + Amber.

- [ ] **Step 5: Register `ThemeService` in desktop and service hosts**

Use `services.AddScoped<ThemeService>()` in both host composition roots.

- [ ] **Step 6: Verify GREEN**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ThemeServiceTests`

Expected: all service tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/APS.UI/Theme src/APS.UI/wwwroot/theme.js src/APS.DesktopHost/wwwroot/index.html src/APS.DesktopHost/App.xaml.cs src/APS.Service/Program.cs tests/APS.UI.Tests
git commit -m "feat: persist APS appearance preferences"
```

### Task 3: Semantic Tailwind design system

**Files:**
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Create: `tests/APS.UI.Tests/TailwindThemeContractTests.cs`

**Interfaces:**
- Consumes: document `data-theme` and accent variables from Task 2.
- Produces Tailwind roles: `canvas`, `surface`, `surface-raised`, `surface-inset`, `border`, `border-strong`, `primary`, `secondary`, `muted`, `accent`, `accent-hover`, `accent-soft`, `accent-foreground`, `focus`, `success`, `warning`, `danger`, and `info`.

- [ ] **Step 1: Write failing source-contract tests**

```csharp
[Fact]
public void Tailwind_source_declares_light_dark_and_semantic_roles()
{
    var css = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind-input.css"));
    Assert.Contains("[data-theme=\"dark\"]", css);
    Assert.Contains("--color-canvas", css);
    Assert.Contains("--color-surface", css);
    Assert.Contains("--color-accent-soft", css);
    Assert.Contains("prefers-reduced-motion", css);
}
```

- [ ] **Step 2: Run contract test and verify RED**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter TailwindThemeContractTests`

Expected: FAIL because dark and semantic roles are absent.

- [ ] **Step 3: Replace the minimal light-only Tailwind source with the semantic system**

Declare warm-neutral light/dark CSS variables in `:root` and `[data-theme="dark"]`; map them through `@theme inline`; define preset accent attribute blocks and custom-variable fallbacks; set body, selection, focus-visible, scrollbar, native control, and reduced-motion base behavior. Keep density compact and shadows restrained.

- [ ] **Step 4: Compile Tailwind and verify generated roles**

Run: `dotnet build src/APS.UI/APS.UI.csproj --configuration Release`

Expected: Tailwind compilation succeeds and generated `tailwind.css` contains `.bg-canvas`, `.bg-surface`, `.text-primary`, `.border-border`, and `.ring-focus`.

- [ ] **Step 5: Run contract tests and verify GREEN**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter TailwindThemeContractTests`

- [ ] **Step 6: Commit**

```powershell
git add src/APS.UI/wwwroot/tailwind-input.css src/APS.UI/wwwroot/tailwind.css tests/APS.UI.Tests/TailwindThemeContractTests.cs
git commit -m "feat: define semantic Tailwind themes"
```

### Task 4: Branding and appearance popover

**Files:**
- Create: `src/APS.UI/Components/Layout/AppearancePopover.razor`
- Create: `src/APS.UI/wwwroot/app-icon.png`
- Modify: `src/APS.UI/Components/Layout/MainLayout.razor`
- Modify: `src/APS.UI/Components/Layout/NavItem.razor`
- Modify: `src/APS.UI/Components/Layout/NavGroup.razor`
- Create: `tests/APS.UI.Tests/LayoutThemeContractTests.cs`

**Interfaces:**
- Consumes: `ThemeService` and semantic Tailwind roles.
- Produces: accessible sidebar appearance popover and icon-plus-`APS` brand.

- [ ] **Step 1: Write failing layout source-contract tests**

```csharp
[Fact]
public void Main_layout_uses_icon_and_single_APS_brand()
{
    var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MainLayout.razor"));
    Assert.Contains("app-icon.png", razor);
    Assert.Contains(">APS<", razor);
    Assert.DoesNotContain("Steel planning system", razor);
    Assert.DoesNotContain(">A</div>", razor);
}

[Fact]
public void Appearance_popover_exposes_all_modes_and_reset()
{
    var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/AppearancePopover.razor"));
    Assert.Contains("System", razor);
    Assert.Contains("Light", razor);
    Assert.Contains("Dark", razor);
    Assert.Contains("Reset", razor);
}
```

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter LayoutThemeContractTests`

- [ ] **Step 3: Copy the generated master icon into UI static assets**

Copy `src/APS.DesktopHost/Assets/app-icon-master.png` to `src/APS.UI/wwwroot/app-icon.png` without modifying the original.

- [ ] **Step 4: Implement the accessible popover and revised sidebar**

Use a complete outline/background for selected mode and accent, `aria-pressed` for mode/swatch buttons, `aria-expanded` on the trigger, Escape/outside dismissal through the JS module, and focus return. Initialize the service once from the layout and unsubscribe on disposal.

- [ ] **Step 5: Convert shell/navigation classes to semantic roles**

Use warm-neutral canvas/surface roles, full-row active navigation background, semantic borders/text, compact hover/focus transitions, and an always-visible bottom appearance trigger.

- [ ] **Step 6: Run layout tests and build**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter LayoutThemeContractTests`

Run: `dotnet build src/APS.DesktopHost/APS.DesktopHost.csproj --configuration Release --no-restore`

- [ ] **Step 7: Commit**

```powershell
git add src/APS.UI/Components/Layout src/APS.UI/wwwroot/app-icon.png tests/APS.UI.Tests/LayoutThemeContractTests.cs
git commit -m "feat: add APS appearance controls"
```

### Task 5: Full active-UI semantic migration

**Files:**
- Modify: every active `.razor` file under `src/APS.UI/Components/Pages`
- Modify: every active `.razor` file under `src/APS.UI/Components/Shared`
- Modify: `src/APS.UI/Components/Layout/PlanContextBar.razor`
- Modify: `src/APS.UI/Components/Layout/ContextInspector.razor`
- Create: `tests/APS.UI.Tests/ThemeCoverageTests.cs`

**Interfaces:**
- Consumes: Task 3 semantic Tailwind roles.
- Produces: complete light/dark/accent coverage across all active UI pages.

- [ ] **Step 1: Write a failing active-surface coverage test**

```csharp
[Fact]
public void Active_razor_surfaces_do_not_use_fixed_shell_palette()
{
    var files = Directory.GetFiles(Repo.File("src/APS.UI/Components"), "*.razor", SearchOption.AllDirectories);
    var banned = new[] { "bg-white", "bg-slate-50", "text-slate-900", "border-slate-200", "bg-blue-", "text-blue-" };
    var violations = files.SelectMany(file => banned.Where(token => File.ReadAllText(file).Contains(token)).Select(token => $"{file}: {token}"));
    Assert.Empty(violations);
}

[Fact]
public void Selected_rows_never_use_left_edge_only_accent()
{
    var files = Directory.GetFiles(Repo.File("src/APS.UI/Components"), "*.razor", SearchOption.AllDirectories);
    Assert.DoesNotContain(files, file => File.ReadAllText(file).Contains("border-l") && File.ReadAllText(file).Contains("accent"));
}
```

- [ ] **Step 2: Run coverage tests and verify RED with the current violations list**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ThemeCoverageTests`

- [ ] **Step 3: Migrate layout support and shared components**

Replace fixed shell palette utilities with semantic roles in `PlanContextBar`, `ContextInspector`, `Table`, `StatTile`, and `ScheduleGantt`. Keep process/status colors semantic and stable.

- [ ] **Step 4: Migrate planning pages in coherent batches**

Batch A: Home, PlanVersions, PlanCompare, Planning.

Batch B: DemandSupply, CampaignStudio, SteelmakingCasting, RollingFinishing, FiniteSchedule.

Batch C: Inventory, MaterialFlow, MasterData, WorkOrders, Traceability.

For tables/forms/actions use shared semantic combinations consistently: surface + border, primary/secondary/muted text, accent primary action, soft full-row selection, and semantic state badges.

- [ ] **Step 5: Run coverage tests and verify GREEN**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ThemeCoverageTests`

- [ ] **Step 6: Compile Tailwind and desktop host**

Run: `dotnet build src/APS.DesktopHost/APS.DesktopHost.csproj --configuration Release`

- [ ] **Step 7: Commit**

```powershell
git add src/APS.UI/Components tests/APS.UI.Tests/ThemeCoverageTests.cs src/APS.UI/wwwroot/tailwind.css
git commit -m "feat: theme all APS workspaces"
```

### Task 6: Correct versioning and repository hygiene

**Files:**
- Modify: `VERSION`
- Modify: `src/APS.DesktopHost/APS.DesktopHost.csproj`
- Modify: `.gitignore`
- Create: `tests/APS.UI.Tests/ReleaseMetadataTests.cs`

**Interfaces:**
- Produces: desktop version `0.2.5`; legacy root version `0.10.0`; ignored `.superpowers/` brainstorming workspace.

- [ ] **Step 1: Write failing metadata tests**

```csharp
[Fact]
public void Desktop_and_legacy_versions_are_intentionally_distinct()
{
    Assert.Equal("0.10.0", File.ReadAllText(Repo.File("VERSION")).Trim());
    var project = XDocument.Load(Repo.File("src/APS.DesktopHost/APS.DesktopHost.csproj"));
    Assert.Equal("0.2.5", project.Descendants("Version").Single().Value);
}
```

- [ ] **Step 2: Run metadata test and verify RED**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ReleaseMetadataTests`

- [ ] **Step 3: Restore root version, set desktop version, and ignore visual-companion state**

Set `VERSION` to `0.10.0`, desktop `<Version>` to `0.2.5`, and append `.superpowers/` to `.gitignore`.

- [ ] **Step 4: Verify GREEN and commit**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter ReleaseMetadataTests`

```powershell
git add VERSION src/APS.DesktopHost/APS.DesktopHost.csproj .gitignore tests/APS.UI.Tests/ReleaseMetadataTests.cs
git commit -m "fix: restore APS desktop version sequence"
```

### Task 7: Verification, database-safe package, install, and launch

**Files:**
- Verify: `build/Releases/APS-win-Setup.exe`
- Verify: `%LocalAppData%\APS-Data\Data\aps.db`
- Create runtime backup: `%LocalAppData%\APS-Data\Backups\aps-<timestamp>-pre-0.2.5.db`

**Interfaces:**
- Consumes: completed theme implementation and release metadata.
- Produces: installed, running APS Planner 0.2.5 with preserved database and working shortcuts/icons.

- [ ] **Step 1: Run focused theme tests**

Run: `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --configuration Release`

Expected: all theme tests pass.

- [ ] **Step 2: Run full build and existing planning suite**

Run: `dotnet build APS.slnx --configuration Release`

Run: `dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --configuration Release --no-build`

Expected: build succeeds. Record the exact planning-test result; do not conceal pre-existing route/campaign failures.

- [ ] **Step 3: Capture the live database baseline and online backup**

Use Python `sqlite3.Connection.backup()` while APS is running. Record `PRAGMA integrity_check`, migration count, per-table row counts, and backup SHA-256.

- [ ] **Step 4: Package version 0.2.5**

Run: `pwsh build/release.ps1 -Version 0.2.5 -SkipTests`

The bypass is permitted only because the full planning-test result was run and recorded in Step 2; this local package is not published.

- [ ] **Step 5: Install silently and launch visibly**

Run: `build/Releases/APS-win-Setup.exe --silent`, wait for the Velopack completion log, then start `%LocalAppData%\APS\current\APS.DesktopHost.exe` visibly.

- [ ] **Step 6: Verify installed runtime**

Confirm executable file version `0.2.5.0`, window title `APS Planner`, responsive process, Desktop and Start menu shortcut targets/icons, live database `integrity=ok`, unchanged application row counts, and migration history at least as complete as baseline.

- [ ] **Step 7: Capture representative light and dark screens**

Capture Control Tower and one dense workspace in Light and Dark, plus one non-default accent. Inspect for flash, contrast, mixed-theme surfaces, clipped popover, focus visibility, and full-row selection.

- [ ] **Step 8: Final repository audit**

Run: `git status --short`, `git diff --check`, and inspect commits/diff for generated caches, database files, credentials, or unrelated changes.

