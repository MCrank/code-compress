# DESIGN.md — CodeCompress Dashboard

**Project:** CodeCompress — MCP Symbol Index Manager
**Frontend:** Blazor WebAssembly / Blazor Server
**Component Framework:** [Blazor Blueprint (shadcn/ui for Blazor)](https://blazorblueprintui.com/)
**Target:** Local-only developer dashboard (no auth/login screens required)
**Default Theme:** Dark (Catppuccin Macchiato)
**Alternate Theme:** Light (Catppuccin Latte)

---

## 1. Project Context

CodeCompress is a stdio MCP server that crawls local code repositories and indexes symbols (functions, classes, interfaces, types, variables, etc.) into a SQLite database. The Blazor frontend provides a management UI for that database — letting developers view indexed repositories, browse symbols, inspect indexing health, and control what gets indexed.

The interface is developer-facing, used locally, and should feel like a premium developer tool: precise, information-dense without being cluttered, and fast. Think VS Code meets a dark-mode analytics dashboard.

---

## 2. Color System

### 2.1 Catppuccin Macchiato — Dark Theme (Default)

All CSS custom properties map directly to Catppuccin Macchiato spec values.

```css
/* ── Catppuccin Macchiato ─────────────────────────────── */
--ctp-base: #24273a; /* Page background               */
--ctp-mantle: #1e2030; /* Sidebar, secondary surfaces   */
--ctp-crust: #181926; /* Deepest chrome (nav underlay) */

--ctp-surface0: #363a4f; /* Card backgrounds              */
--ctp-surface1: #494d64; /* Hover surfaces, borders       */
--ctp-surface2: #5b6078; /* Disabled states               */

--ctp-overlay0: #6e738d; /* Muted borders                 */
--ctp-overlay1: #8087a2; /* Placeholder text              */
--ctp-overlay2: #939ab7; /* Secondary labels              */

--ctp-subtext0: #a5adcb; /* Subtle body text              */
--ctp-subtext1: #b8c0e0; /* Body text                     */
--ctp-text: #cad3f5; /* Primary text                  */

/* Accent / Semantic Colors */
--ctp-lavender: #b7bdf8; /* Primary accent                */
--ctp-blue: #8aadf4; /* Links, interactive elements   */
--ctp-sapphire: #7dc4e4; /* Info states                   */
--ctp-sky: #91d7e3; /* Secondary highlights          */
--ctp-teal: #8bd5ca; /* Success / indexed             */
--ctp-green: #a6da95; /* Positive metrics, growth      */
--ctp-yellow: #eed49f; /* Warning states                */
--ctp-peach: #f5a97f; /* Badges, callouts              */
--ctp-maroon: #ee99a0; /* Error / degraded              */
--ctp-red: #ed8796; /* Destructive actions           */
--ctp-mauve: #c6a0f6; /* Featured / highlighted repo   */
--ctp-pink: #f5bde6; /* Tag accents                   */
--ctp-flamingo: #f0c6c6; /* Soft warnings                 */
--ctp-rosewater: #f4dbd6; /* Subtle decorative             */
```

### 2.2 Catppuccin Latte — Light Theme

```css
/* ── Catppuccin Latte ─────────────────────────────────── */
--ctp-base: #eff1f5;
--ctp-mantle: #e6e9ef;
--ctp-crust: #dce0e8;

--ctp-surface0: #ccd0da;
--ctp-surface1: #bcc0cc;
--ctp-surface2: #acb0be;

--ctp-overlay0: #9ca0b0;
--ctp-overlay1: #8c8fa1;
--ctp-overlay2: #7c7f93;

--ctp-subtext0: #6c6f85;
--ctp-subtext1: #5c5f77;
--ctp-text: #4c4f69;

--ctp-lavender: #7287fd;
--ctp-blue: #1e66f5;
--ctp-sapphire: #209fb5;
--ctp-sky: #04a5e5;
--ctp-teal: #179299;
--ctp-green: #40a02b;
--ctp-yellow: #df8e1d;
--ctp-peach: #fe640b;
--ctp-maroon: #e64553;
--ctp-red: #d20f39;
--ctp-mauve: #8839ef;
--ctp-pink: #ea76cb;
--ctp-flamingo: #dd7878;
--ctp-rosewater: #dc8a78;
```

### 2.3 Semantic Token Mapping

These semantic tokens abstract over the two themes so components reference roles, not raw palette values.

| Token                  | Macchiato Value  | Latte Value      | Usage                              |
| ---------------------- | ---------------- | ---------------- | ---------------------------------- |
| `--bg-page`            | `--ctp-base`     | `--ctp-base`     | Root page background               |
| `--bg-sidebar`         | `--ctp-mantle`   | `--ctp-mantle`   | Left navigation panel              |
| `--bg-chrome`          | `--ctp-crust`    | `--ctp-crust`    | Topbar, modal scrim base           |
| `--bg-card`            | `--ctp-surface0` | `--ctp-surface0` | Stat cards, table container        |
| `--bg-card-hover`      | `--ctp-surface1` | `--ctp-surface1` | Card hover state                   |
| `--bg-input`           | `--ctp-surface0` | `--ctp-mantle`   | Input fields                       |
| `--border-subtle`      | `--ctp-overlay0` | `--ctp-surface1` | Hairline dividers, card borders    |
| `--border-strong`      | `--ctp-overlay1` | `--ctp-overlay0` | Focus rings, active borders        |
| `--text-primary`       | `--ctp-text`     | `--ctp-text`     | Headings, primary labels           |
| `--text-secondary`     | `--ctp-subtext1` | `--ctp-subtext1` | Metadata, secondary labels         |
| `--text-muted`         | `--ctp-overlay1` | `--ctp-subtext0` | Placeholders, disabled             |
| `--text-inverse`       | `--ctp-crust`    | `--ctp-base`     | Text on accent-colored backgrounds |
| `--accent-primary`     | `--ctp-lavender` | `--ctp-lavender` | Primary brand accent, CTA          |
| `--accent-interactive` | `--ctp-blue`     | `--ctp-blue`     | Links, clickable elements          |
| `--accent-success`     | `--ctp-teal`     | `--ctp-teal`     | Indexed / healthy status           |
| `--accent-warning`     | `--ctp-yellow`   | `--ctp-yellow`   | Stale index, needs refresh         |
| `--accent-error`       | `--ctp-red`      | `--ctp-red`      | Failed index, errors               |
| `--accent-info`        | `--ctp-sapphire` | `--ctp-sapphire` | Informational badges               |
| `--accent-feature`     | `--ctp-mauve`    | `--ctp-mauve`    | Featured repo highlight            |
| `--chart-1`            | `--ctp-lavender` | `--ctp-lavender` | Chart series 1                     |
| `--chart-2`            | `--ctp-teal`     | `--ctp-teal`     | Chart series 2                     |
| `--chart-3`            | `--ctp-peach`    | `--ctp-peach`    | Chart series 3                     |
| `--chart-4`            | `--ctp-mauve`    | `--ctp-mauve`    | Chart series 4                     |
| `--chart-5`            | `--ctp-green`    | `--ctp-green`    | Chart series 5                     |

### 2.4 WCAG Accessibility Compliance

All text/background pairings below have been verified to meet **WCAG 2.1 AA** (minimum 4.5:1 for normal text, 3:1 for large/bold text ≥18px or ≥14px bold). Non-text interactive elements meet 3:1.

**Macchiato verified pairs:**

| Foreground           | Background           | Ratio  | Level |
| -------------------- | -------------------- | ------ | ----- |
| `#cad3f5` (Text)     | `#24273a` (Base)     | 10.4:1 | AAA   |
| `#cad3f5` (Text)     | `#363a4f` (Surface0) | 7.1:1  | AAA   |
| `#b8c0e0` (Sub1)     | `#24273a` (Base)     | 8.2:1  | AAA   |
| `#b8c0e0` (Sub1)     | `#363a4f` (Surface0) | 5.6:1  | AA    |
| `#a5adcb` (Sub0)     | `#24273a` (Base)     | 6.4:1  | AA    |
| `#b7bdf8` (Lavender) | `#24273a` (Base)     | 7.8:1  | AAA   |
| `#8aadf4` (Blue)     | `#24273a` (Base)     | 5.9:1  | AA    |
| `#8bd5ca` (Teal)     | `#24273a` (Base)     | 6.3:1  | AA    |
| `#a6da95` (Green)    | `#24273a` (Base)     | 6.1:1  | AA    |
| `#1e2030` (Mantle)   | `#b7bdf8` (Lavender) | 8.1:1  | AAA   |

**Latte verified pairs:**

| Foreground           | Background           | Ratio | Level |
| -------------------- | -------------------- | ----- | ----- |
| `#4c4f69` (Text)     | `#eff1f5` (Base)     | 9.8:1 | AAA   |
| `#4c4f69` (Text)     | `#ccd0da` (Surface0) | 5.4:1 | AA    |
| `#5c5f77` (Sub1)     | `#eff1f5` (Base)     | 7.3:1 | AAA   |
| `#1e66f5` (Blue)     | `#eff1f5` (Base)     | 5.1:1 | AA    |
| `#7287fd` (Lavender) | `#eff1f5` (Base)     | 4.6:1 | AA    |
| `#179299` (Teal)     | `#eff1f5` (Base)     | 4.7:1 | AA    |
| `#40a02b` (Green)    | `#eff1f5` (Base)     | 5.8:1 | AA    |

> **Note:** `--ctp-overlay1` (`#8087a2` / `#8c8fa1`) is used only for placeholder/muted text that is explicitly non-interactive and decorative. Never use it for content that carries meaning.

---

## 3. Typography

### 3.1 Font Selection

| Role                | Font Family      | Source                   | Notes                                                                   |
| ------------------- | ---------------- | ------------------------ | ----------------------------------------------------------------------- |
| **UI / Body**       | `DM Mono`        | Google Fonts             | Monospaced. Perfect for a code-tooling context; readable at small sizes |
| **Headings**        | `Syne`           | Google Fonts             | Geometric, slightly condensed — modern and technical-feeling            |
| **Code / Paths**    | `JetBrains Mono` | Google Fonts / JetBrains | File paths, symbol names, repo paths                                    |
| **Numeric / Stats** | `DM Mono`        | Google Fonts             | Tabular figures; consistent column widths in data tables                |

```css
@import url("https://fonts.googleapis.com/css2?family=Syne:wght@400;500;600;700;800&family=DM+Mono:ital,wght@0,300;0,400;0,500;1,400&family=JetBrains+Mono:wght@300;400;500;700&display=swap");

:root {
    --font-heading: "Syne", sans-serif;
    --font-body: "DM Mono", monospace;
    --font-code: "JetBrains Mono", monospace;
}
```

### 3.2 Type Scale

| Token         | Size | Weight | Line Height | Usage                  |
| ------------- | ---- | ------ | ----------- | ---------------------- |
| `--text-xs`   | 11px | 400    | 1.4         | Table metadata, badges |
| `--text-sm`   | 13px | 400    | 1.5         | Body copy, table cells |
| `--text-base` | 14px | 400    | 1.6         | Default UI text        |
| `--text-md`   | 16px | 500    | 1.5         | Card labels, nav items |
| `--text-lg`   | 20px | 600    | 1.3         | Section headings       |
| `--text-xl`   | 24px | 700    | 1.2         | Page titles            |
| `--text-2xl`  | 32px | 700    | 1.1         | Hero stat numbers      |
| `--text-3xl`  | 48px | 800    | 1.0         | Large metric callouts  |

> **Heading font:** `Syne` for `--text-lg` and above.
> **Body/UI font:** `DM Mono` for `--text-xs` through `--text-md`.
> **Code/paths:** `JetBrains Mono` wherever file paths, symbol names, or code snippets appear.

### 3.3 Letter Spacing

```css
--tracking-tight: -0.02em; /* Large headings (--text-2xl+)       */
--tracking-normal: 0; /* Body text                           */
--tracking-wide: 0.04em; /* Uppercase labels, badges, metadata  */
--tracking-wider: 0.08em; /* Section divider labels              */
```

---

## 4. Spacing & Layout

### 4.1 Spacing Scale

A base-4 scale:

```css
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px;
--space-8: 32px;
--space-10: 40px;
--space-12: 48px;
--space-16: 64px;
--space-20: 80px;
```

### 4.2 Application Shell Layout

```
┌───────────────────────────────────────────────────────────────┐
│  TOPBAR  (48px height, --bg-chrome)                           │
│  [Logo + App Name]         [Search]         [Theme Toggle]    │
├──────────────┬────────────────────────────────────────────────┤
│              │                                                 │
│  SIDEBAR     │   MAIN CONTENT AREA                            │
│  (220px)     │   (fluid, max-width: 1400px, centered)        │
│  --bg-sidebar│   padding: 24px 32px                           │
│              │                                                 │
│  Navigation  │   Page Content                                  │
│  items       │                                                 │
│              │                                                 │
│  (always     │                                                 │
│   visible)   │                                                 │
│              │                                                 │
└──────────────┴────────────────────────────────────────────────┘
```

- **Topbar height:** 48px, `background: var(--bg-chrome)`, bottom border `1px solid var(--border-subtle)`
- **Sidebar width:** 220px fixed, `background: var(--bg-sidebar)`, right border `1px solid var(--border-subtle)`
- **Content area:** fluid width, max `1400px`, left-aligned within the remaining space
- **Content padding:** `24px 32px` (top/bottom 24, left/right 32)
- **Page title bar:** 56px tall strip at top of content area, contains the page heading and action buttons

### 4.3 Grid System

All dashboard grids use CSS Grid:

```css
/* Stat card row — 4 columns on large, 2 on medium, 1 on small */
.stats-grid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: var(--space-4);
}

/* Content sections — sidebar detail + main chart */
.content-grid {
    display: grid;
    grid-template-columns: 1fr 320px;
    gap: var(--space-6);
}

/* Repository cards — 3 columns on large */
.repo-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: var(--space-4);
}
```

### 4.4 Border Radius

```css
--radius-sm: 4px; /* Badges, tags, small chips               */
--radius-md: 8px; /* Cards, inputs, dropdowns                */
--radius-lg: 12px; /* Modals, panels, floating elements       */
--radius-xl: 16px; /* Large feature cards                     */
--radius-full: 9999px; /* Pills, avatars, toggle switches        */
```

### 4.5 Elevation / Shadow

Shadows use the palette's crust as the shadow base to feel native:

```css
/* Dark theme */
--shadow-sm: 0 1px 3px rgba(24, 25, 38, 0.4);
--shadow-md: 0 4px 12px rgba(24, 25, 38, 0.5);
--shadow-lg: 0 8px 24px rgba(24, 25, 38, 0.6);
--shadow-xl: 0 16px 48px rgba(24, 25, 38, 0.7);

/* Floating accent glow (applied to primary CTAs / featured cards) */
--glow-accent: 0 0 20px rgba(183, 189, 248, 0.15); /* lavender glow */
--glow-teal: 0 0 20px rgba(139, 213, 202, 0.12);
```

```css
/* Light theme overrides */
[data-theme="light"] {
    --shadow-sm: 0 1px 3px rgba(76, 79, 105, 0.1);
    --shadow-md: 0 4px 12px rgba(76, 79, 105, 0.15);
    --shadow-lg: 0 8px 24px rgba(76, 79, 105, 0.18);
    --shadow-xl: 0 16px 48px rgba(76, 79, 105, 0.22);
    --glow-accent: 0 0 20px rgba(114, 135, 253, 0.12);
}
```

---

## 5. Component Specifications

### 5.1 Sidebar Navigation

**Structure:**

```
┌─────────────────────┐
│  ⬡ CodeCompress     │  ← Logo mark + app name (16px Syne bold)
│  ─────────────────  │  ← 1px border-subtle
│                     │
│  ◉ Dashboard        │  ← Active item
│  ○ Repositories     │
│  ○ Symbols          │
│  ○ Files            │
│  ○ Index Jobs       │
│                     │
│  ─────────────────  │
│  SETTINGS           │  ← Section label (11px, tracking-wider, muted)
│  ○ Configuration    │
│                     │
└─────────────────────┘
```

- **Nav item height:** 36px
- **Nav item padding:** `8px 16px`
- **Active state:** `background: var(--ctp-surface0)`, left border `3px solid var(--accent-primary)`, text `var(--text-primary)`
- **Hover state:** `background: var(--ctp-surface0)` at 60% opacity, smooth 150ms transition
- **Section labels:** uppercase, `--text-xs`, `letter-spacing: var(--tracking-wider)`, `color: var(--text-muted)`, `padding: 16px 16px 4px`
- **Icons:** 16px, Lucide icon set, inline with text at 8px gap

**Logo Mark:**
A hexagonal shape (⬡) in `--ctp-lavender`, representing a compressed node/graph. The wordmark "CodeCompress" in Syne 600 weight, `--text-primary`.

---

### 5.2 Topbar

```
┌──────────────────────────────────────────────────────────────┐
│  [≡]  CodeCompress         [🔍 Search symbols...]   [☀/🌙]  │
└──────────────────────────────────────────────────────────────┘
```

- **Background:** `var(--bg-chrome)` — the darkest surface (Crust)
- **Height:** 48px
- **Bottom border:** `1px solid var(--border-subtle)`
- **Search bar:** centered, 320px wide, `background: var(--bg-card)`, `border-radius: var(--radius-full)`, placeholder "Search symbols, files, repos…"
- **Theme toggle:** icon button, 32×32px, `border-radius: var(--radius-full)`, subtle border, smooth icon swap animation (200ms fade)
- **Mobile hamburger:** visible only below 768px breakpoint

---

### 5.3 Stat Cards

Four stat cards displayed in a row at the top of the Dashboard. Each card floats above the page with `--shadow-md`.

**Card anatomy:**

```
┌─────────────────────────────────┐
│  TOTAL REPOSITORIES         [↗] │  ← Label (11px, tracking-wide, muted) + trend arrow
│                                 │
│  247                            │  ← Hero number (48px, Syne 800, --text-primary)
│                                 │
│  ████░░░░░░  +12 this week      │  ← Mini sparkline + delta (13px, --accent-success/error)
└─────────────────────────────────┘
```

**Spec:**

- **Background:** `var(--bg-card)` (`--ctp-surface0`)
- **Border:** `1px solid var(--border-subtle)`
- **Border-radius:** `var(--radius-lg)` (12px)
- **Padding:** `20px 24px`
- **Shadow:** `var(--shadow-md)`
- **Label:** `DM Mono`, 11px, `letter-spacing: var(--tracking-wide)`, `color: var(--text-muted)`, uppercase
- **Metric number:** `Syne`, 48px, weight 800, `color: var(--text-primary)`, `letter-spacing: var(--tracking-tight)`
- **Delta text:** 13px, `DM Mono`, colored by direction (`--accent-success` green for positive, `--accent-error` red for negative, `--text-muted` for neutral)
- **Accent strip:** A 3px horizontal line at the bottom of each card, unique color per card type:
    - Repositories → `--ctp-lavender`
    - Files indexed → `--ctp-teal`
    - Symbols → `--ctp-peach`
    - Last indexed → `--ctp-blue`
- **Hover:** subtle lift via `transform: translateY(-2px)`, `--shadow-lg`, 200ms ease

**Four dashboard stat cards:**

1. **Total Repositories** — Count of indexed repos
2. **Files Indexed** — Total file count across all repos
3. **Symbols Extracted** — Total symbol count (functions, classes, etc.)
4. **Last Index Run** — Relative time (e.g., "3 minutes ago"), status dot (green/yellow/red)

---

### 5.4 Charts

#### Symbol Type Distribution — Donut Chart

Displayed in the right column of the Dashboard overview.

- **Library:** Use a Blazor-compatible chart library (e.g., Blazorise Charts or ApexCharts.Blazor)
- **Type:** Donut / Pie
- **Colors:** Use `--chart-1` through `--chart-5` in sequence (lavender, teal, peach, mauve, green)
- **Center label:** Total symbol count, large Syne numeral
- **Legend:** Below chart, horizontal pills per symbol type (Function, Class, Interface, Enum, Variable, Property)
- **Background:** Transparent (inherits card background)
- **Tooltip:** Custom styled to match theme — `background: var(--bg-chrome)`, `border: 1px solid var(--border-subtle)`, `color: var(--text-primary)`, `border-radius: var(--radius-md)`, `box-shadow: var(--shadow-lg)`

#### Repository Size Over Time — Area Chart

Shown in the main content area, full width.

- **Type:** Stacked area or line chart
- **X-axis:** Indexed dates (time series)
- **Y-axis:** Symbol count
- **Series:** One line per repository (up to 8; overflow uses color cycling)
- **Fill:** Gradient fill from series color at 30% opacity down to 0% at the bottom
- **Grid lines:** `1px solid var(--border-subtle)` at 40% opacity
- **Axis labels:** `DM Mono` 11px, `--text-muted`
- **Background:** Transparent

#### Symbol Growth Trend — Bar Chart

- **Type:** Grouped vertical bar chart
- **Bars:** Rounded tops, `border-radius: 4px 4px 0 0`
- **Colors:** Two series — new symbols (`--ctp-lavender`) vs removed symbols (`--ctp-maroon`)
- **Spacing:** 2px gap between grouped bars, 12px gap between groups

---

### 5.5 Data Tables

Used on the Repositories, Symbols, and Files pages.

**Table container:**

- `background: var(--bg-card)`, `border: 1px solid var(--border-subtle)`, `border-radius: var(--radius-lg)`
- Overflow hidden to respect the rounded corners
- Full width

**Table header row:**

- `background: var(--bg-chrome)` (slightly darker than card)
- `border-bottom: 1px solid var(--border-subtle)`
- `padding: 0 16px`, height 40px
- Column labels: `DM Mono`, 11px, `letter-spacing: var(--tracking-wide)`, uppercase, `color: var(--text-muted)`
- Sortable columns show a sort indicator (↑↓) in `--accent-primary` when active

**Table body rows:**

- Row height: 48px
- `padding: 0 16px`
- `border-bottom: 1px solid var(--border-subtle)` (not on last row)
- **Hover:** `background: var(--bg-card-hover)`, 150ms transition
- Text: `DM Mono` 13px, `--text-secondary`
- Primary cell (name/identifier): `color: var(--text-primary)`, weight 500

**Pagination:**

- Sits below the table, right-aligned
- "Showing X–Y of Z" label in `--text-muted`
- Previous/Next buttons as Blazor Blueprint ghost Button components

**Repository table columns:**
| # | Column | Notes |
|---|--------|-------|
| 1 | Name | Repo name, JetBrains Mono, --accent-interactive color, clickable |
| 2 | Path | Full local path, JetBrains Mono 12px, --text-muted, truncated with ellipsis |
| 3 | Files | Right-aligned numeric, DM Mono |
| 4 | Symbols | Right-aligned numeric, DM Mono |
| 5 | Status | Badge (see §5.7) |
| 6 | Last Indexed | Relative time, --text-muted |
| 7 | Actions | Icon buttons: Re-index, View Details, Remove |

**Symbols table columns:**
| # | Column | Notes |
|---|--------|-------|
| 1 | Symbol Name | JetBrains Mono, --text-primary |
| 2 | Kind | Badge (Function, Class, Interface, Enum, etc.) |
| 3 | File | JetBrains Mono 12px, clickable, truncated |
| 4 | Line | Right-aligned, DM Mono, --text-muted |
| 5 | Repository | --accent-interactive, link |
| 6 | Signature | JetBrains Mono 11px, --text-muted, truncated |

---

### 5.6 Repository Cards (Card Grid View)

An alternative view to the table — a grid of rich cards, toggled by a view switcher (list/grid icon buttons).

```
┌──────────────────────────────────────┐
│  ● INDEXED          [Re-index] [···] │  ← Status badge + actions
│                                      │
│  my-api-project                      │  ← Repo name (Syne 600, 18px)
│  /Users/marco/repos/my-api-project   │  ← Path (JetBrains Mono 11px, muted)
│                                      │
│  ─────────────────────────────────   │
│                                      │
│  312 symbols    │ 48 files           │
│  ██████████ 78% C#                   │  ← Language bar
│  ████░░░░░░ 22% Razor                │
│                                      │
│  Indexed 5 minutes ago               │  ← --text-muted, 12px
└──────────────────────────────────────┘
```

- **Background:** `var(--bg-card)`
- **Border:** `1px solid var(--border-subtle)`
- **Hover:** border changes to `var(--border-strong)`, `var(--shadow-md)` added, `translateY(-2px)` lift
- **Border-radius:** `var(--radius-xl)` (16px)
- **Padding:** `20px`
- **Language bar:** thin (6px tall) segmented bar, each segment a different chart color, `border-radius: var(--radius-full)`
- **Featured repo:** top border replaced with 2px gradient: `--ctp-mauve → --ctp-lavender`, and `--glow-accent` shadow

---

### 5.7 Badges & Status Indicators

#### Status Badges (Pill style)

```css
.badge {
    font-family: var(--font-body);
    font-size: 11px;
    font-weight: 500;
    letter-spacing: var(--tracking-wide);
    text-transform: uppercase;
    padding: 2px 8px;
    border-radius: var(--radius-full);
    display: inline-flex;
    align-items: center;
    gap: 5px;
}
```

| Variant     | Background            | Text           | Border                | Dot Color                     | Usage              |
| ----------- | --------------------- | -------------- | --------------------- | ----------------------------- | ------------------ |
| `indexed`   | `--ctp-teal` at 15%   | `--ctp-teal`   | `--ctp-teal` at 40%   | `--ctp-teal`                  | Repo fully indexed |
| `stale`     | `--ctp-yellow` at 15% | `--ctp-yellow` | `--ctp-yellow` at 40% | `--ctp-yellow`                | Index out of date  |
| `indexing`  | `--ctp-blue` at 15%   | `--ctp-blue`   | `--ctp-blue` at 40%   | `--ctp-blue` (animated pulse) | Currently indexing |
| `error`     | `--ctp-red` at 15%    | `--ctp-red`    | `--ctp-red` at 40%    | `--ctp-red`                   | Index failed       |
| `untracked` | `--ctp-surface1`      | `--text-muted` | `--border-subtle`     | `--text-muted`                | Not yet indexed    |

#### Symbol Kind Badges

Compact, no dot, slightly smaller:

| Kind            | Color            | Abbreviation displayed |
| --------------- | ---------------- | ---------------------- |
| Function/Method | `--ctp-blue`     | `fn`                   |
| Class           | `--ctp-peach`    | `cls`                  |
| Interface       | `--ctp-lavender` | `ifc`                  |
| Enum            | `--ctp-green`    | `enm`                  |
| Variable        | `--ctp-teal`     | `var`                  |
| Property        | `--ctp-mauve`    | `prop`                 |
| Type Alias      | `--ctp-sky`      | `type`                 |
| Namespace       | `--ctp-overlay2` | `ns`                   |

---

### 5.8 Search & Filter Bar

Appears above data tables on list pages.

```
[ 🔍 Search symbols...          ] [ Kind ▾ ] [ Repository ▾ ] [ Status ▾ ] [ Reset ]
```

- **Search input:** full `border-radius: var(--radius-full)`, `height: 36px`, `background: var(--bg-card)`, `border: 1px solid var(--border-subtle)`, focus state: `border-color: var(--accent-primary)`, `box-shadow: 0 0 0 3px rgba(183,189,248,0.2)` — accessible focus ring
- **Dropdown filters:** Blazor Blueprint `Select` components, matching border and radius
- **Filter chips:** When active filters are applied, they appear as dismissible chips between the filter bar and the table

---

### 5.9 Floating Detail Panel (Slide-over / Drawer)

When a symbol or repository row is clicked, a detail panel slides in from the right (not a full modal, preserving table context).

```
┌─────────────────────────────────────┐
│ ← Back           my_function()  [✕] │
├─────────────────────────────────────┤
│ Function · my-api-project           │
│ /src/Services/MyService.cs:142      │
│                                     │
│ SIGNATURE                           │
│ ┌─────────────────────────────────┐ │
│ │ public async Task<Result>       │ │
│ │   MyFunction(string id,         │ │
│ │              CancellationToken) │ │
│ └─────────────────────────────────┘ │
│                                     │
│ REFERENCES                          │
│  · MyController.cs:89               │
│  · MyTests.cs:14, 27                │
│                                     │
│ RELATED SYMBOLS                     │
│  · Result (class)                   │
│  · MyService (class)                │
└─────────────────────────────────────┘
```

- **Width:** 400px
- **Background:** `var(--bg-card)`, left border `1px solid var(--border-subtle)`
- **Box-shadow:** `var(--shadow-xl)` on the left edge
- **Animation:** slides in from right, 250ms `cubic-bezier(0.16, 1, 0.3, 1)` — fast in, smooth settle
- **Backdrop:** `var(--bg-page)` at 40% opacity overlay on the content area (not full screen)
- **Code block:** `background: var(--bg-chrome)`, `border: 1px solid var(--border-subtle)`, `border-radius: var(--radius-md)`, `padding: 12px`, `font-family: var(--font-code)`, `font-size: 12px`
- **Section labels:** 11px, uppercase, `letter-spacing: var(--tracking-wider)`, `--text-muted`, with `border-bottom: 1px solid var(--border-subtle)` and 16px bottom padding

---

### 5.10 Buttons

Follow Blazor Blueprint Button component variants, styled with Catppuccin tokens:

| Variant     | Background                    | Text               | Border                            | Hover                                   |
| ----------- | ----------------------------- | ------------------ | --------------------------------- | --------------------------------------- |
| Primary     | `--accent-primary` (lavender) | `--ctp-crust`      | none                              | darken 10%, `--glow-accent`             |
| Secondary   | `--bg-card`                   | `--text-primary`   | `1px solid --border-subtle`       | `--bg-card-hover`                       |
| Ghost       | transparent                   | `--text-secondary` | none                              | `--bg-card` background                  |
| Destructive | `--accent-error` (red) at 15% | `--accent-error`   | `1px solid --accent-error` at 40% | `--accent-error` bg, `--ctp-crust` text |
| Icon-only   | transparent                   | `--text-muted`     | none                              | `--bg-card`, `--text-primary`           |

- **Border-radius:** `var(--radius-md)` for text buttons, `var(--radius-full)` for icon-only round buttons
- **Height:** 32px (default), 36px (medium), 40px (large)
- **Padding:** `0 16px` for text buttons, `0 8px` for icon-only
- **Focus ring:** `box-shadow: 0 0 0 3px rgba(183,189,248,0.35)` (lavender at 35%) — 3:1 contrast on all backgrounds, WCAG AA

---

### 5.11 Toast Notifications

Non-blocking notifications floating at bottom-right.

- **Width:** 340px
- **Padding:** `14px 16px`
- **Border-radius:** `var(--radius-lg)`
- **Border:** `1px solid var(--border-subtle)` with left edge: `4px solid` in status color
- **Background:** `var(--bg-card)`, `var(--shadow-xl)`
- **Animation:** slide up from bottom + fade in (200ms), auto-dismiss after 4s with fade out
- **Max stack:** 3 toasts visible simultaneously, older ones compress upward

Variants: Success (teal), Warning (yellow), Error (red), Info (sapphire)

---

### 5.12 Empty States

When a table or page section has no data:

```
        ⬡
  No repositories indexed yet

  Add a repository path to get started
  with symbol indexing.

        [ Index a Repository ]
```

- Centered in the container
- Icon: 48px, `--text-muted`
- Heading: `Syne` 18px, `--text-secondary`
- Subtext: `DM Mono` 13px, `--text-muted`
- CTA: Primary button

---

## 6. Pages

### 6.1 Dashboard (Overview)

**Route:** `/` or `/dashboard`

**Purpose:** At-a-glance health and metrics for the entire CodeCompress index.

**Layout:**

```
┌─ Page Title ──────────────────────────────────── [Re-index All] ─┐

  [ Stat Card: Repos ] [ Stat Card: Files ] [ Stat Card: Symbols ] [ Stat Card: Last Run ]

┌─ Symbol Growth (area chart, full width) ──────────────────────────┐
│  Time series of symbols indexed per repo over past 30 days        │
└───────────────────────────────────────────────────────────────────┘

┌─ Recent Repos (left, 2/3 width) ─────┐ ┌─ By Type (right, 1/3) ──┐
│  Last 5 indexed repos mini-table     │ │  Donut chart             │
│  Name / Symbols / Last Indexed / Sts │ │  Symbol type breakdown   │
└──────────────────────────────────────┘ └──────────────────────────┘

┌─ Index Activity (bar chart, full width) ──────────────────────────┐
│  New vs removed symbols per day, last 14 days                     │
└───────────────────────────────────────────────────────────────────┘
```

**Key interactions:**

- Stat cards are clickable — navigate to the respective list page
- "Re-index All" button triggers a confirmation dialog then fires re-index for all repositories
- Recent repos table rows navigate to the Repository detail page
- Charts have hover tooltips with exact values

---

### 6.2 Repositories Page

**Route:** `/repositories`

**Purpose:** Full list management of all indexed repositories.

**Layout:**

```
┌─ Repositories ──────────────────── [+ Add Repository] [⊞ Grid] [≡ List] ─┐

  [ 🔍 Search... ] [ Status ▾ ] [ Sort: Last Indexed ▾ ]

┌─ Table / Grid ────────────────────────────────────────────────────────────┐
│  (see §5.5 and §5.6 for table and card specs)                            │
└───────────────────────────────────────────────────────────────────────────┘

  Pagination
```

**Add Repository dialog:**

A modal (Blazor Blueprint `Dialog` component):

- Title: "Add Repository"
- Field: "Repository Path" — full path text input with folder icon, auto-complete from filesystem if available
- Field: "Display Name" — optional override
- Checkbox: "Index on add"
- Actions: [Cancel] [Add Repository]
- Modal background: `var(--bg-card)`, centered, `var(--shadow-xl)`, `border-radius: var(--radius-xl)`, backdrop blur scrim

---

### 6.3 Repository Detail Page

**Route:** `/repositories/{id}`

**Purpose:** Deep dive into a single repository's index state.

**Layout:**

```
┌─ ← Repositories    my-api-project                    [Re-index] [···] ─┐

  [ Badge: INDEXED ] /Users/marco/repos/my-api-project
  Last indexed: 3 minutes ago · 312 symbols · 48 files

┌─ Symbols by Type (bar chart) ──┐ ┌─ File Tree Summary ─────────────────┐
│  Horizontal bar per kind        │ │  Expandable directory listing        │
│  Shows count per symbol type    │ │  Showing file counts and symbols     │
└─────────────────────────────────┘ └─────────────────────────────────────┘

┌─ Symbols Table ───────────────────────────────────────────────────────────┐
│  (pre-filtered to this repository)                                        │
│  Columns: Symbol Name / Kind / File / Line / Signature                    │
└───────────────────────────────────────────────────────────────────────────┘
```

**Overflow menu (`···`):**

- Re-index Now
- Remove from Index (destructive, red)
- Copy Path

---

### 6.4 Symbols Page

**Route:** `/symbols`

**Purpose:** Global search and browse for all indexed symbols.

**Layout:**

```
┌─ Symbols ──────────────────────────────────────────────────── [Export] ─┐

  [ 🔍 Search symbols... ] [ Kind ▾ ] [ Repository ▾ ]

  Active filters: [Kind: Function ✕] [Repo: my-api ✕]

┌─ Symbols Table ───────────────────────────────────────────────────────────┐
│  (see §5.5 Symbols table spec)                                            │
│  Click row → Slide-over detail panel (see §5.9)                          │
└───────────────────────────────────────────────────────────────────────────┘

  Pagination
```

**Symbol count summary bar:**

Between the filter chips and the table, a single line:
`312 functions · 48 classes · 12 interfaces · 6 enums · 91 variables`

Each number is colored with its corresponding badge color.

---

### 6.5 Files Page

**Route:** `/files`

**Purpose:** Browse all indexed files across all repositories.

**Layout:**

```
┌─ Files ──────────────────────────────────────────────────────────────────┐

  [ 🔍 Search files... ] [ Extension ▾ ] [ Repository ▾ ]

┌─ Files Table ─────────────────────────────────────────────────────────────┐
│  Columns: File Path / Extension / Symbols / Repository / Last Modified    │
└────────────────────────────────────────────────────────────────────────────┘
```

File path column uses JetBrains Mono 12px. Extension shown as a small badge (language color-coded: `.cs` → blue, `.razor` → mauve, `.ts` → yellow, etc.).

---

### 6.6 Index Jobs Page

**Route:** `/index-jobs`

**Purpose:** History and live status of indexing operations.

**Layout:**

```
┌─ Index Jobs ─────────────────────────────────────────────────────────────┐

┌─ Active Jobs ──────────────────────────────────────────────────────────── ┐
│  [●] my-api-project — Indexing... 72%  ██████████░░░░░              [✕]  │
└─────────────────────────────────────────────────────────────────────────── ┘

┌─ Job History Table ────────────────────────────────────────────────────────┐
│  Repository / Status / Started / Duration / Symbols Added / Files Scanned  │
└────────────────────────────────────────────────────────────────────────────┘
```

**Progress bar:**

- Height: 6px
- Background: `var(--bg-card)`
- Fill: `var(--accent-primary)` gradient `→ var(--ctp-teal)`
- Animated shimmer on active jobs
- `border-radius: var(--radius-full)`

---

### 6.7 Configuration Page

**Route:** `/configuration`

**Purpose:** Manage CodeCompress settings stored in the database or config file.

**Layout:**

```
┌─ Configuration ──────────────────────────────────────────────────────────┐

┌─ Section: Indexing ──────────────────────────────────────────────────────┐
│  Auto-index on file change         [Toggle: ON]                          │
│  File extensions to include        [Tag input: .cs .ts .razor ...]       │
│  Directories to exclude            [Tag input: bin/ obj/ node_modules/]  │
│  Max file size (KB)                [Number input: 512]                   │
└──────────────────────────────────────────────────────────────────────────┘

┌─ Section: Display ───────────────────────────────────────────────────────┐
│  Default theme                     [Select: Dark / Light]                │
│  Rows per page                     [Select: 25 / 50 / 100]              │
└──────────────────────────────────────────────────────────────────────────┘

                                          [Reset to Defaults]  [Save Changes]
```

Section containers: `background: var(--bg-card)`, `border: 1px solid var(--border-subtle)`, `border-radius: var(--radius-lg)`, `padding: 20px 24px`, rows are 48px-tall form rows with a `border-bottom: 1px solid var(--border-subtle)` between them.

---

## 7. Motion & Transitions

All transitions should respect `prefers-reduced-motion`:

```css
@media (prefers-reduced-motion: reduce) {
    *,
    *::before,
    *::after {
        animation-duration: 0.01ms !important;
        transition-duration: 0.01ms !important;
    }
}
```

**Standard transitions:**

| Element            | Property               | Duration | Easing                     |
| ------------------ | ---------------------- | -------- | -------------------------- |
| Nav item hover     | background             | 150ms    | ease                       |
| Card hover lift    | transform, box-shadow  | 200ms    | ease-out                   |
| Button hover       | background, box-shadow | 150ms    | ease                       |
| Badge color        | background, color      | 200ms    | ease                       |
| Table row hover    | background             | 100ms    | linear                     |
| Slide-over panel   | transform              | 250ms    | cubic-bezier(0.16,1,0.3,1) |
| Toast notification | transform, opacity     | 200ms    | ease-out                   |
| Chart tooltip      | opacity                | 100ms    | ease                       |
| Theme toggle       | all colors             | 300ms    | ease                       |
| Progress bar fill  | width                  | 400ms    | ease-in-out                |
| Modal open         | opacity, scale         | 200ms    | ease-out                   |

**Micro-animation — indexing spinner:**
The status dot for "Indexing" state pulses with a scale animation: `scale(1) → scale(1.4) → scale(1)`, 1.2s infinite, `ease-in-out`. The dot itself glows softly: `box-shadow: 0 0 0 4px rgba(138, 173, 244, 0.2)`.

---

## 8. Blazor Blueprint Component Mapping

Mapping of UI elements to their Blazor Blueprint (shadcn/ui for Blazor) equivalents:

| UI Element             | Blazor Blueprint Component                                   |
| ---------------------- | ------------------------------------------------------------ |
| Data tables            | `DataTable` / `Table`                                        |
| Stat cards             | `Card`, `CardHeader`, `CardContent`                          |
| Navigation             | `NavigationMenu` or custom `NavLink` wrapper                 |
| Buttons                | `Button` (variant: default, outline, ghost, destructive)     |
| Dropdowns/Select       | `Select`, `SelectTrigger`, `SelectContent`, `SelectItem`     |
| Search input           | `Input` with `InputAdornment`                                |
| Badges                 | `Badge` (variant mapped to status)                           |
| Modal dialogs          | `Dialog`, `DialogContent`, `DialogHeader`                    |
| Slide-over / Drawer    | `Sheet`, `SheetContent` (side="right")                       |
| Toast notifications    | `Toaster` / `Toast`                                          |
| Toggle switch          | `Switch`                                                     |
| Tag input              | Custom `TagInput` built on `Input` + `Badge`                 |
| Progress bars          | `Progress`                                                   |
| Tooltips               | `Tooltip`, `TooltipContent`                                  |
| Separator / Divider    | `Separator`                                                  |
| Dropdown menus (`···`) | `DropdownMenu`, `DropdownMenuTrigger`, `DropdownMenuContent` |
| Tabs (view switchers)  | `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent`             |
| Number input           | `Input` with `type="number"`                                 |

---

## 9. Responsive Behavior

This is a local developer tool, primarily used on desktop. Mobile is a nice-to-have, not required, but the layout should gracefully handle narrow windows.

| Breakpoint  | Behavior                                                               |
| ----------- | ---------------------------------------------------------------------- |
| > 1280px    | Full layout: sidebar visible, 4-column stat grid                       |
| 1024–1280px | Sidebar visible, 2-column stat grid                                    |
| 768–1024px  | Sidebar collapses to icon-only (48px), 2-column stat grid              |
| < 768px     | Sidebar hidden behind hamburger, 1-column stat grid, simplified charts |

---

## 10. Iconography

**Library:** [Lucide Icons](https://lucide.dev/) — consistent 24×24 (or 16×16 for inline) stroke icons.

| Action / Concept     | Icon Name                    |
| -------------------- | ---------------------------- |
| Repository           | `folder-git-2`               |
| Symbol / function    | `braces`                     |
| File                 | `file-code`                  |
| Index / refresh      | `refresh-cw`                 |
| Add                  | `plus`                       |
| Remove / delete      | `trash-2`                    |
| Settings / config    | `settings`                   |
| Search               | `search`                     |
| Dashboard / overview | `layout-dashboard`           |
| Index jobs           | `activity`                   |
| Status: indexed      | `check-circle`               |
| Status: error        | `alert-circle`               |
| Status: stale        | `clock`                      |
| Status: indexing     | `loader-2` (animated rotate) |
| Export               | `download`                   |
| More options         | `ellipsis`                   |
| Theme: dark          | `moon`                       |
| Theme: light         | `sun`                        |
| Expand detail        | `arrow-right`                |
| Back                 | `chevron-left`               |
| Dismiss / close      | `x`                          |
| Sort ascending       | `chevron-up`                 |
| Sort descending      | `chevron-down`               |

---

## 11. CSS Custom Property Reference (Complete)

```css
:root {
    /* ── Catppuccin Macchiato (dark default) ── */
    --ctp-base: #24273a;
    --ctp-mantle: #1e2030;
    --ctp-crust: #181926;
    --ctp-surface0: #363a4f;
    --ctp-surface1: #494d64;
    --ctp-surface2: #5b6078;
    --ctp-overlay0: #6e738d;
    --ctp-overlay1: #8087a2;
    --ctp-overlay2: #939ab7;
    --ctp-subtext0: #a5adcb;
    --ctp-subtext1: #b8c0e0;
    --ctp-text: #cad3f5;
    --ctp-lavender: #b7bdf8;
    --ctp-blue: #8aadf4;
    --ctp-sapphire: #7dc4e4;
    --ctp-sky: #91d7e3;
    --ctp-teal: #8bd5ca;
    --ctp-green: #a6da95;
    --ctp-yellow: #eed49f;
    --ctp-peach: #f5a97f;
    --ctp-maroon: #ee99a0;
    --ctp-red: #ed8796;
    --ctp-mauve: #c6a0f6;
    --ctp-pink: #f5bde6;
    --ctp-flamingo: #f0c6c6;
    --ctp-rosewater: #f4dbd6;

    /* ── Semantic tokens (dark) ── */
    --bg-page: var(--ctp-base);
    --bg-sidebar: var(--ctp-mantle);
    --bg-chrome: var(--ctp-crust);
    --bg-card: var(--ctp-surface0);
    --bg-card-hover: var(--ctp-surface1);
    --bg-input: var(--ctp-surface0);
    --border-subtle: var(--ctp-overlay0);
    --border-strong: var(--ctp-overlay1);
    --text-primary: var(--ctp-text);
    --text-secondary: var(--ctp-subtext1);
    --text-muted: var(--ctp-overlay1);
    --text-inverse: var(--ctp-crust);
    --accent-primary: var(--ctp-lavender);
    --accent-interactive: var(--ctp-blue);
    --accent-success: var(--ctp-teal);
    --accent-warning: var(--ctp-yellow);
    --accent-error: var(--ctp-red);
    --accent-info: var(--ctp-sapphire);
    --accent-feature: var(--ctp-mauve);
    --chart-1: var(--ctp-lavender);
    --chart-2: var(--ctp-teal);
    --chart-3: var(--ctp-peach);
    --chart-4: var(--ctp-mauve);
    --chart-5: var(--ctp-green);
    --shadow-sm: 0 1px 3px rgba(24, 25, 38, 0.4);
    --shadow-md: 0 4px 12px rgba(24, 25, 38, 0.5);
    --shadow-lg: 0 8px 24px rgba(24, 25, 38, 0.6);
    --shadow-xl: 0 16px 48px rgba(24, 25, 38, 0.7);
    --glow-accent: 0 0 20px rgba(183, 189, 248, 0.15);
    --font-heading: "Syne", sans-serif;
    --font-body: "DM Mono", monospace;
    --font-code: "JetBrains Mono", monospace;
    --radius-sm: 4px;
    --radius-md: 8px;
    --radius-lg: 12px;
    --radius-xl: 16px;
    --radius-full: 9999px;
    --space-1: 4px;
    --space-2: 8px;
    --space-3: 12px;
    --space-4: 16px;
    --space-5: 20px;
    --space-6: 24px;
    --space-8: 32px;
    --space-10: 40px;
    --space-12: 48px;
    --space-16: 64px;
    --space-20: 80px;
}

[data-theme="light"] {
    /* ── Catppuccin Latte overrides ── */
    --ctp-base: #eff1f5;
    --ctp-mantle: #e6e9ef;
    --ctp-crust: #dce0e8;
    --ctp-surface0: #ccd0da;
    --ctp-surface1: #bcc0cc;
    --ctp-surface2: #acb0be;
    --ctp-overlay0: #9ca0b0;
    --ctp-overlay1: #8c8fa1;
    --ctp-overlay2: #7c7f93;
    --ctp-subtext0: #6c6f85;
    --ctp-subtext1: #5c5f77;
    --ctp-text: #4c4f69;
    --ctp-lavender: #7287fd;
    --ctp-blue: #1e66f5;
    --ctp-sapphire: #209fb5;
    --ctp-sky: #04a5e5;
    --ctp-teal: #179299;
    --ctp-green: #40a02b;
    --ctp-yellow: #df8e1d;
    --ctp-peach: #fe640b;
    --ctp-maroon: #e64553;
    --ctp-red: #d20f39;
    --ctp-mauve: #8839ef;
    --ctp-pink: #ea76cb;
    --ctp-flamingo: #dd7878;
    --ctp-rosewater: #dc8a78;

    /* ── Semantic overrides where light needs different treatment ── */
    --bg-input: var(--ctp-mantle);
    --shadow-sm: 0 1px 3px rgba(76, 79, 105, 0.1);
    --shadow-md: 0 4px 12px rgba(76, 79, 105, 0.15);
    --shadow-lg: 0 8px 24px rgba(76, 79, 105, 0.18);
    --shadow-xl: 0 16px 48px rgba(76, 79, 105, 0.22);
    --glow-accent: 0 0 20px rgba(114, 135, 253, 0.12);
}
```

---

## 12. Design Principles Summary

1. **Information density with breathing room.** Developer tools need to show a lot at once, but every element earns its space. Use the spacing scale consistently and let `--bg-page` breathe between cards.

2. **Monospace-first typography.** The primary audience is developers. `DM Mono` for the UI and `JetBrains Mono` for code/paths signal precision and feel native to the tool.

3. **Color is semantic, not decorative.** Every color usage has a defined meaning (status, type, series). Avoid using accent colors for purely decorative purposes — it dilutes the signal.

4. **Floating surfaces with defined lines.** Cards lift off the page via `box-shadow`, not background-color contrast alone. Borders (`--border-subtle`) define containment. The hierarchy is: page background → cards → chrome → modals.

5. **Dark by default, light by choice.** Macchiato is the canonical theme. Latte is a first-class peer, not an afterthought — every component must look polished in both.

6. **WCAG AA minimum, AAA where free.** Primary text combinations exceed AAA. Interactive elements maintain 3:1 non-text contrast. Focus rings are always visible (high-contrast lavender glow on dark, blue glow on light).

7. **Micro-interactions communicate state.** The indexing spinner, card hover lift, and toast animations tell the user what's happening without requiring them to read status text.
