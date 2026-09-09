# Billing Platform M0 Research Brief — SaaS Billing Simulator Architecture & UX Reference
**Document Version:** 1.0.0 (Milestone 0: Discovery, Architecture & Product Reference)  
**Target Audience:** Orchestrator (Scope & Roadmap), `Claude — Frontend / UI` (Visual & Component Design), `Codex — Backend` (Data Model & Simulation Engine)  
**Product Scope:** Developer-grade SaaS Billing & Subscription Simulator (Portfolio Showcase, Zero Real Money)  
**Core Tech Stack:** Clean Modern Web Architecture (React 19 + TypeScript + Vite + Tailwind CSS | ASP.NET Core 10 / C# or Node/Go + PostgreSQL)  
**Design Philosophy:** *Financial Rigor + Developer Ergonomics + Visual Calm + Simulation Fidelity*

---

## Executive Summary & Simulation Mission

The **Billing & Subscription Platform** is a portfolio SaaS billing simulator designed to demonstrate mastery of modern recurring billing, subscription lifecycles, and financial developer tooling. Unlike standard CRUD applications or toy projects with mock payment buttons, this platform simulates the architectural depth and operational workflows of global billing engines like **Stripe Billing**, **Chargebee**, **Paddle**, and **Lemon Squeezy**.

### Core Product Thesis
1. **Zero Real Money, 100% Real Architecture:** Real credit cards are never charged. Instead, the platform implements a deterministic payment simulation engine (deterministic card numbers for declines, insufficient funds, expired cards, and 3D Secure challenges) alongside a comprehensive dunning and retry state machine.
2. **"Time Travel" & Simulation Superpowers:** A key differentiator of a portfolio billing simulator is the ability to fast-forward virtual time (`+1 Day`, `+7 Days`, `+30 Days`, `Advance to Next Renewal Anchor`) to trigger invoice generation, proration line items, payment retries, and webhook emissions on demand without waiting for real calendar days.
3. **Developer-Grade Transparency:** Provide both a high-level executive SaaS dashboard (MRR, ARR, Churn, Net Movements) and developer-first inspection tools (click-to-copy IDs, raw JSON inspection drawers, HMAC-signed webhook delivery logs, and an interactive event timeline).

### Explicit Anti-Patterns (What We Are NOT Building)
- ❌ **Naive "Pay Now" Modal Stubs:** No fake form that just sets `is_paid = true` in an instant click without generating an invoice, calculating taxes/discounts, or emitting an event.
- ❌ **Floating-Point Financial Math:** Never use JavaScript `Number` or database `FLOAT/DOUBLE` for monetary amounts. Every monetary value is strictly stored in integer cents (`USD 29.00` = `2900`).
- ❌ **Destructive In-Place Edits on Invoices:** Finalized invoices are legal financial records; they are immutable. Modifications mid-cycle produce prorated credit/debit adjustment line items or credit notes, never direct row edits.
- ❌ **Generic Purple-Gradient "AI SaaS" Aesthetics:** We reject saturated neon purple meshes and blurry card soup. We embrace clean, high-density, crisp 1px bordered financial ergonomics with semantic status tokens.

---

## 1. SaaS Billing Benchmark Analysis Matrix

We benchmarked four world-class billing platforms to extract proven product workflows, user ergonomics, and developer patterns:

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   BENCHMARK ANALYSIS MATRIX                                            │
├───────────────────┬───────────────────────────────────┬────────────────────────────────────────────────┤
│ Platform          │ Dominant Product & UX Strength    │ What Our Billing Simulator Borrows             │
├───────────────────┼───────────────────────────────────┼────────────────────────────────────────────────┤
│ 1. Stripe Billing │ "Developer & Operational Depth"   │ • Tokenized URL-synced facet filters           │
│                   │ The gold standard in API design,  │ • Persistent amber "Test/Simulator Mode" bar   │
│                   │ event logs, and slide-over drawers│ • Slide-over entity drawers (keep table state) │
│                   │                                   │ • Copyable IDs (`cus_...`) with micro-toasts   │
│                   │                                   │ • Dual-view: human card + raw JSON tab         │
├───────────────────┼───────────────────────────────────┼────────────────────────────────────────────────┤
│ 2. Lemon Squeezy  │ "Visual Calm & Creator Simplicity"│ • Soft pastel badge fills with colored dots    │
│                   │ Frictionless subscription UX with │ • Chronological visual customer event timeline │
│                   │ radical typographic clarity.      │ • Humanized renewal microcopy                  │
│                   │                                   │ • Clean tier selector with period discounts    │
├───────────────────┼───────────────────────────────────┼────────────────────────────────────────────────┤
│ 3. Chargebee      │ "Enterprise Lifecycle & Dunning"  │ • Granular action buttons (Pause, Resume, Swap)│
│                   │ Rigorous subscription state engine│ • "Preview Proration" modal before committing  │
│                   │ and comprehensive dunning rules.  │ • Dedicated Dunning / At-Risk revenue cockpit  │
│                   │                                   │ • Real-time seat quantity recalculator         │
├───────────────────┼───────────────────────────────────┼────────────────────────────────────────────────┤
│ 4. Paddle         │ "MoR Clarity & Invoice Integrity" │ • Clear breakdown: Gross - Tax - Fee = Net     │
│                   │ Merchant of Record transparency,  │ • Printable SVG/HTML official invoice sheet    │
│                   │ localized tax and credit notes.   │ • Formal Credit Note emission on refund/void   │
└───────────────────┴───────────────────────────────────┴────────────────────────────────────────────────┘
```

---

## 2. Core SaaS Billing Domain Workflows & Lifecycle Architecture

### 2.1 Multi-Tenancy & Organizations
- **Organization Boundary (`org_id`):** Every customer, product, subscription, invoice, webhook, and metric is scoped to an organization.
- **Organization Settings:**
  - Default currency (`USD`, `EUR`, `GBP`).
  - Invoice sequence formatting: Prefix (e.g. `INV-2026-`), starting number (e.g. `0001`), tax identification (EIN, VAT).
  - Webhook signing secrets (`whsec_...`) and API keys (`pk_test_...`, `sk_test_...`).
- **Simulation Mode Global Flag:** Persistent visual indicator that the organization operates in simulation mode with virtual clock controls.

### 2.2 Customer Domain & Hosted Billing Portal
- **Customer Entity (`cus_...`):**
  - Name, email, billing address, currency.
  - **Credit Balance (`balance` in cents):** Positive balance represents customer credit (e.g., negative invoice debit applied automatically to next invoice); negative balance represents outstanding debt.
  - **Delinquency Status (`delinquent: boolean`):** Set automatically when any invoice enters dunning or `past_due`.
  - Payment methods list with a marked default (simulated credit cards, ACH).
- **Self-Serve Customer Portal Persona:**
  - A dedicated route (`/portal/:customer_id`) simulating what the end-user customer sees.
  - Allows self-serve actions: view current subscription, download past invoice receipts, upgrade/downgrade plan, adjust seats, swap simulated test payment card, and cancel subscription.

### 2.3 Product Catalog & Multi-Tier Pricing Models
Separates the **Product** (marketing entity: "Pro Plan", "Enterprise Suite") from the **Price** (purchasable financial contract):

```
Product: "Cloud Analytics Platform" (prod_123)
  ├── Price 1: $29.00 / month (flat recurring)
  ├── Price 2: $290.00 / year (flat recurring, save ~17%)
  ├── Price 3: $12.00 / seat / month (per-unit recurring)
  └── Price 4: $0.05 / 1,000 API requests (metered / usage overage)
```

**Supported Pricing Models in Simulator:**
1. **Flat-Fee Recurring:** Standard monthly or annual recurring charge (e.g., $29/mo or $290/yr).
2. **Per-Seat / Per-Unit:** Variable quantity multiplied by base price (e.g., 5 seats @ $15/seat = $75/mo).
3. **Tiered / Graduated Pricing:** Tier 1 (1-5 units) @ $20; Tier 2 (6-20 units) @ $15; Tier 3 (21+ units) @ $10.
4. **Metered / Usage-Based:** Ingests usage events throughout the cycle, aggregations (Sum, Max, Last) billed in arrears on next invoice.
5. **Free Trial Support:** Specified trial duration in days (e.g. 14 days). Supports both "No card upfront" and "Card required upfront".

### 2.4 Subscription State Machine & Proration Engine

#### Complete Subscription State Diagram
```
             ┌────────────────────────┐
             │      (Creation)        │
             └──────────┬─────────────┘
                        │
         ┌──────────────┴──────────────┐
         ▼                             ▼
   [ trialing ]                  [ active ] ◄──────────────────────┐
         │                             │                           │
         │ (Trial expires)             │ (Renewal billing fails)   │ (Payment
         │                             ▼                           │  recovered)
         │                       [ past_due ] ─────────────────────┘
         │                             │
         │                             │ (Dunning retries exhausted)
         │                             ▼
         │                        [ unpaid ]
         │                             │
         └──────────────┬──────────────┘
                        │ (Canceled by user or system)
                        ▼
                  [ canceled ]
```

#### Subscription States & Semantics:
- `incomplete`: Subscription created with payment method requiring confirmation; transitions to `active` once payment succeeds or `incomplete_expired` after 24 hours.
- `trialing`: In active trial period. No invoice charged until trial end date (`trial_end`).
- `active`: Current period paid and good standing.
- `past_due`: Renewal payment failed; currently in dunning retry sequence.
- `unpaid`: Dunning sequence exhausted without payment recovery; service suspended.
- `canceled`: Subscription terminated; no further invoices will be generated.
- `paused`: Billing or service temporarily held.

#### Proration Mechanics (The Mathematical Foundation):
When a customer changes plans or seat counts mid-cycle:
$$	ext{Elapsed Ratio} = rac{	ext{VirtualNow} - 	ext{PeriodStart}}{	ext{PeriodEnd} - 	ext{PeriodStart}}$$
$$	ext{Remaining Ratio} = 1 - 	ext{Elapsed Ratio} = rac{	ext{PeriodEnd} - 	ext{VirtualNow}}{	ext{PeriodEnd} - 	ext{PeriodStart}}$$

- **Prorated Unused Credit (Old Plan):**
  $$	ext{Credit Amount} = -1 	imes \left( 	ext{Old Plan Price} 	imes 	ext{Remaining Ratio} ight)$$
- **Prorated Charge (New Plan):**
  $$	ext{Debit Amount} = +1 	imes \left( 	ext{New Plan Price} 	imes 	ext{Remaining Ratio} ight)$$
- **Net Proration Amount:**
  $$	ext{Net Due} = 	ext{Debit Amount} + 	ext{Credit Amount}$$

**Handling Policies:**
- **Immediate Upgrade:** Emits an immediate one-off prorated invoice for `Net Due` (or credits the balance if negative) and resets or maintains cycle anchor.
- **Downgrade at Period End (`cancel_at_period_end` or schedule change):** Keeps current plan active until `current_period_end`, then switches automatically on the next renewal date.
- **Seat Adjustments:** Immediate prorated line item created on the upcoming invoice draft or charged immediately.

### 2.5 Invoicing Engine & Financial Line Items
- **Invoice Lifecycle States:**
  - `draft`: Open for line-item mutations (e.g. usage ingestion, adjustments).
  - `open`: Finalized and posted. Legally locked. Waiting for auto-charge or manual payment.
  - `paid`: Successfully settled with a payment transaction.
  - `uncollectible`: Bad debt / written off following failed dunning.
  - `void`: Canceled before payment.
- **Line Items Composition:**
  - Base subscription fee.
  - Proration debit line items (`Prorated upgrade to Pro Plan from Sep 15 to Sep 30`).
  - Proration credit line items (`Unused time on Starter Plan from Sep 15 to Sep 30`).
  - Discounts / Coupons (`coupon_welcome20` -> -20%).
  - Taxes (simulated VAT / Sales Tax percentage).
- **Printable / PDF Invoice Layout:**
  - Professional, printable invoice layout including Organization Header, Customer VAT/Address, Sequential Invoice ID (`INV-2026-0042`), Issue Date, Due Date, Itemized Table, Subtotal, Taxes, Credits Applied, Total Due, and Paid Stamp.

### 2.6 Simulated Payment Gateway & Dunning Engine

#### Deterministic Test Cards Library
The platform provides a one-click test card selector for users to simulate every edge case:

```
┌────────────────────────┬─────────────────────────┬───────────────────────────────────────────┐
│ Card Number            │ Simulation Result       │ Triggered Lifecycle Flow                  │
├────────────────────────┼─────────────────────────┼───────────────────────────────────────────┤
│ 4242 4242 4242 4242    │ Succeeded (200 OK)      │ Invoice -> paid, Sub -> active            │
│ 4000 0000 0000 0002    │ Card Declined           │ Invoice -> past_due, Dunning sequence     │
│ 4000 0000 0000 0004    │ Insufficient Funds      │ Invoice -> past_due, Dunning sequence     │
│ 4000 0000 0000 0005    │ Expired Card            │ Immediate payment failure notification    │
│ 4000 0000 0000 3022    │ 3D Secure Verification  │ Interactive OTP modal simulation challenge│
│ 4000 0000 0000 0007    │ Processing Error        │ Simulated transient gateway 500 error     │
└────────────────────────┴─────────────────────────┴───────────────────────────────────────────┘
```

#### Dunning Retry Engine State Machine:
- **Failure 1 (Day 0 / Anchor Date):** Renewal payment fails. Invoice marks `open` (payment failed). Subscription status degrades from `active` to `past_due`. Event `invoice.payment_failed` emitted. Dunning Attempt #1 recorded.
- **Failure 2 (Day 3):** Virtual clock advances 3 days. Dunning background job executes automated retry #2. Fails again. Alert email simulated in activity log.
- **Failure 3 (Day 7):** Automated retry #3 executes. Fails again.
- **Failure 4 (Day 14 - Final Action):** Final retry fails. Invoice marked `uncollectible`. Subscription transitions to `unpaid` or `canceled`.
- **Payment Recovery Path:** When the user updates the customer's payment method to `4242...` and clicks "Retry Payment", the invoice immediately moves to `paid`, subscription restores to `active`, and delinquency is cleared.

### 2.7 Webhooks & Event-Driven Audit Engine
- **Standard JSON Event Envelope:**
  ```json
  {
    "id": "evt_1P8xYz2eZvKYlo2C9",
    "object": "event",
    "api_version": "2026-09-01",
    "created": 1725894000,
    "type": "invoice.payment_succeeded",
    "org_id": "org_demo_01",
    "data": {
      "object": {
        "id": "in_1P8xYz2eZvKYlo2C",
        "customer": "cus_9941aB",
        "amount_paid": 2900,
        "currency": "usd",
        "status": "paid"
      }
    }
  }
  ```
- **Webhook Delivery Engine:**
  - Endpoint registration with endpoint URL and secret (`whsec_...`).
  - Cryptographic HMAC-SHA256 signature generated in `X-Signature: t=1725894000,v1=98df8...` header.
  - Delivery history log: Records HTTP status, response time (ms), request body, and response headers.
  - Interactive "Resend / Test Webhook" simulator tool for inspecting developer webhooks.

### 2.8 Executive Dashboard & SaaS Analytics
- **North Star Metrics:**
  - **MRR (Monthly Recurring Revenue):** Normalized monthly value of all active recurring subscriptions.
  - **ARR (Annual Recurring Revenue):** `MRR * 12`.
  - **Net MRR Movements (Waterfall):**
    $$	ext{Net New MRR} = 	ext{New MRR} + 	ext{Expansion MRR} - 	ext{Contraction MRR} - 	ext{Churned MRR}$$
  - **ARPU (Average Revenue Per User):** `Total MRR / Active Customers`.
  - **Subscriber Churn Rate:** Percentage of subscriptions canceled in the period.
  - **Dunning at-Risk Revenue:** Total volume of invoices in `past_due` state.

---

## 3. Detailed UX & Visual Reference Points (Stripe, Lemon Squeezy, Chargebee, Paddle)

### Reference Point 1: Stripe Dashboard (Developer Precision & URL-First State)
- **Persistent Test Mode Banner:** A distinctive amber/orange top border (`border-t-4 border-amber-500`) and a prominent status badge `[ ⚡ SIMULATOR MODE ]` reminding the user no real money is moving.
- **Deep-Linkable Facet Search Bar:**
  ```
  [ 🔍 Filter: status:active  plan:pro_monthly  created:>2026-08-01 ]
  ```
  Every filter chip updates the URL query string (`?status=active&plan=pro_monthly`), enabling shareable links and flawless browser back/forward navigation.
- **Slide-Over Detail Drawers (`Sheet`):** Clicking a table row opens a 540px slide-over panel on the right with the entity's metadata, leaving the background table visible and preserving scroll position.
- **Copyable Hash IDs with Instant Feedback:** Every `cus_...`, `sub_...`, and `in_...` identifier has an adjacent copy button with a 1.2s micro-toast `Copied cus_9941 to clipboard`.
- **Raw JSON Inspector Tab:** Every detail view includes a `"Raw JSON"` developer tab allowing engineers to see the exact API payload.

### Reference Point 2: Lemon Squeezy (Visual Calm, Pastel Semantics & Customer Timeline)
- **Typographic Restraint & Soft Colorway:** Replaces harsh bright borders with soft zinc/slate lines (`border-slate-200 / dark:border-slate-800`) and pastel badge fills (`bg-emerald-50 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300`).
- **Visual Customer Narrative Timeline:** Rather than a dry log table, the customer detail page leads with a human-readable event timeline:
  ```
  ● Sep 09, 2026 14:00 — Subscribed to Pro Plan ($29.00/mo) via Visa •••• 4242
  ● Sep 09, 2026 14:00 — Invoice #INV-0042 marked Paid ($29.00)
  ● Sep 01, 2026 10:00 — Customer record created (acme@corp.com)
  ```
- **Radical Simplicity in Status:** Renewal dates expressed in relative + absolute time (`Renews in 21 days (Sep 30, 2026)`).

### Reference Point 3: Chargebee (Lifecycle Rigor & Dunning Cockpit)
- **Granular Lifecycle Controls:** Every subscription screen offers clear primary and secondary actions: `[ Upgrade/Downgrade Plan ]`, `[ Change Seat Count ]`, `[ Pause Subscription ]`, `[ Cancel ]`.
- **"Preview Proration & Upcoming Charges" Modal:** Before committing a subscription plan change or seat adjustment, a modal presents:
  ```
  ┌─────────────────────────────────────────────────────────────────┐
  │ Confirm Subscription Plan Change                                │
  ├─────────────────────────────────────────────────────────────────┤
  │ Current: Starter Plan ($15/mo)   ➜   New: Pro Plan ($45/mo)     │
  │ Effective Date: Immediate (Sep 09, 2026)                        │
  │ Remaining in Cycle: 21 days of 30 days (70%)                    │
  ├─────────────────────────────────────────────────────────────────┤
  │ Prorated credit (unused Starter):                     -$10.50   │
  │ Prorated charge (new Pro):                            +$31.50   │
  ├─────────────────────────────────────────────────────────────────┤
  │ Amount Due Immediately:                               +$21.00   │
  │ Next Regular Renewal (Sep 30, 2026):                   $45.00   │
  ├─────────────────────────────────────────────────────────────────┤
  │ [ Cancel ]                               [ Confirm & Charge ]   │
  └─────────────────────────────────────────────────────────────────┘
  ```
- **Dunning Cockpit:** Highlights delinquent accounts with high-visibility warning banners, dunning stage badges (`Attempt 2 of 4`), and a one-click `[ Retry Charge Now ]` button.

### Reference Point 4: Paddle (Merchant of Record & Tax/Invoice Transparency)
- **Financial Deduction Waterfall:** Clear visual itemization separating gross revenue, regional taxes, processing fees, and net payout:
  $$	ext{Gross Invoice} (\$100.00) - 	ext{Sales Tax/VAT} (\$10.00) - 	ext{Simulated Fee} (\$3.20) = 	ext{Net Revenue} (\$86.80)$$
- **Formal Credit Note Emission:** Refunds are not just balance subtractions; they emit a distinct, printable `Credit Note` entity referencing the parent invoice.

---

## 4. Specialized Portfolio Simulator Ergonomics ("God Mode" Simulation Bar)

To make this platform an extraordinary portfolio piece, it requires an interactive **Simulation Bar** docked persistently across the top of the application:

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ⚡ SIMULATION COCKPIT │ Virtual Clock: 2026-09-09 14:00 UTC │ [ +1 Day ] [ +7 Days ] [ +30 Days ] [ Advance to Next Anchor ] │
│ Fast Actions: [ ⚡ Trigger Renewal Cron ] [ 🔁 Process Dunning Retries ] [ 🧪 Seed Demo Dataset ] [ ⚙ Gateway Config ▾ ]     │
└────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Simulation Capabilities:
1. **Virtual Clock Display:** Displays the current simulated date/time.
2. **Time Travel Controls:**
   - `+1 Day`: Tests daily grace periods and usage sync.
   - `+7 Days`: Advances past trial expiration or 7-day dunning stage.
   - `+30 Days`: Advances through an entire monthly billing cycle to trigger renewal invoices.
   - `Advance to Next Anchor`: Automatically finds the nearest `current_period_end` across all active subscriptions and jumps directly to that timestamp.
3. **One-Click Batch Cron Triggers:**
   - `Trigger Renewal Cron`: Immediately executes the recurring invoice generation worker for all subscriptions whose virtual renewal date is reached.
   - `Process Dunning Retries`: Executes payment retry sweeps for all `past_due` invoices.
4. **Gateway Override Settings:**
   - Dropdown toggle to force all future simulated payments to decline or require 3D Secure, testing failure modes without changing card details.
5. **Deterministic Demo Seeder:**
   - Creates 20 realistic customers, 3 product tiers, 35 subscriptions across all states (`active`, `past_due`, `trialing`, `canceled`), past invoices, and populated event logs.

---

## 5. Proposed Information Architecture & Module Map for MVP

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│                                BILLING PLATFORM MODULE MAP                                   │
├─────────────────┬───────────────────────────────┬────────────────────────────────────────────┤
│ Module          │ Route                         │ Primary Capabilities                       │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M1: Dashboard   │ `/`                           │ • MRR, ARR, Active Subs, Churn KPIs        │
│ & Analytics     │                               │ • Net MRR Waterfall Chart                  │
│                 │                               │ • Live Real-time Activity / Event Feed     │
│                 │                               │ • At-Risk Dunning Warning Banner           │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M2: Customers   │ `/customers`                  │ • Searchable customer table & balance      │
│ & Portal        │ `/customers/:id`              │ • Slide-over customer inspection sheet     │
│                 │ `/portal/:customer_id`        │ • Payment method manager (test cards)      │
│                 │                               │ • Customer billing portal (self-serve)     │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M3: Product     │ `/products`                   │ • Products list & tier definitions         │
│ Catalog         │ `/products/new`               │ • Flat, per-seat, and tiered price builder │
│                 │ `/prices`                     │ • Free trial duration configuration        │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M4: Subscription│ `/subscriptions`              │ • Subscription state machine monitor       │
│ Manager         │ `/subscriptions/:id`          │ • Plan upgrade/downgrade with proration    │
│                 │                               │ • Seat adjustments & cancel/pause modal    │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M5: Invoices &  │ `/invoices`                   │ • Invoices table with status badges        │
│ Payments        │ `/invoices/:id`               │ • Printable / PDF invoice document viewer  │
│                 │ `/payments`                   │ • Pay with Test Card / 3DS challenge modal │
│                 │                               │ • Credit Notes & Refund issuer             │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M6: Developers  │ `/developers/webhooks`        │ • Webhook endpoint registration            │
│ & Webhooks      │ `/developers/events`          │ • Event timeline with expandable JSON      │
│                 │ `/developers/api-keys`        │ • HMAC signature inspector & resend tool   │
├─────────────────┼───────────────────────────────┼────────────────────────────────────────────┤
│ M7: Simulation  │ Persistent Top Bar &          │ • Virtual clock advance & time travel      │
│ Console         │ `/settings/simulation`        │ • Gateway behavior simulator toggle        │
│                 │                               │ • Demo dataset generator & database reset  │
└─────────────────┴───────────────────────────────┴────────────────────────────────────────────┘
```

---

## 6. Concrete Implementation Guidance for Frontend (`Claude — Frontend / UI`)

### 6.1 Design System & Visual Tokens
- **Palette:** Zinc/Slate neutral surface (`bg-zinc-50 dark:bg-zinc-950`), crisp solid borders (`border-zinc-200 dark:border-zinc-800`), dark navy/slate sidebar.
- **Typography & Numeral Formatting:**
  - Headers: Inter / Plus Jakarta Sans, semi-bold (`tracking-tight text-zinc-900 dark:text-zinc-100`).
  - Monetary values: Monospace with tabular numerals (`font-mono tabular-nums font-semibold`), right-aligned on all tables.
  - IDs & Hashes: Monospace with muted color (`font-mono text-xs text-zinc-500 hover:text-zinc-900 cursor-pointer`).

### 6.2 Semantic Status Badge System
```typescript
export const billingStatusTokens = {
  // Active, Paid, Succeeded
  success: {
    badge: "bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800",
    dot: "bg-emerald-500",
  },
  // Trialing, Processing, Pending
  info: {
    badge: "bg-sky-50 text-sky-700 border-sky-200 dark:bg-sky-950/40 dark:text-sky-300 dark:border-sky-800",
    dot: "bg-sky-500",
  },
  // Past Due, Incomplete, Action Required
  warning: {
    badge: "bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-800",
    dot: "bg-amber-500",
  },
  // Unpaid, Uncollectible, Failed, Declined
  destructive: {
    badge: "bg-rose-50 text-rose-700 border-rose-200 dark:bg-rose-950/40 dark:text-rose-300 dark:border-rose-800",
    dot: "bg-rose-500",
  },
  // Canceled, Voided, Paused, Draft
  neutral: {
    badge: "bg-zinc-100 text-zinc-700 border-zinc-200 dark:bg-zinc-800 dark:text-zinc-300 dark:border-zinc-700",
    dot: "bg-zinc-400",
  },
};
```

### 6.3 High-Density Table Standards
- Row height: 40px default with 1px borders.
- Sticky table headers with subtle background blur or solid zinc-100 background.
- Right-aligned monetary values and left-aligned names/descriptions.
- Micro-interactions: One-click copy icon on hover with `Copied!` tooltip.

### 6.4 Invoice Sheet UX
- Split-screen layout on `/invoices/:id`:
  - **Left pane (60%):** Paper-white card mimicking real printed invoice with company logo, clean line items, subtotal, tax breakdown, and status stamp (`PAID` in emerald, `PAST DUE` in amber, `VOID` in slate).
  - **Right pane (40%):** Operational actions (`[ Pay with Test Card ]`, `[ Void Invoice ]`, `[ Download PDF ]`, `[ Send Receipt ]`), payment attempts history, and raw event log.

---

## 7. Concrete Architecture & Security Guidance for Backend (`Codex — Backend`)

### 7.1 Relational Data Model (PostgreSQL / Relational DB)
1. **`organizations`:** `id`, `name`, `default_currency`, `invoice_prefix`, `webhook_secret`, `created_at`.
2. **`customers`:** `id` (`cus_...`), `org_id`, `name`, `email`, `currency`, `credit_balance_cents`, `delinquent`, `default_payment_method_id`, `metadata`.
3. **`products`:** `id` (`prod_...`), `org_id`, `name`, `description`, `active`, `created_at`.
4. **`prices`:** `id` (`price_...`), `product_id`, `type` (`recurring` / `one_time`), `model` (`flat`, `per_seat`, `tiered`, `metered`), `unit_amount_cents`, `currency`, `interval` (`month`, `year`), `trial_days`.
5. **`subscriptions`:** `id` (`sub_...`), `customer_id`, `price_id`, `status`, `quantity` (seats), `current_period_start`, `current_period_end`, `cancel_at_period_end`, `canceled_at`, `trial_end`.
6. **`invoices`:** `id` (`in_...`), `customer_id`, `subscription_id`, `status` (`draft`, `open`, `paid`, `uncollectible`, `void`), `subtotal_cents`, `tax_cents`, `total_cents`, `amount_paid_cents`, `due_date`, `paid_at`.
7. **`invoice_line_items`:** `id` (`ili_...`), `invoice_id`, `price_id`, `description`, `amount_cents`, `quantity`, `is_proration`, `period_start`, `period_end`.
8. **`payments`:** `id` (`pay_...`), `invoice_id`, `amount_cents`, `status`, `card_last4`, `card_brand`, `failure_code`, `failure_message`, `created_at`.
9. **`webhook_endpoints`:** `id` (`we_...`), `org_id`, `url`, `secret`, `active`, `subscribed_events`.
10. **`events`:** `id` (`evt_...`), `org_id`, `type`, `data_json`, `created_at`.
11. **`webhook_deliveries`:** `id`, `endpoint_id`, `event_id`, `response_status`, `response_body`, `duration_ms`, `attempt`, `created_at`.

### 7.2 Financial Invariants & Integrity Rules
- **Integer Cents Only:** All currency columns must be `BIGINT` or `INTEGER` representing cents (e.g., $49.99 = `4999`). Floating point arithmetic is forbidden.
- **Idempotency Keys (`Idempotency-Key`):** Support idempotency key headers on all payment and subscription mutation endpoints to prevent double-charging or duplicate invoices during retries.
- **Optimistic Concurrency / Row Locking:** Apply version columns or optimistic locking on `subscriptions` and `customers` to prevent race conditions between automated renewal crons and manual customer upgrades.

### 7.3 Virtual Clock & Time Travel Architecture
Create an injectable `IVirtualClock` service:
```csharp
public interface IVirtualClock
{
    DateTimeOffset Now { get; }
    void FastForward(TimeSpan duration);
    void SetTime(DateTimeOffset newTime);
    void Reset();
}
```
All expiration checks, renewal cron jobs, trial conversions, and proration calculations must strictly query `IVirtualClock.Now` rather than system `DateTime.UtcNow`.

### 7.4 Transactional Outbox Pattern for Webhooks
When an invoice is paid or subscription changes:
1. Write the business state change and the `Event` record within the **same atomic database transaction**.
2. An asynchronous background worker continuously polls for unprocessed `Event` rows, generates HMAC-SHA256 signatures, dispatches HTTP POST requests to registered webhook endpoints, and logs delivery results.

---

## 8. Action Items for Handoff & Next Steps

### For Orchestrator
- Finalize the Milestone MVP roadmap:
  - **M1:** Foundations, Project Setup, Schema, and Virtual Clock.
  - **M2:** Customers, Product Catalog, and Pricing Engine.
  - **M3:** Subscriptions State Machine, Proration Engine & Billing Portal.
  - **M4:** Invoicing Engine, Deterministic Payment Gateway & Dunning.
  - **M5:** Webhooks, Event Log & Simulation Bar.
  - **M6:** Dashboard Analytics (MRR/ARR/Waterfall) & Production Polish.

### For `Claude — Frontend / UI`
- Review the status badge design tokens and table layout specifications in this brief.
- Scaffold the application shell with responsive Left Sidebar, persistent Top Navigation, and the persistent **Simulation Bar**.
- Implement the reusable `Sheet` drawer component and tokenized URL filter bar.

### For `Codex — Backend`
- Review the relational entity-relationship model and financial invariants.
- Implement the `IVirtualClock` time-travel service as a core dependency.
- Structure the subscription state transition rules and proration formula helper.

---
*Brief authored by Antigravity (Research, Visual Ideation & Independent Review).*
