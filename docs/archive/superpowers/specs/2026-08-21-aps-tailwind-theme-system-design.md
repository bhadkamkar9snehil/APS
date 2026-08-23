# APS Tailwind Theme System Design

Date: 2026-08-21
Status: Approved for implementation

## Objective

Replace the current light-only, hardcoded Slate/Blue presentation with a complete Tailwind 4 theme system for APS. The system must provide polished light and dark themes, follow Windows automatically when requested, allow safe accent customization, apply consistently across the entire active UI, and preserve the installed database during the upgrade.

The visual direction is **Precision Neutral**: quiet warm-neutral surfaces, strong information hierarchy, restrained depth, and an industrial character without visual heaviness.

## Branding and version

- Replace the letter `A` in the sidebar brand with the new tactile APS icon.
- Show only `APS` beside the icon. Remove `APS Planner` and `Steel planning system` from the in-app brand block.
- Keep the Windows product/window name `APS Planner` where an operating-system-facing name is required.
- Correct the desktop release sequence from the mistakenly assigned `0.10.2` to `0.2.5`. The root `VERSION` file belongs to the older prototype line and is not authoritative for the Windows product.

## Theme modes

APS supports three explicit appearance preferences:

- **System**: resolve from `prefers-color-scheme` and react immediately when Windows changes.
- **Light**: always use the APS light theme.
- **Dark**: always use the APS dark theme.

The default is System. Preferences persist locally as a small versioned value containing mode, accent kind, and accent value. Missing, malformed, obsolete, or inaccessible preferences fall back to System with the default amber accent.

An inline bootstrap script in the desktop host document resolves and applies the theme before Blazor renders. This prevents a light flash during dark-mode startup. The document exposes the effective theme through `data-theme` and sets `color-scheme` so native controls match.

## Tailwind architecture

Tailwind 4 remains the only active application styling pipeline. The implementation will deepen it rather than add a competing override generation.

`tailwind-input.css` will define:

- the class/data-attribute dark variant;
- semantic design tokens mapped into Tailwind theme colors;
- light and dark values for canvas, elevated surfaces, inset surfaces, borders, primary/secondary/muted text, selection, hover, disabled, focus, and accent roles;
- typography, radius, shadow, density, and transition primitives;
- reduced-motion and forced-colors accommodations.

Active Razor components will use semantic utilities such as `bg-canvas`, `bg-surface`, `text-primary`, `text-secondary`, `border-subtle`, `bg-accent`, `bg-accent-soft`, and `ring-focus`. Hardcoded `slate`, `blue`, `white`, and dark-incompatible utilities will be removed from active UI surfaces.

Semantic success, warning, error, and informational colors remain independent from the selected accent. Manufacturing/process colors remain stable across themes and are tuned separately for legibility.

Selection uses a complete background and/or full outline. No selected item uses a left-edge-only border or stripe.

## Accent system

The appearance popover provides six curated accents:

- Amber (default)
- Violet
- Forest
- Brick
- Plum
- Olive

Each preset supplies light and dark values for:

- base accent;
- hover/pressed accent;
- readable foreground;
- soft selection/background;
- border;
- focus ring.

An advanced custom color picker accepts a user-selected color, derives the same semantic family, previews it in both effective surface contexts, and validates contrast. Invalid values or combinations that cannot produce accessible foreground/focus states are rejected without replacing the active preference.

Accent affects active navigation, primary actions, links, selected rows, progress, and focus. It does not recolor warning, error, success, or manufacturing-status semantics.

## Components and data flow

### Theme service

A scoped Blazor theme service owns the current preference and change notification. It calls a small JavaScript module to:

- load/save the preference;
- apply document attributes and custom properties;
- observe `matchMedia('(prefers-color-scheme: dark)')`;
- validate and derive custom accent tokens;
- notify .NET when the effective system theme changes.

The service is the single application API for reading or changing appearance. Components do not access browser storage directly.

### Appearance popover

A dedicated component sits at the bottom of the sidebar and contains:

- System/Light/Dark segmented control;
- six accent swatches;
- custom color control and contrast feedback;
- live preview;
- reset action.

It supports keyboard operation, Escape dismissal, outside-click dismissal, focus return, appropriate ARIA state, and a compact layout that does not reduce planning workspace.

### Brand

The generated transparent master icon is copied into the UI static assets and rendered as an image in the sidebar. It is decorative beside the `APS` text and therefore has an empty alternative description. The OS executable, installer, and shortcut continue using the multi-resolution `.ico`.

## UI coverage

The migration covers every active shell and page surface, including:

- sidebar navigation and grouping;
- plan context bar;
- context inspector;
- footer/update states;
- control tower and empty states;
- plan versions and comparison;
- demand, campaign, steelmaking, rolling, schedule, inventory, and material flow;
- master-data editors;
- work orders and traceability;
- planning sandbox;
- tables, forms, buttons, badges, dialogs, charts, schedule blocks, tooltips, scrollbars, loading, errors, and disabled states.

The work does not revive or restyle archived prototype HTML/CSS.

## Accessibility and interaction

- Text and interactive states target WCAG AA contrast.
- Focus is visible for keyboard users in both themes and every accent.
- Color is not the sole status indicator.
- Controls expose accessible names and selected/expanded state.
- Motion respects `prefers-reduced-motion`.
- System-mode changes do not discard the user's selected accent.
- Theme changes apply without reload and without transient mixed-theme surfaces.

## Error handling

- Storage unavailable: use in-memory System + Amber and keep the UI operational.
- Invalid stored preference: ignore it and write a valid default on the next user change.
- Invalid custom color: show concise inline feedback and preserve the previous valid accent.
- JavaScript interop unavailable during prerender/disposal: do not crash the layout; apply defaults until interop is available.

## Testing and verification

Implementation follows test-first development for theme behavior.

Automated coverage will verify:

- default and persisted preference loading;
- mode changes and effective-theme resolution;
- system change notifications only affecting System mode;
- preset accent selection;
- valid custom accent application;
- invalid custom value rejection and fallback;
- reset behavior;
- brand text/icon rendering;
- Tailwind compilation and absence of deprecated hardcoded shell classes.

Release verification will include:

- full solution build;
- the existing planning test suite with unrelated failures reported honestly rather than hidden;
- representative light and dark desktop captures;
- keyboard/focus inspection;
- executable, window, shortcut, and installer icon checks;
- installed version `0.2.5`;
- pre-upgrade SQLite online backup;
- post-upgrade integrity, migration count, and application-record comparison;
- launch of the installed desktop application.

## Non-goals

- No arbitrary layout redesign of planning workflows.
- No new charting library.
- No remote account/profile synchronization.
- No recoloring of semantic statuses based on user accent.
- No publication to GitHub Releases unless separately requested.

