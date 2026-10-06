---
name: Apex Enterprise Banking
colors:
  surface: '#f7f9fb'
  surface-dim: '#d8dadc'
  surface-bright: '#f7f9fb'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f4f6'
  surface-container: '#eceef0'
  surface-container-high: '#e6e8ea'
  surface-container-highest: '#e0e3e5'
  on-surface: '#191c1e'
  on-surface-variant: '#42474d'
  inverse-surface: '#2d3133'
  inverse-on-surface: '#eff1f3'
  outline: '#73777e'
  outline-variant: '#c3c7ce'
  surface-tint: '#406182'
  primary: '#001629'
  on-primary: '#ffffff'
  primary-container: '#002b49'
  on-primary-container: '#7293b6'
  inverse-primary: '#a8caef'
  secondary: '#bc000c'
  on-secondary: '#ffffff'
  secondary-container: '#e80f16'
  on-secondary-container: '#fffbff'
  tertiary: '#091426'
  on-tertiary: '#ffffff'
  tertiary-container: '#1e293b'
  on-tertiary-container: '#8590a6'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#cfe5ff'
  primary-fixed-dim: '#a8caef'
  on-primary-fixed: '#001d34'
  on-primary-fixed-variant: '#274969'
  secondary-fixed: '#ffdad5'
  secondary-fixed-dim: '#ffb4aa'
  on-secondary-fixed: '#410001'
  on-secondary-fixed-variant: '#930007'
  tertiary-fixed: '#d8e3fb'
  tertiary-fixed-dim: '#bcc7de'
  on-tertiary-fixed: '#111c2d'
  on-tertiary-fixed-variant: '#3c475a'
  background: '#f7f9fb'
  on-background: '#191c1e'
  surface-variant: '#e0e3e5'
typography:
  display-lg:
    fontFamily: Manrope
    fontSize: 36px
    fontWeight: '700'
    lineHeight: 44px
    letterSpacing: -0.02em
  display-lg-mobile:
    fontFamily: Manrope
    fontSize: 28px
    fontWeight: '700'
    lineHeight: 36px
    letterSpacing: -0.01em
  headline-lg:
    fontFamily: Manrope
    fontSize: 28px
    fontWeight: '600'
    lineHeight: 36px
    letterSpacing: -0.015em
  headline-md:
    fontFamily: Manrope
    fontSize: 22px
    fontWeight: '600'
    lineHeight: 30px
    letterSpacing: -0.01em
  title-lg:
    fontFamily: Manrope
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
  title-sm:
    fontFamily: Manrope
    fontSize: 15px
    fontWeight: '600'
    lineHeight: 20px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
  data-mono:
    fontFamily: JetBrains Mono
    fontSize: 13px
    fontWeight: '500'
    lineHeight: 18px
    letterSpacing: -0.01em
  data-mono-sm:
    fontFamily: JetBrains Mono
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 14px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  gutter: 1rem
  gutter-lg: 1.5rem
  margin: 1rem
  margin-md: 1.5rem
  margin-lg: 2.5rem
  space-2xs: 0.125rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 0.75rem
  space-base: 1rem
  space-lg: 1.5rem
  space-xl: 2rem
  space-2xl: 3rem
---

## Brand & Style

This design system embodies institutional precision, executive trust, and regulatory rigor tailored for modern enterprise and commercial banking. Built around an aesthetic of clean authority, it merges deep navy structural architecture with vibrant, high-intent coral-red accents derived from premier financial identifiers.

The personality balances stoic corporate stability with modern speed:
- **Atmosphere:** Clean, clinical, and reassuringly robust. Surfaces are uncluttered, structured around clear spatial hierarchies and crisp division lines.
- **Visual Stance:** Corporate / Modern with elevated information density. It prioritizes data clarity, auditability, and immediate optical recognition over decorative ornamentation.
- **Target Audience:** Treasury directors, institutional risk analysts, enterprise compliance officers, and commercial banking clients requiring continuous, low-latency financial operations.

## Colors

The color palette establishes rigorous enterprise hierarchy through intentional contrast between structural deep blues and active interaction triggers.

### Palette Architecture
- **Primary Navy (`#002B49` / `#0F172A`):** The institutional anchor used for primary navigation chrome, master buttons, prominent headers, and high-impact structural panels.
- **Secondary Accent Red (`#E30613`):** The authoritative focal accent reserved for critical actions, single-point authorization triggers (such as MBID SSO authorization), high-risk operations, and key visual identifiers.
- **Supporting Slate-Blue (`#1E293B`):** Used for primary typography, active tab states, and heavy iconography.
- **Neutral Surface Foundation (`#F8FAFC` & `#FFFFFF`):** Cool Slate-50 background that prevents eye fatigue across data-heavy operational shifts, framed by clean `#E2E8F0` (Slate-200) micro-borders.

### Semantic Tiers
- **Success (Emerald):** `#059669` fill / `#ECFDF5` container / `#047857` text for cleared settlements, ledger balances, and confirmed transfers.
- **Warning (Amber):** `#D97706` fill / `#FFFBEB` container / `#B45309` text for pending multi-sig reviews, holds, and AML thresholds.
- **Danger (Rose/Red):** `#E11D48` fill / `#FFF1F2` container / `#BE123C` text for failed batches, rejected authorizations, and compliance flags.
- **Info (Blue):** `#2563EB` fill / `#EFF6FF` container / `#1D4ED8` text for system notes, standard transaction routing, and informational callouts.

## Typography

The typographic strategy pairs **Manrope** for confident, geometric headings with **Inter** for dense, neutral UI text, accompanied by **JetBrains Mono** for accounting figures, account numbers, and transaction ledgers.

- **Manrope:** Delivers architectural presence for titles and page headers without feeling overly decorative or fragile.
- **Inter:** Ensures legibility in dense forms, data tables, and verification screens at 13px–14px sizes.
- **JetBrains Mono:** Enforces tabular alignment for monetary sums, routing codes, IBAN numbers, and timestamps to eliminate visual jitter during real-time ledger updates.

## Layout & Spacing

This design system uses a precise 4px baseline grid optimized for high-density administrative operations.

- **Desktop (1920px+):** 12-column fluid grid with 24px (`1.5rem`) gutters and dynamic side margins (up to 40px). Side rails and navigation drawers remain fixed width (64px collapsed, 260px expanded), leaving the viewport flexible for data tables.
- **Tablet (768px - 1023px):** 8-column layout with 16px gutters and 24px margins. Operational data panels collapse from horizontal multi-column grids into stacked horizontal groups.
- **Mobile (< 768px):** 4-column system with 16px gutters and 16px margins. Financial cards drop into single-column flows, while action bars pin to the bottom edge.
- **Data Densities:** Tables and nested form lists utilize compact vertical spacing (`space-sm` for row cells) to maximize visible records above the fold.

## Elevation & Depth

Visual depth relies on low-contrast structural outlines and subtle ambient shadows to preserve clean enterprise clarity:

- **Border Hierarchy:** Cards, table boundaries, inputs, and tab containers feature 1px solid borders (`#E2E8F0` / Slate-200). Layering and nested panels use soft color shifts (`#FFFFFF` resting on `#F8FAFC`) rather than dark drop shadows.
- **Resting Level (Card & Panels):** `0 1px 3px 0 rgba(15, 23, 42, 0.05), 0 1px 2px -1px rgba(15, 23, 42, 0.03)`.
- **Active / Dropdown Level:** `0 4px 6px -1px rgba(15, 23, 42, 0.07), 0 2px 4px -2px rgba(15, 23, 42, 0.05)`.
- **Modal & Floating Dialogs:** `0 20px 25px -5px rgba(15, 23, 42, 0.12), 0 8px 10px -6px rgba(15, 23, 42, 0.08)` paired with a navy backdrop overlay (`rgba(15, 23, 42, 0.45)` with `backdrop-filter: blur(4px)`).

## Shapes

The design system maintains a structured, semi-compact radius value:
- **Base Components (Inputs, Buttons, Badges):** 4px (`0.25rem`) corner radius. This communicates administrative discipline and alignment.
- **Containers & Surfaces (Cards, Tables, Drawers, Modals):** 8px (`0.5rem`) corner radius (`rounded-lg`), delivering subtle softness while preserving horizontal and vertical lines.
- **Pill Exceptions:** Restricted strictly to counter tags, contextual system alert pills, and status badges.

## Components

### Buttons
- **Primary Institution:** Solid Navy (`#002B49`), white text, 40px height for administrative standard, 48px for sign-in/primary flows. Hover: `#0F172A`. Focused: 2px ring offset with `#002B49`.
- **Accent Action (High Impact):** Solid Coral-Red (`#E30613`), white text. Reserved for single-action authorizations, immediate payment transfers, and critical validation. Hover: `#C90510`.
- **Secondary / Outline:** White background with 1px border (`#CBD5E1`), text `#1E293B`. Hover: `#F1F5F9`.
- **Ghost:** Transparent background, text `#475569`. Hover: `#F1F5F9`.

### Input Fields & Controls
- **Form Controls:** 42px height, 1px border (`#CBD5E1`), `#FFFFFF` background, 14px Inter text. Placeholder text `#94A3B8`. Focused state introduces a 1.5px `#002B49` border with a subtle `0 0 0 3px rgba(0, 43, 73, 0.1)` glow.
- **Checkboxes & Radios:** 16px square/circle with a 1px border (`#94A3B8`). Selected: Deep Navy fill (`#002B49`) with crisp white checkmark/dot.

### Status Badges
- **Configuration:** 22px height, 8px horizontal padding, 11px uppercase `JetBrains Mono` font with medium weight.
- **Variants:**
  - *Success:* `#ECFDF5` background, `#047857` text, `#A7F3D0` subtle border.
  - *Warning:* `#FFFBEB` background, `#B45309` text, `#FDE68A` subtle border.
  - *Danger:* `#FFF1F2` background, `#BE123C` text, `#FECDD3` subtle border.
  - *Info/Neutral:* `#F1F5F9` background, `#334155` text, `#E2E8F0` subtle border.

### Enterprise Tables
- **Header:** 36px height, background `#F8FAFC`, uppercase 12px text (`#64748B`), 1px bottom border (`#E2E8F0`).
- **Rows:** 48px standard row height, `#FFFFFF` resting, hover state `#F8FAFC`. Alternating zebra stripes are avoided in favor of crisp 1px `#F1F5F9` row dividers.
- **Numeric & Ledger Cells:** Right-aligned using `JetBrains Mono` with negative spacing optimization.

### Modals & Drawers
- **Header Section:** 56px height, `#FFFFFF` background, 1px border bottom (`#E2E8F0`), strong title in `Manrope` 18px.
- **Drawer Panels:** Slide in from right edge for item inspection and transaction drill-downs; fixed width of 480px or 640px, full viewport height, styled with clean `#E2E8F0` left-border dividers.