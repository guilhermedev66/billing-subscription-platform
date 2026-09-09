# SaaS Billing UI Design Tokens & Reference Guide
**Target Audience:** `Claude — Frontend / UI`  
**Purpose:** Tailwind CSS v4 tokens calibration against Stripe, Chargebee, Lemon Squeezy, and Paddle.

---

## 1. Tailwind v4 Theme Configuration (`@theme`)

```css
@import "tailwindcss";

@theme {
  /* Neutral Zinc Surfaces (Stripe/Linear Dark Financial Ergonomics) */
  --color-surface-canvas: #09090b;      /* zinc-950 */
  --color-surface-card: #121215;        /* zinc-900 elevated */
  --color-surface-subtle: #18181b;      /* zinc-900 */
  --color-surface-muted: #27272a;       /* zinc-800 */
  
  /* Crisp 1px Structural Borders */
  --color-border-subtle: #27272a;       /* zinc-800 */
  --color-border-hover: #3f3f46;        /* zinc-700 */
  --color-border-focus: #71717a;        /* zinc-500 */

  /* Typographic Hierarchy */
  --color-text-primary: #fafafa;        /* zinc-50 */
  --color-text-secondary: #a1a1aa;      /* zinc-400 */
  --color-text-muted: #71717a;          /* zinc-500 */
  --color-text-disabled: #52525b;       /* zinc-600 */

  /* Simulator Top Banner */
  --color-sim-banner-bg: #451a03;       /* amber-950/40 */
  --color-sim-banner-border: #f59e0b;   /* amber-500 */
  --color-sim-banner-text: #fcd34d;     /* amber-300 */
}
```

---

## 2. Real-World Benchmark Token Breakdown

### A. Sidebar Navigation

| Platform | Background | 1px Border | Nav Item Inactive | Nav Item Active | Active Accent Indicator |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Stripe Dashboard** | `#0f1117` / `#0a2540` | `rgba(255,255,255,0.08)` | `#8898aa` | `#ffffff` | Left border 3px solid `#635bff` or pill `rgba(255,255,255,0.06)` |
| **Lemon Squeezy** | `#121212` / `#fafafa` | `#262626` / `#eaeaea` | `#737373` | `#fafafa` / `#18181b` | Pill `bg-zinc-800/80` or `bg-zinc-100` |
| **Chargebee** | `#141724` | `#1f2338` | `#94a3b8` | `#ffffff` | Indigo pill `#4338ca` |
| **Recommended for Simulator** | `#09090b` (zinc-950) | `#27272a` (zinc-800) | `#a1a1aa` (zinc-400) | `#fafafa` (zinc-50) | Pill `bg-zinc-800/70 border border-zinc-700/50` |

---

## 3. Semantic Status Badge System (Tailwind v4 Classes)

All status badges follow the Lemon Squeezy + Stripe hybrid model: **Subtle tinted background + crisp matching border + 6px solid indicator dot + high-contrast text**.

### 1. Active / Paid / Succeeded
* **Token:** `emerald`
* **Badge:** `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-400 border border-emerald-500/20`
* **Dot:** `w-1.5 h-1.5 rounded-full bg-emerald-400`
* **Real-world Hex:** Background `rgba(16, 185, 129, 0.1)`, Text `#34d399`, Border `rgba(16, 185, 129, 0.25)`

### 2. Past Due / Incomplete / Dunning
* **Token:** `amber`
* **Badge:** `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20`
* **Dot:** `w-1.5 h-1.5 rounded-full bg-amber-400`
* **Real-world Hex:** Background `rgba(245, 158, 11, 0.1)`, Text `#fbbf24`, Border `rgba(245, 158, 11, 0.25)`

### 3. Unpaid / Failed / Declined
* **Token:** `rose`
* **Badge:** `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-400 border border-rose-500/20`
* **Dot:** `w-1.5 h-1.5 rounded-full bg-rose-400`
* **Real-world Hex:** Background `rgba(244, 63, 94, 0.1)`, Text `#fb7185`, Border `rgba(244, 63, 94, 0.25)`

### 4. Canceled / Void / Paused / Draft
* **Token:** `zinc`
* **Badge:** `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-500/10 text-zinc-400 border border-zinc-500/20`
* **Dot:** `w-1.5 h-1.5 rounded-full bg-zinc-400`
* **Real-world Hex:** Background `rgba(113, 113, 122, 0.1)`, Text `#a1a1aa`, Border `rgba(113, 113, 122, 0.25)`

### 5. Trialing / Processing / Pending
* **Token:** `sky`
* **Badge:** `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-sky-500/10 text-sky-400 border border-sky-500/20`
* **Dot:** `w-1.5 h-1.5 rounded-full bg-sky-400`
* **Real-world Hex:** Background `rgba(14, 165, 233, 0.1)`, Text `#38bdf8`, Border `rgba(14, 165, 233, 0.25)`

---

## 4. Simulator Mode Persistent Cockpit Banner

```html
<div class="h-10 px-4 bg-amber-500/10 border-b border-amber-500/30 flex items-center justify-between text-xs text-amber-300 font-medium">
  <div class="flex items-center gap-2">
    <span class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400 font-mono text-[10px] tracking-wider uppercase font-semibold">
      ⚡ SIMULATOR MODE
    </span>
    <span class="text-amber-200/80">Virtual Time:</span>
    <span class="font-mono text-amber-100 font-semibold">2026-09-09 14:00 UTC</span>
  </div>
  <div class="flex items-center gap-1.5">
    <button class="px-2 py-1 bg-amber-500/15 hover:bg-amber-500/25 text-amber-200 border border-amber-500/30 rounded text-xs font-medium transition-colors">
      +1 Day
    </button>
    <button class="px-2 py-1 bg-amber-500/15 hover:bg-amber-500/25 text-amber-200 border border-amber-500/30 rounded text-xs font-medium transition-colors">
      +30 Days
    </button>
  </div>
</div>
```
