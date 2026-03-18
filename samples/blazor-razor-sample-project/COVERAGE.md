# Blazor Razor Sample Project — Parser Construct Coverage

Constructs exercised by this sample project against `BlazorRazorParser`.

## Directives
- [x] `@page` (single route, route with constraint)
- [x] `@using` (simple namespace)
- [x] `@using` with alias (`@using Alias = Namespace`)
- [x] `@inject` (simple type, generic type)
- [x] `@inherits` (base class)
- [x] `@implements` (interface)

## Code Blocks
- [x] `@code { }` with properties and methods
- [x] `@code` with opening brace on next line
- [x] `@functions { }` (legacy syntax)
- [x] Multiple `@code` blocks in one component
- [x] Empty `@code { }` block

## Component Patterns
- [x] Component class symbol (synthetic)
- [x] `[Parameter]` properties
- [x] Lifecycle methods (`OnInitialized`)
- [x] Event handler methods
- [x] Layout components (`@inherits LayoutComponentBase`)
- [x] Error boundary components

## Other
- [x] Razor comments (`@* *@`)
- [x] Implicit expression (`@variable`)
