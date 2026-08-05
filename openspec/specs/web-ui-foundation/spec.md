# web-ui-foundation Specification

## Purpose

Establishes the Web app's design-system foundation: MudBlazor as the component/design system, a cohesive GitHub-esque dark app shell with navigation, a dark-default theme with a light toggle, and consistent MudBlazor styling across the existing pages.

## Requirements

### Requirement: MudBlazor design system
The Blazor Web App SHALL use MudBlazor as its component/design system, with the required MudBlazor services and providers registered so MudBlazor components function under the app's render mode. The default Bootstrap template styling SHALL be replaced.

#### Scenario: MudBlazor is wired up
- **WHEN** the Web app starts
- **THEN** MudBlazor services are registered and the theme/popover/dialog/snackbar providers are present so MudBlazor components render and behave correctly

### Requirement: GitHub-esque dark app shell
The app SHALL present a cohesive shell — an app bar and a navigation drawer linking the primary pages (Home, Configuration, Dependency Map, Runs) — styled in a dark, modern, GitHub-esque manner.

#### Scenario: Shell and navigation are present
- **WHEN** any page is open
- **THEN** the app bar and navigation drawer are shown, and the drawer links navigate to Home, Configuration, Dependency Map, and Runs

### Requirement: Dark-default theme with light toggle
The app SHALL default to a dark theme and SHALL provide a control in the app bar to toggle between dark and light.

#### Scenario: Dark by default
- **WHEN** the app is first loaded
- **THEN** it renders in the dark theme

#### Scenario: Theme can be toggled
- **WHEN** the user activates the theme toggle
- **THEN** the app switches between dark and light

### Requirement: Consistent styling across pages
The existing pages (Home, Configuration, Dependency Map) SHALL be presented with the MudBlazor design system so the application uses one consistent visual language, without changing their behavior.

#### Scenario: Existing pages use the design system
- **WHEN** the Configuration or Dependency Map page is open
- **THEN** it is rendered with MudBlazor components consistent with the shell, and its existing behavior (managing settings/repos, viewing the map) is unchanged
