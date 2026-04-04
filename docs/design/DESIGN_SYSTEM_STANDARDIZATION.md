# Design System Standardization & Consistency Audit

**Date:** 2026-04-04  
**Objective:** Eliminate CSS fragmentation and establish unified design system across all 11 pages  
**Status:** Analysis Complete → Ready for Implementation

---

## Current State: Fragmentation Found

### 1. SPACING SCALE (Critical Issue)
**Current:** 28 different margin/padding/gap values
```
Margins: 0.4, 0.5, 0.6, 0.72, 0.75, 1, 1.5rem
Padding: 0.3, 0.35, 0.5, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 1rem
Gaps: 0.15, 0.18, 0.28, 0.35, 0.4, 0.45, 0.5, 0.56, 0.65, 0.72, 0.75, 0.8, 0.9, 1, 1.5rem
```

**Problem:** No consistent scale. Every component uses different spacing.
**Impact:** 
- Difficult to maintain
- Visual inconsistency across pages
- Hard to predict spacing behavior

### 2. TYPOGRAPHY SCALE (Critical Issue)
**Current:** 30+ different font sizes (0.55rem to 1.25rem)
```
Most common: 0.6, 0.65, 0.7, 0.72, 0.74, 0.76, 0.8, 0.86, 0.95, 1.05rem
Problems: 
- 0.6 vs 0.65 vs 0.68 vs 0.7 (4 "small" sizes)
- 0.72 vs 0.74 vs 0.76 (3 "label" sizes)
- 0.8 vs 0.82 vs 0.85 vs 0.86 (4 "body" sizes)
```

**Impact:** Typography feels chaotic and unmaintainable

### 3. COMPONENT HEIGHTS (Moderate Issue)
**Current:** Multiple different min-heights
```
Metrics: 3.2rem (optimized) vs 4.8rem (old values still in CSS)
Card headers: implied 2.4rem
BOM type headers: 1.8rem
Inputs/buttons: inconsistent heights
```

### 4. BORDER RADIUS (Moderate Issue)
**Current:** 8 different values (0.3rem to 1rem)
```
Cards: 0.4rem, 0.5rem, 0.8rem, 1rem
Buttons: 0.3rem, 0.4rem
Badges: 0.4rem, 0.55rem, 0.6rem
```

### 5. PADDING CONSISTENCY (Moderate Issue)
**Card body padding:**
- Most: 0.5rem 0.7rem (optimized)
- Some: 0.75rem (old)
- Tables: 0 or special values

**Card header padding:**
- Most: 0.5rem 0.7rem
- Some: 0.65rem 0.85rem (old)
- Inconsistent gap values

---

## Proposed Design System

### SPACING SCALE (3-level system)
```
Tight:   0.4rem  (filter gaps, close items)
Normal:  0.6rem  (standard gap between components)
Spacious: 0.9rem (major section gaps)
Large:   1.2rem  (page margins)

Applications:
- margin-bottom: 0.4rem OR 0.6rem (never 0.5rem, 0.75rem, 1rem)
- padding: 0.5rem 0.6rem OR 0.7rem 0.8rem OR 1rem 1.2rem
- gap: 0.4rem OR 0.6rem OR 0.9rem (never 0.35, 0.56, 0.72, 0.75)
- margin-bottom on sections: 0.6rem (consistent)
```

### TYPOGRAPHY SCALE (5-level system)
```
XS (Extra Small):  0.55rem - labels, badges
SM (Small):        0.65rem - helper text, captions
MD (Medium):       0.8rem  - body text, card titles
LG (Large):        0.95rem - page subtitles
XL (Extra Large):  1.1rem  - page titles

Current offenders to fix:
- 0.6, 0.62, 0.68, 0.7, 0.72, 0.74, 0.75, 0.76, 0.77, 0.79, 0.82, 0.85, 0.86, 1.05
  → Consolidate to: 0.55, 0.65, 0.8, 0.95, 1.1
```

### COMPONENT HEIGHTS
```
Metric cards:      3.2rem (fixed)
Card headers:      2.4rem (fixed) 
Input/button:      2.2rem (fixed)
BOM type headers:  1.8rem (fixed)
Table rows:        2.0rem (fixed)

All with consistent padding: 0.3rem vertical (top+bottom)
```

### BORDER RADIUS (Unified)
```
All UI elements: 0.4rem
  - Cards: 0.4rem
  - Buttons: 0.4rem
  - Badges: 0.4rem
  - Input fields: 0.4rem
  - Tabs: 0.4rem
  
Exception: Only `.material-campaign` uses 0.8rem (larger, distinct cards)
```

### PADDING CONSISTENCY
```
Card header:      0.5rem 0.7rem (STANDARD)
Card body:        0.5rem 0.7rem (STANDARD)
Card with table:  0 (table needs full width)
Page section:     0.5rem 0.7rem (STANDARD)
Page body:        0.5rem 0.6rem (tighter, page-level)
```

### SHADOWS (Standardize to 2 types)
```
Soft shadow:      0 1px 3px rgba(0,0,0,0.02) - for subtle cards
Default shadow:   var(--shadow-soft) - most cards
Focus shadow:     var(--shadow-soft) elevated on hover

Remove: Any custom box-shadow definitions
```

---

## Implementation Plan

### Phase 1: CSS Variables (Establish scale)
Add to `:root`:
```css
/* Spacing scale */
--spacing-xs: 0.4rem;
--spacing-sm: 0.6rem;
--spacing-md: 0.9rem;
--spacing-lg: 1.2rem;

/* Typography scale */
--font-xs: 0.55rem;
--font-sm: 0.65rem;
--font-md: 0.8rem;
--font-lg: 0.95rem;
--font-xl: 1.1rem;

/* Component heights */
--height-input: 2.2rem;
--height-card-header: 2.4rem;
--height-metric: 3.2rem;

/* Border radius */
--radius-standard: 0.4rem;
--radius-card: 0.4rem;
--radius-large: 0.8rem;
```

### Phase 2: Card Standardization
- **All cards:** border-radius 0.4rem, padding 0.5rem 0.7rem
- **Card headers:** min-height 2.4rem, padding 0.5rem 0.7rem
- **Card body:** padding 0.5rem 0.7rem
- **Exception:** .material-campaign = 0.8rem radius

### Phase 3: Spacing Cleanup
- **All margin-bottom:** 0.6rem (unless small section = 0.4rem)
- **All gaps:** 0.6rem (tight sections = 0.4rem, spacious = 0.9rem)
- **All padding:** either 0.5rem 0.7rem OR 0.7rem 0.8rem
- **Page margins:** 0.5rem 0.6rem (body padding)

### Phase 4: Typography Cleanup
- **Page titles:** 1.1rem
- **Page subtitles:** 0.65rem
- **Card titles:** 0.8rem
- **Card subtitles:** 0.65rem
- **Labels/badges:** 0.55rem
- **Body text:** 0.8rem

### Phase 5: Component Height Standardization
- **Metric cards:** 3.2rem
- **Card headers:** 2.4rem
- **Input fields:** 2.2rem
- **Table rows:** 2.0rem
- **Button:** 2.2rem

### Phase 6: Border Radius Standardization
- **All:** 0.4rem (use --radius-standard variable)
- **Exception:** Material campaign cards = 0.8rem

### Phase 7: Audit All Pages
1. **Dashboard** - Ensure all metrics 3.2rem, all gaps 0.6rem
2. **Campaigns** - Standardize card padding, margins
3. **Schedule** - Check chart container spacing
4. **Orders** - Standardize table cell heights, gaps
5. **Dispatch** - Standardize card grid spacing
6. **Material** - Consolidate card types, fix campaign card styling
7. **Capacity** - Table row heights, header styling
8. **Scenarios** - Card grid gaps, list table spacing
9. **CTP** - Form input heights, table spacing
10. **BOM** - KPI pills, type headers, detail tables
11. **Master** - Table cell padding, header height

---

## Expected Outcomes

### Before (Fragmented)
```
Dashboard: Various card heights (3.2, 4.8rem), gaps (0.35, 0.5, 0.6, 0.72rem)
Material: Different campaign card styles, inconsistent padding
BOM: Multiple font sizes (0.55, 0.6, 0.68, 0.74rem), gaps (0.35, 0.5, 0.4)
Master: Table with custom padding (0, 0.85rem), inconsistent headers
```

### After (Standardized)
```
Dashboard: All cards 3.2rem, all gaps 0.6rem (tight=0.4rem)
Material: Uniform campaign cards (0.8rem radius), consistent padding
BOM: Single typography scale (0.55/0.65/0.8/0.95/1.1rem), gaps (0.4/0.6rem)
Master: Table with standard padding (0.5rem 0.7rem), consistent headers
```

---

## Maintenance Benefits

✓ **Easier Updates:** Change 1 CSS variable → all related components update
✓ **Consistency:** Every component follows same spacing/sizing rules
✓ **Performance:** Fewer CSS rules (consolidate duplicates)
✓ **Onboarding:** New developers understand the system immediately
✓ **Responsive:** Easier to write media queries with consistent scale
✓ **Testing:** Visual regression testing easier with uniform components

---

## Files to Modify

1. `ui_design/styles.css`
   - Add CSS variables to `:root`
   - Replace 28 spacing values with 3-4 standard values
   - Replace 30+ font sizes with 5 standard sizes
   - Standardize border-radius to 0.4rem (except material-campaign)
   - Consolidate padding across all cards
   - Remove duplicate box-shadow rules

2. `ui_design/index.html`
   - Minor inline style adjustments (remove conflicting margin-bottom)
   - Ensure all grid layouts use consistent gap class

---

## Implementation Checklist

- [ ] Add CSS variables to :root
- [ ] Consolidate .metric and .card-* rules
- [ ] Standardize all .grid-*, .split, .stack rules
- [ ] Fix all margin-bottom to 0.4rem or 0.6rem
- [ ] Fix all gap to 0.4rem, 0.6rem, or 0.9rem
- [ ] Consolidate typography to 5-level scale
- [ ] Unify border-radius (except material-campaign)
- [ ] Standardize card padding across all pages
- [ ] Remove duplicate box-shadow definitions
- [ ] Audit each of 11 pages for compliance
- [ ] Test responsive behavior with new scale
- [ ] Verify no visual regressions

---

**Estimated CSS Reduction:** ~50+ lines of fragmented rules → ~10 lines of variables
**Estimated Maintenance Benefit:** 60% easier to update global styling
