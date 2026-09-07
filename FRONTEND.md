# Frontend

Next.js (App Router) + TypeScript SPA-style client for the Money Management API.

Lives in `web/` at repo root, alongside `src/` (backend) and `tests/`.

Related docs:
- [WIKI.md](./WIKI.md) — product/business concepts
- [BACKEND.md](./BACKEND.md) — API, data model, EF Core

## Design direction (v1)

- **shadcn/ui defaults**, dark-first, neutral palette. No custom accent color yet — pick one later if needed.
- Theme toggle (light / dark / system) in the header; default is dark.
- Sidebar nav + main content layout. No top-tab navigation.

---

## Stack

| Concern | Choice |
|---------|--------|
| Framework | Next.js 15 (App Router) |
| Language | TypeScript (strict mode) |
| Package manager | npm |
| Bundler (dev) | Turbopack (`next dev --turbo`) |
| Styling | Tailwind v4 (CSS-first config) |
| Component primitives | shadcn/ui (Radix-based) |
| Server state | TanStack Query v5 |
| Client state | Zustand (only where needed — most state is server state) |
| Forms | React Hook Form + Zod |
| Charts | Recharts |
| Date handling | `date-fns` in `lib/utils/date.ts`. Displayed dates are day-first with an English-worded month via `formatShortDate` → `dd MMM yyyy` (e.g. `01 May 2026`, locale `enGB`); `formatMonthYear` → `MMM yyyy` (e.g. `May 2026`, locale `enGB`); `toIsoDateString` → `yyyy-MM-dd` is the API wire format. **Date entry uses native `<input type="date">`, so the picker's *displayed* format follows the browser/OS locale (not app-controllable); the stored/submitted value is always ISO.** |
| Number/currency formatting | `formatMoney(amount, currency)` wrapping `Intl.NumberFormat('ro-MD', { style: 'currency', currency, currencyDisplay: 'code' })` — shows the ISO code (`MDL`, `USD`, …) rather than the `L`/`$`/`€` symbol. Per-account currency since Phase 1, multi-currency transactions since Phase 4. (The legacy `formatMDL`/`formatMDLCompact` helpers were removed 2026-07-01 — `formatMoney` is the single money formatter; the module-level `formatter` const they wrapped is retained as `formatMoney`'s invalid-code fallback.) |
| Lint/format | Biome v2 |
| Unit tests | Vitest + Testing Library |
| E2E | Playwright (3–5 critical flows only) |
| API mocking (tests) | MSW v2 |

### React Specifics

- **React 19** — React Compiler handles memoization automatically. Don't add manual `useMemo`/`useCallback` for performance unless profiled.
- **Server Components** by default in the App Router. Client boundary (`"use client"`) pushed as far down the tree as possible — typically only for interactive widgets (forms, charts, dropdowns).
- **`use()`** hook for promises/context where useful; **server actions** for mutations from server components.

---

## App Structure

```
app/                             # Next.js app dir is at web/app/, not web/src/app/
  page.tsx                       # Dashboard
  layout.tsx                     # App shell with sidebar
  providers.tsx                  # ThemeProvider + QueryClientProvider
  globals.css
  accounts/page.tsx              # built: list (account-name now links to detail page) + add
                                 # dialog + "Update balance" action (Phase 4)
  accounts/[id]/page.tsx         # built: per-account detail (Server-Component shell rendering
                                 # AccountDetailView — header strip, Performance card with
                                 # YTD/All-time toggle, Balance-over-time card with interval
                                 # Select, Activity section with All/Contributions/Withdrawals/
                                 # Adjustments/Other subtabs, skeleton + 404 paths)
  transactions/
    page.tsx                     # built: list + filters (incl. transfer + adjustment tri-states) + Add + Transfer dialogs;
                                 # rows are deletable here too now (allowDelete) — per-row Trash → confirm → DELETE /transactions/{id}
    import/page.tsx              # built: PDF (maib) upload → preview (with per-row transfer toggle, Phase 3) → commit
  settings/
    page.tsx                     # built: settings index — link cards to Categories + FX rates + Data
    categories/page.tsx          # built: Categories mgmt — single expandable list (categories-manager):
                                 # add-category dialog [name/flow/colour] + edit (rename/recolour via PUT) + archive; each category row
                                 # expands to its auto-categorize keyword rules as removable chips
                                 # (Seeded=outline / Learned=secondary) + inline add-keyword.
                                 # The create form lives in components/categories/create-category-dialog.tsx
                                 # (CreateCategoryDialog [controlled open/onOpenChange + defaultFlow + onCreated] +
                                 # shared CategoryFormFields/categoryFormSchema) and is reused by the import flow.
    fx-rates/page.tsx            # built: list (with Source column: Manual/BNM badge) + add +
                                 # delete FX rates (Phase 2) + "Refresh from BNM" button that
                                 # triggers POST /fx-rates/refresh and toasts the result counts;
                                 # plus a "Backfill history" button → dialog (From defaulted to
                                 # earliest account opening + optional To) → POST /fx-rates/backfill
    data/page.tsx                # built: Data (backup & restore) — Export card (download full
                                 # JSON backup via anchor-click) + Import card (.json picker →
                                 # destructive confirm dialog → POST /data/import → toast counts)
  budgets/page.tsx               # built: list + add/edit/archive budgets for current month
                                 # + "Rebuild periods" button (POST /budgets/rebuild-all-periods)
  goals/page.tsx                 # built: list (name cell now links to detail) + add/edit/
                                 # update-saved/archive savings goals (linked + manual modes)
  goals/[id]/page.tsx            # built: per-goal detail (Server-Component shell rendering
                                 # GoalDetailView — header strip, Progress card, Pace card,
                                 # History chart, Contributions table, skeleton + 404 paths)
  pools/page.tsx                 # built: Pools tab — summary tiles (your share / outside capital /
                                 # pool value / unpaid payouts) + pools-table + "Show archived"
                                 # switch (accounts-page pattern) + create-pool dialog
  pools/[id]/page.tsx            # built: per-pool detail — value card (pool value, NAV, account
                                 # balance and unpaid-payout split), mark-staleness warning,
                                 # participants table (shares as PERCENTAGES, never unit counts),
                                 # event ledger (the one place units appear), reconciliation panel,
                                 # and every money-moving dialog (they need the roster, which only
                                 # the detail endpoint returns — the list row menu is Open + Archive)
  loans/page.tsx                 # built: Loans tab — "Owed to me" / "I owe" summary tiles (MDL,
                                 # net-worth-card-style missing-FX warning) + loans table +
                                 # Add-loan / Record-payment / Edit / Archive dialogs
  loans/[id]/page.tsx            # built: per-loan detail (async Server-Component shell rendering
                                 # LoanDetailView — header strip, Progress card, disbursement
                                 # line, Payments table w/ per-row delete, skeleton + 404 paths)
  reports/page.tsx               # built: tabbed page (Monthly Summary, Categories, Top Payees,
                                 # Balance Over Time, Year-over-Year) backed by /reports/* endpoints

src/
  components/
    ui/                          # shadcn-style primitives (hand-written for Tailwind v4);
                                 # sheet.tsx (side-anchored Dialog on @radix-ui/react-dialog) added
                                 # 2026-07-01 for the mobile nav drawer
    layout/                      # sidebar (desktop rail) + sidebar-nav (shared nav list, single
                                 # source of NAV_ITEMS) + sidebar-drawer (md:hidden mobile nav Sheet)
                                 # + header (toggle collapses the desktop rail AND opens the mobile
                                 # drawer; aria-expanded reflects drawer state) + theme-toggle
    accounts/                    # create-account-dialog (with currency Select, Phase 1),
                                 # accounts-table (with MDL-eq column, Phase 2; name cell now
                                 # links to /accounts/[id] — row-action dropdown still works
                                 # without navigating),
                                 # update-balance-dialog (3-mode: Investment / Withdrawal /
                                 # Balance adjustment — amount input for the first two, new-total
                                 # input for adjustment; POSTs /accounts/{id}/balance-changes),
                                 # detail/ — account-detail-view (top-level Client Component
                                 # owning useAccountDetail + branching loading / 404 / error /
                                 # happy-path), account-detail-header (back link, name/type/
                                 # currency badges, balance + MDL-eq, action button group:
                                 # New transfer (CryptoExchange-only; opens the shared CreateTransferDialog
                                 # with this account preset as source) /
                                 # Update balance (3-mode dialog, gated by
                                 # ADJUSTABLE_TYPES) / Archive — replaced by a single Unarchive
                                 # button when the account is archived; both states also expose a
                                 # destructive "Delete permanently" button → delete-account-dialog,
                                 # which redirects to /accounts on success and surfaces the 409
                                 # "archive instead" message on failure), performance-card (3-KPI grid
                                 # +Contributions / −Withdrawals / ±Net P&L plus Current value
                                 # line; the opening balance lives in the Activity list now, not
                                 # here; YTD ↔ All-time toggle swaps only the activity totals;
                                 # the Net P&L cell is hidden for Cash accounts — value can never
                                 # drift, so the total is structurally 0; grid collapses to 2 cols;
                                 # output missing-FX warning), balance-trend-
                                 # card (Recharts line + Daily/Weekly/Monthly interval Select,
                                 # window scales 30d/3mo/6mo with the interval, sr-only points
                                 # for tests, MDL-eq line for non-MDL accounts), activity-
                                 # section (All/Contributions/Withdrawals/Adjustments/Other
                                 # subtabs that preset the TransactionsTable filters; rows are
                                 # deletable here via TransactionsTable's allowDelete prop →
                                 # per-row Trash → confirm → DELETE /transactions/{id}; on the All
                                 # subtab a display-only "Opening balance" row (account.initialCapital
                                 # @ openingDate) is pinned at the table bottom via TransactionsTable's
                                 # pinnedFooter prop — shown only when the opening balance ≠ 0), account-
                                 # detail-skeleton + account-detail-error (404 surfaces a
                                 # distinct "Account not found." copy from generic failures)
    transactions/                # add-transaction-dialog (currency-aware amount label + transfer toggle +
                                 # optional Notes textarea),
                                 # create-transfer-dialog (any-currency source/dest; when they differ,
                                 # an editable "Destination amount" field pre-fills via GET /fx-rates/convert
                                 # and shows the effective rate; optional Notes field [transfer-notes-input]
                                 # written to both legs),
                                 # transactions-table (native currency + MDL-eq, transfer/adjustment badges;
                                 # per-row inline Category select recategorizes via PUT /transactions/{id}/category,
                                 # options filtered by row direction, disabled on transfer/adjustment rows;
                                 # per-row note: muted sub-line when present + a NoteControl pencil that opens a
                                 # dialog to edit/clear via PUT /transactions/{id}/notes [useUpdateTransactionNotes];
                                 # owning-account name shown as a muted sub-line under the description only when
                                 # listing across all accounts [no accountId filter], omitted on a single-account list),
                                 # transactions-filters (direction + transfer + adjustment tri-states),
                                 # import-upload, import-preview (per-row transfer checkbox + optional counter-account dropdown
                                 # [any currency; a cross-currency counter reveals an editable "Amount received" field
                                 # pre-filled via GET /fx-rates/convert + effective-rate line, sent as counterAmount];
                                 # learn-with-confirm: assigning an un-suggested category reveals an editable keyword
                                 # [proposeKeyword util] that ships in the commit as a learned category_patterns rule;
                                 # per-row category picker has a pinned "+ New category…" option [NEW_CATEGORY sentinel]
                                 # that opens the shared CreateCategoryDialog (flow defaulted to the row direction) and
                                 # auto-assigns the created category to that row on success — no leaving the import flow;
                                 # per-row reveal-on-demand note: a StickyNote "Add note" button in the Description cell
                                 # opens a compact textarea [openNoteRows set]; non-blank notes ship in the commit as `notes`;
                                 # PERF: each row is a memoized <ImportPreviewRow>; free-text inputs (note/counter-amount/learn-keyword)
                                 # hold a local draft and commit to the reducer on blur, so typing re-renders only that row (not all N);
                                 # rows are keyed by array index (not content): the parser intentionally keeps duplicate statement
                                 # rows [identical date/direction/amount/description], so a content-based key collides and mis-maps per-row state;
                                 # summary card shows Opening / Closing (= opening+in−out) / Net / Fees + an
                                 # "opening + in − out = closing" reconciliation (fees are info-only, already in Out);
                                 # the import counter sums ALL included rows incl. transfers so it matches the
                                 # statement totals; a row-derived block cross-checks In/Out/Fees vs the PDF header;
                                 # opening-balance reconciliation banner [non-blocking, amber, TriangleAlert]: shown above the
                                 # table when preview.reconciliation && !openingMatches — the statement's opening balance
                                 # doesn't equal the app's balance just before the period (a month-boundary gap; backend
                                 # ReconciliationDto). Commit stays enabled; renders nothing when it matches or is absent);
                                 # PERF (2026-07-01): the row list is virtualized with @tanstack/react-virtual —
                                 # dynamic measureElement (variable-height rows), index-based keys PRESERVED (the parser's
                                 # intentional duplicate rows must not collide), summary/banners/column-header/commit stay
                                 # OUTSIDE the scroll window; the <table> became an ARIA <ul>/<li> list so rows can be
                                 # absolutely positioned (screen readers announce a list of the full row count)
    settings/                    # fx-rates-table (with Source column + outline Manual/BNM
                                 # badges; BNM rows get a "will be re-fetched on next refresh"
                                 # title on the delete button), create-fx-rate-dialog (Phase 2),
                                 # refresh-bnm-rates-button (Loader2 spinner while pending,
                                 # sonner toast with insert/update/skip counts on success,
                                 # generic error toast on failure),
                                 # export-backup-card (download full JSON backup),
                                 # import-backup-card (.json picker + destructive confirm
                                 # dialog "Replace all data?" → useImportData → toast with
                                 # restored counts; surfaces ApiError inline + via toast for
                                 # 400 unsupported-schema / malformed-file)
    budgets/                     # budgets-table (built: progress bar + status pill per row,
                                 # 120% visual cap), create-budget-dialog (built: RHF/Zod,
                                 # expense-only category filter, inline 409 conflict),
                                 # edit-budget-dialog (built: PUT /budgets/{id}, limit only —
                                 # category is read-only), archive-budget-dialog (built:
                                 # confirmation dialog before soft-delete),
                                 # rebuild-periods-button (built: POST /budgets/rebuild-all-periods,
                                 # RefreshCw → Loader2 spinner, toasts rebuilt/period counts —
                                 # the in-app fix for a budget showing Spent 0 over pre-existing data)
    reports/                     # built: monthly-summary-section (grouped bar chart + table,
                                 # trailing 12 months default), category-breakdown-section
                                 # (donut + table, Expense/Income toggle, current-month default,
                                 # Uncategorized bucket), top-payees-section (rank table,
                                 # direction toggle, trailing 3 months default, limit 10),
                                 # balance-over-time-section (line chart, account Select,
                                 # Daily/Weekly/Monthly interval Select, paired MDL line via
                                 # data-show-mdl sentinel only when account currency ≠ MDL,
                                 # trailing 6 months default), year-over-year-section (pure
                                 # client transform of trailing-24-month monthly-summary —
                                 # no endpoint), reports-tabs (shell), shared date-range-picker
                                 # + direction-toggle widgets, export-csv-button (used on
                                 # /transactions, anchor-click download against
                                 # /reports/transactions.csv)
    goals/                       # goals-table (built: status pill + progress bar + 120% cap,
                                 # mode badge with linked-account name, missing-FX warning icon,
                                 # row-action menu gates "Update saved" to manual goals),
                                 # create-goal-dialog (built: RHF/Zod, name/target/optional
                                 # future-only target date, radio toggle between manual and
                                 # linked-to-account modes, account Select shown only in
                                 # linked mode, 404 inline error), edit-goal-dialog (built:
                                 # PUT /goals/{id}, lets the user toggle linked ↔ manual),
                                 # update-saved-dialog (built: PATCH /goals/{id}/manual-saved
                                 # for manual goals only — surfaces 400 if backend rejects),
                                 # archive-goal-dialog (built: confirmation dialog before
                                 # soft-delete),
                                 # detail/ — goal-detail-view (top-level Client owning
                                 # useGoalDetail + branching loading/404/generic-error/happy-path),
                                 # goal-detail-header (back link + name h1 + target/mode/archived
                                 # badges + Edit/Update-saved (manual only)/Archive actions),
                                 # goal-progress-card (Saved big number + %-of-target + 120%-cap
                                 # bar + status pill + missing-FX warning), goal-pace-card
                                 # (3-cell grid: Avg monthly / Projected completion + months
                                 # at pace / Required monthly to hit date, each with null-
                                 # disambiguating subtitle: Not enough history / Pace too slow
                                 # / Goal already met / No target date), goal-history-chart
                                 # (Recharts line over savedHistory + dashed target ReferenceLine
                                 # + conditional ReferenceDot at targetDate when in range +
                                 # sr-only point enumeration), goal-contributions-table
                                 # (Date/signed Amount/Source badge — "Manual" vs "From <linked
                                 # account>"/Notes), goal-detail-skeleton + goal-detail-error
                                 # (404 surfaces a distinct "Goal not found." from generic)
    pools/                       # pools-summary, pools-table, pool-value-now-field (the shared
                                 # "type Binance's real total right now" input), 9 dialogs
                                 # (create-pool w/ back-dated-subscription repeater, add-participant,
                                 # record-subscription, record-redemption, close-month, settle-payout,
                                 # record-cost-reimbursement, archive-pool, delete-event) and
                                 # detail/ (pool-detail-view + header/skeleton/error,
                                 # pool-value-card, mark-staleness-warning, pool-participants-table,
                                 # pool-events-table, pool-reconciliation-panel)
    loans/                       # loans-table (built: counterparty cell links to /loans/[id],
                                 # Borrowed [Received] / Lent [Given] direction badge, principal,
                                 # repaid, outstanding w/ muted MDL-eq sub-line + missing-FX icon,
                                 # 100%-capped progress bar, Active/Settled status pill, loan date,
                                 # row-action menu: Record payment [hidden when Settled] / Edit /
                                 # Archive; a "Show archived" switch above the table [accounts-page
                                 # pattern] fetches useLoans(includeArchived) — archived rows get an
                                 # Archived outline badge and their menu collapses to Unarchive
                                 # only), loans-summary (built: two tiles — "Owed to me" =
                                 # Σ outstandingMdl of Given, "I owe" = Σ of Received; sums ONLY
                                 # non-archived loans even when the toggle reveals archived rows;
                                 # when any loan can't convert, shows the partial sum + amber
                                 # warning link to /settings/fx-rates — net-worth-card's exact
                                 # treatment),
                                 # create-loan-dialog (built: RHF/Zod, fieldset/legend native radio
                                 # "I borrowed money" / "I lent money", counterparty, principal +
                                 # currency Select, date [default today UTC, max today], OPTIONAL
                                 # account Select — "No account" default, non-archived accounts
                                 # filtered to the picked currency, incompatible selection reset on
                                 # currency change, muted hint that linking records a transaction —
                                 # notes; ApiError surfaced inline role="alert"),
                                 # record-payment-dialog (built: shared by table row action +
                                 # detail header; context line "Repaying loan from X — outstanding
                                 # N", amount [Zod max = outstanding + native max], date [min =
                                 # loanDate, max today], same currency-filtered account Select,
                                 # notes), edit-loan-dialog (built: counterparty + notes only,
                                 # mirrors the PUT contract), archive-loan-dialog (built: confirm
                                 # before soft archive; when outstanding > 0 the copy gains an
                                 # amber TriangleAlert warning that archiving hides the debt from
                                 # the list/totals without settling it — settle-then-archive is
                                 # the intended flow),
                                 # detail/ — loan-detail-view (top-level Client owning useLoanDetail
                                 # + loading/404 "Loan not found."/generic-error/happy branches),
                                 # loan-detail-header (back link, counterparty h1, Borrowed-or-Lent +
                                 # Status + Archived badges, action group Record payment [hidden when
                                 # Settled] / Edit / Archive — swapped for a single Unarchive button
                                 # when archived, the accounts-detail precedent), loan-progress-card (Outstanding big number in native
                                 # currency + muted MDL-eq, "repaid X of Y" subtitle, 100%-capped
                                 # bar, missing-FX <output> warning), disbursement line ("Disbursed
                                 # via <account> on <date>" when disbursementAccountName present),
                                 # loan-payments-table (Date / signed Amount / Account [— when
                                 # unlinked] / Notes / per-row delete w/ confirm; confirm copy warns
                                 # "This will also delete the linked transaction on <account>." only
                                 # when transactionId non-null; newest-first as delivered by API),
                                 # loan-detail-skeleton + loan-detail-error
    dashboard/                   # stat-card (shared tile shell: title/icon/amount/caption +
                                 #   MissingFxNote + countLabel — the three headline tiles
                                 #   render through it so there is one skeleton + one error copy),
                                 # total-assets-card / i-owe-card / net-worth-card
                                 #   (all three read GET /dashboard/net-worth via useNetWorth();
                                 #   net-worth-card no longer sums balanceMdl client-side),
                                 # account-card (native + MDL-eq line), recent-transactions,
                                 # monthly-summary-card (built: income/expense/net + savings rate,
                                 # current UTC month, missing-FX warning),
                                 # net-worth-trend-chart (built: Recharts line, 6mo, missing-FX warning),
                                 # budget-progress (built: top 5 budgets by spend% — current
                                 # month, status pill + progress bar + view-all link, empty hint),
                                 # savings-goals (built: top 3 goals by progress% — status pill +
                                 # progress bar + Saved / Target line + view-all link, empty hint)
  lib/
    api/                         # client.ts (GET/POST/PUT/PATCH/DELETE; ApiError.message reads the
                                 # backend's RFC-7807 ProblemDetails `detail` first, then `title`,
                                 # then legacy `error`, so domain error messages surface in toasts)
                                 # + accounts (incl. useAdjustBalance + useUpdateAccount [PUT /accounts/{id};
                                 # rename + notes; invalidates ['accounts'] + ['dashboard']] + useArchiveAccount /
                                 # useUnarchiveAccount / useDeleteAccount + useAccountDetail keyed at ['accounts','detail',id] under the ['accounts'] prefix — picked up by every existing accounts-rooted invalidation for free),
                                 # transactions (incl. useCreateTransfer, isTransfer/isAdjustment filters),
                                 # categories (incl. useCreateCategory / useArchiveCategory) +
                                 # category-patterns (useCategoryPatterns / useCreate/Update/Delete),
                                 # imports, fx-rates (Phase 2 — useFxRates /
                                 # useCreateFxRate / useDeleteFxRate / useRefreshBnmRates /
                                 # useBackfillBnmRates;
                                 # the refresh mutation invalidates FOUR roots — ['fx-rates'],
                                 # ['accounts'], ['dashboard'], ['goals'] — because rates ripple
                                 # through every MDL-converted figure in the app),
                                 # budgets (built: useBudgets / useCreateBudget /
                                 # useUpdateBudgetLimit / useArchiveBudget / useRebuildBudgetPeriods —
                                 # keys rooted at ['budgets']; rebuild invalidates ['budgets']+['dashboard']),
                                 # goals (built: useGoals / useGoalDetail / useCreateGoal /
                                 # useUpdateGoal / useUpdateManualSaved / useArchiveGoal —
                                 # detail keyed at ['goals','detail',id] under the existing
                                 # ['goals'] prefix so every mutation refreshes it for free),
                                 # loans (built: useLoans [optional includeArchived, keyed under
                                 # the ['loans'] prefix] / useLoanDetail / useCreateLoan /
                                 # useUpdateLoan / useArchiveLoan / useUnarchiveLoan [POST
                                 # /loans/{id}/unarchive, invalidates ['loans'] only — no money
                                 # moves] / useRecordLoanPayment /
                                 # useDeleteLoanPayment — detail keyed at ['loans','detail',id]
                                 # under ['loans']; update/archive invalidate ['loans'] only,
                                 # while the money-moving trio — create, recordPayment,
                                 # deletePayment — invalidates SEVEN roots: ['loans'],
                                 # ['accounts'], ['transactions'], ['dashboard'], ['reports'],
                                 # ['budgets'], ['goals'] — same blanket set as useCreateTransfer,
                                 # because a linked loan movement shifts account balances which
                                 # feed dashboards, balance-over-time reports, and linked-mode
                                 # goals; applied unconditionally, account-linked or not),
                                 # dashboard (useDashboardSummary, useNetWorthTrend — keys
                                 # rooted at ['dashboard'] so transaction/import/adjust mutations
                                 # invalidate them via the shared ['dashboard'] prefix),
                                 # reports (built: useMonthlySummary, useCategoryBreakdown,
                                 # useTopPayees, useBalanceOverTime — keys rooted at
                                 # ['reports', '<sub>', params]; pure reads, no mutations),
                                 # data (built: getBackupDownloadUrl + downloadBackup —
                                 # anchor-click download of GET /data/export, no fetch — and
                                 # useImportData posting multipart to /data/import; on success
                                 # it invalidates EVERY query root (accounts, transactions,
                                 # categories, budgets, goals, fx-rates, dashboard, reports)
                                 # since a restore replaces all data).
                                 # Transaction/transfer/import/adjust mutations also invalidate
                                 # ['budgets'] so the per-category spend totals stay live, and
                                 # ['goals'] because account-balance changes feed linked-mode
                                 # goals' `saved` figure server-side.
    mocks/                       # MSW handlers + seed + store
    utils/                       # formatMoney (currency-aware), date helpers
  types/api.ts                   # mirrors backend DTOs; covers FxRateDto, transfer + adjustment fields,
                                 # AccountDto.balance/balanceMdl (live, computed on read), TransactionDto.currency/amountMdl,
                                 # LoanDirection/LoanStatus/LoanDto/LoanPaymentDto/LoanDetailDto + loan request shapes
```

---

## State Management

Strict separation of **server state** (remote data) vs **client state** (UI).

### Server state — TanStack Query

Every API resource gets a `lib/api/<resource>.ts` with typed `useQuery`/`useMutation` hooks.

```ts
// lib/api/transactions.ts
export function useTransactions(filters: TransactionFilters) {
  return useQuery({
    queryKey: ['transactions', filters],
    queryFn: () => api.transactions.list(filters),
  });
}

export function useCreateTransaction() {
  return useMutation({
    mutationFn: api.transactions.create,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['transactions'] }),
  });
}
```

Optimistic updates for: bulk re-categorize, archive toggles. (Recurring transactions were dropped from v1 — the maib statement import covers all card-side recurring debits.)

### Client state — Zustand

Only for genuinely client-side state:
- Sidebar collapsed/expanded
- Active dashboard date range filter

PDF import wizard state (upload → preview → commit) lives in component-local `useState`/`useReducer` in `app/transactions/import/page.tsx` — it's tied to one route and dies on navigation, so no global store needed.

Form state lives in React Hook Form, not Zustand.

### Forms — React Hook Form + Zod

One Zod schema per form, shared with the backend's expected payload shape. Inferred types via `z.infer<typeof schema>`.

```ts
// Add-transaction schema (current shape)
const transactionSchema = z.object({
  accountId: z.string().uuid(),
  direction: z.enum(['Income', 'Expense']),
  amount: z.number().positive(),
  transactionDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),  // yyyy-MM-dd, ≤ today
  categoryId: z.string().uuid().optional(),
  description: z.string().min(1).max(500),
  isTransfer: z.boolean().optional(),              // Phase 3
  counterAccountId: z.string().uuid().nullable().optional(),
  originalAmount: z.number().positive().optional(),
  originalCurrency: z.string().length(3).optional(),
});

// Transfer schema — source/dest may be different currencies; when they differ,
// destinationAmount (the amount received in the dest currency) is required & > 0,
// enforced in the component (the schema can't see account currencies).
const transferSchema = z.object({
  sourceAccountId: z.string().uuid(),
  destinationAccountId: z.string().uuid(),
  amount: z.number().positive(),
  destinationAmount: z.number().positive().optional(), // cross-currency only
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  description: z.string().min(1).max(500),
  categoryId: z.string().uuid().optional(),
}).refine(d => d.sourceAccountId !== d.destinationAccountId, {
  path: ['destinationAccountId'],
  message: 'Source and destination must differ',
});

// Balance-adjustment schema (Phase 4) — newBalance implicitly in the account's currency
const adjustBalanceSchema = z.object({
  newBalance: z.coerce.number(),
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  notes: z.string().max(500).optional(),
});

// FX rate schema (Phase 2)
const fxRateSchema = z.object({
  fromCurrency: z.string().regex(/^[A-Z]{3}$/),
  toCurrency: z.string().regex(/^[A-Z]{3}$/),
  rate: z.number().positive(),
  asOf: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
}).refine(d => d.fromCurrency !== d.toCurrency, {
  path: ['toCurrency'],
  message: 'Source and target currencies must differ',
});
```

---

## Theming

**Dark mode default** — finance app you'll stare at nightly.

- Tailwind v4 dark variant
- `next-themes` for persistence + system preference detection
- Theme: `light` / `dark` / `system` — toggle in Settings → Appearance
- Color palette uses CSS custom properties so shadcn/ui components pick up theme automatically

```tsx
<ThemeProvider attribute="class" defaultTheme="dark" enableSystem>
  {children}
</ThemeProvider>
```

---

## Charts

Recharts wrappers in `components/charts/` — typed to our DTOs, never raw Recharts in pages.

Standard charts:
- `<NetWorthLineChart />` — single line, last N months
- `<BalanceOverTimeChart />` — multi-line per account
- `<MonthlySummaryBars />` — grouped bars (income, expense, net)
- `<CategoryPieChart />` — donut, with legend
- `<BudgetProgressBar />` — for inline use in cards

All charts: explicit `width`/`height` to prevent CLS; `ResponsiveContainer` only where needed.

---

## Accessibility (WCAG 2.2 AA)

- Focus indicators ≥ 2px outline, sufficient contrast (criterion 2.4.11)
- Interactive targets ≥ 24×24 CSS pixels (criterion 2.5.8)
- All forms have associated `<label>`s; error messages linked via `aria-describedby`
- Charts include a textual summary for screen readers (`role="img"` + `aria-label`)
- Keyboard navigation tested for every page (no mouse-only flows)
- Automated audit via `@axe-core/playwright` in E2E tests

---

## Performance

Targets:
- **LCP** < 2.5s
- **INP** < 200ms
- **CLS** < 0.1 (explicit dimensions on all images, charts)

Strategy:
- Server Components for data fetching; stream with `<Suspense>`
- Route-based code splitting (automatic with App Router)
- Recharts is heavy — lazy-loaded on report pages only
- Import preview list virtualized with `@tanstack/react-virtual` (dynamic row measurement) so 900+ row statements stay smooth (shipped 2026-07-01). The main transactions list is a candidate for the same treatment past 100 visible rows (not yet virtualized).

---

## Testing

### Unit / Component
- **Vitest** + **Testing Library** for components and custom hooks
- Coverage target: 85%+ on components and hooks, 70%+ on utils

### E2E (Playwright)
Keep to **3–5 critical flows**:
1. **PDF import** (built) — upload maib statement → see preview with duplicates flagged → exclude a row → commit → land on `/transactions` with success toast
2. Add a transaction → appears in list → dashboard net worth updates
3. Create a budget → exceed it → status bar turns red
4. Create a savings goal → update saved amount → progress bar advances
5. Filter transactions by date + category + account → export to CSV

Selectors: prefer `data-testid` and ARIA roles. Avoid CSS-based selectors.

### jsdom polyfills (Radix-in-Vitest)

Radix UI's Select, Dialog, etc. call `Element.hasPointerCapture`, `Element.scrollIntoView`, and `ResizeObserver`, none of which jsdom implements. `tests/setup.ts` polyfills those before the first test runs — required for any test that opens a Radix popover/dialog.

### Mocking
**MSW v2** — used **for tests only** (runtime mocks were removed when the backend slices landed). Slimmed handler list lives in `tests/mocks/handlers.ts`, loaded by `tests/setup.ts` via `src/lib/mocks/server.ts`. Browser dev mode hits the real backend at `NEXT_PUBLIC_API_BASE_URL` (default `http://localhost:5179`). Playwright specs don't share these handlers — the import-flow spec is the only e2e test and it mocks via Playwright's own routing if needed.

---

## API Contract

Generated types via OpenAPI (Scalar exposes the schema at `/scalar/openapi.json`).

**Codegen from OpenAPI is the default.** Backend exposes `/openapi/v1.json` (via Scalar). Generate types:

```bash
npm run gen:api    # runs openapi-typescript against http://localhost:5000/openapi/v1.json -> types/api.ts
```

Run this when the backend is running locally. Generated file is committed so the frontend builds without backend running. For endpoints not yet implemented in the backend, hand-write types under `types/mock.ts` and mark with a TODO referencing the backend ticket — fold them into `types/api.ts` once the endpoint exists.

---

## Dev Workflow

```bash
# install
npm install

# run (assumes backend already running on :5000)
npm run dev          # next dev --turbo on :3000

# typecheck (no emit, fast)
npm run typecheck    # tsc --noEmit

# lint/format
npm run lint
npm run format

# tests
npm test             # vitest
npm run test:e2e     # playwright
```

CORS: backend allows `http://localhost:3000` in development (`AddCors` + `UseCors` in `Program.cs`).

**API base URL**: set in `web/.env.local`:

```
NEXT_PUBLIC_API_BASE_URL=http://localhost:5179
```

The fetch client in `src/lib/api/client.ts` prepends this to every relative path. Leave empty if you ever want to re-enable a same-origin proxy.

---

## MCPs (Development Tooling)

### Playwright MCP (project-scoped)

Wired up in the committed `.mcp.json` at the repo root via `@playwright/mcp@latest` (Microsoft's official server). On first run, `npx` fetches the package and Playwright downloads its Chromium bundle into the per-user cache — one-time, no manual install. Approve the project-scoped MCP trust prompt the first time you open the repo in Claude Code on a new machine. Used to:

- Drive the Next.js app end-to-end while iterating (`browser_navigate`, `browser_snapshot`, `browser_click`, `browser_fill_form`, …)
- Capture console errors after a change (`browser_console_messages`)
- Verify a feature actually works in the browser before marking a task done — this is the per-change smoke test mandated by [QA.md](./QA.md)

### MCPs explicitly NOT needed

| MCP | Reason skipped |
|-----|----------------|
| Chrome DevTools | Playwright already covers browser inspection |
| Gmail / Drive / Calendar | Not relevant to a self-hosted finance app |

### Claude Code agents

Per the project-root [CLAUDE.md](./CLAUDE.md):

- Frontend changes (anything in `web/`) go through the **`frontend-developer`** agent — tuned for React 19 + Next.js 15 + TanStack Query + Tailwind v4 + WCAG 2.2.
- Backend changes go through the **`c-sharp-pro`** agent — see [BACKEND.md](./BACKEND.md).

When a change spans both stacks, dispatch both agents in parallel from the main thread and collate their reports.

---

## Pools UI

The pool screens are the one place in the app where the numbers on screen are **not all the user's
money**, so several conventions exist only here.

- **Vocabulary: "share" and percentages, never "units".** The unit model is what makes the arithmetic
  exact, but "units" is alien vocabulary for a personal-finance app. `formatUnits` / `formatNav` are
  used in exactly two places — the **event ledger** (an audit trail, where unit counts are the point)
  and the unit-drift row of the reconciliation panel. A test asserts the participants table contains
  no unit counts at all.
- **"Your share" is derived by subtraction** (`poolValue − outsideCapital`), not by
  `poolValue × ownerFraction`. The two are equal in principle, but per-participant stakes are rounded
  independently, so multiplying would let the design's deliberate one-cent residual appear between two
  figures printed side by side. The summary percentage is MDL-weighted across pools, and renders `—`
  rather than a fabricated 0% when nothing is convertible.
- **Every money-moving dialog hard-blocks on `poolValueNow`** — Binance's real total, typed at the
  moment money crosses the boundary, "across spot/futures/earn/fiat, **including BNB**". The client
  refuses to issue the request without it. `pool-value-now-field.tsx` is the single shared input so
  the wording and the rule cannot drift between dialogs. The one exception is **cost reimbursement**,
  where no cash crosses the boundary and the backend validator has the field nullable — it is
  labelled optional-but-recommended, and supplying it re-marks the pool first.
- **Close and Settle are visibly two steps.** The close dialog says in as many words that it marks the
  month and records what is owed but **moves no money**; settling is a separate action on the ledger
  row, taken the day the transfer actually lands.
- **The close dialog's preview is computed client-side; the server stays the authority on amounts.**
  `previewPoolValuation` (`src/lib/utils/pool.ts`) mirrors `PoolUnitRegister.SnapshotAsOf` —
  `poolValue = typed total − unpaidDistributionCash`, NAV guarded on both the dust tolerance and
  positivity, capital base floored where consumed, owner excluded — and reprices every row on each
  keystroke. This is not duplication for its own sake: the dialog previously gated on the server's
  `distributable`, which is priced against the *previous* mark, so after any close (which sweeps every
  stake back to basis) the button stayed disabled and **no payout could ever be made from the UI**.
  The request body still sends only the participant selection; expect the preview and the server to
  differ by a cent, and the copy says so.
- **The close dialog chooses *who* gets paid, never *how much*.** The `distributable` figures on
  screen were priced against the *previous* mark and go stale the instant a new total is typed, and a
  `cash` above someone's freshly-recomputed distributable is a hard 409. So the UI sends the
  participant selection and lets the server compute the amounts. **Consequence: partial payouts are
  not expressible from the UI** — which is fine, because "reinvest this month" is simply leaving a
  participant unchecked.
- **A losing month is not closed here.** `POST /pools/{id}/distributions` fails with
  `pools.distribution_nothing_to_pay` before saving when nothing is payable, so the dialog detects
  that state up front and points at **Update balance** on the account instead (an Adjustment dated
  today is explicitly still permitted on a pooled account). The friends' stakes fall pro-rata with the
  mark, which is the intended behaviour.
- **A zero `distributable` is spelled out, not left as "0.00"** — "nothing payable, still at or below
  their capital base". A green month paying zero is the single most disputable behaviour in the whole
  arrangement, so the UI explains it rather than printing a bare number. The **owner's** distributable
  is rendered as "Stays in the pool" rather than an amount, because the owner's seed carries no cash
  basis and the raw figure would read as an offer.
- **The reconciliation panel renders only when something is wrong**, across all **five** finding
  classes (unmatched transactions, unit drift, value drifts, unbacked cash claims, and the balance
  identity). A permanently-green panel is noise, and noise gets ignored. The trade-off is that a clean
  pool gives no positive confirmation the tripwire ran.
- **The balance finding leads, and reads in two directions.** Added 2026-09-07 after a live pool reached
  3,000 units against a 2,000 balance and the panel said nothing. *Ledger claims more* means shares were
  issued for money that never arrived; *account holds more* means cash is being split pro-rata as though
  the pool earned it. They are different problems and are worded differently — the gap is shown
  **unsigned** with the direction carried by the title, the label and `data-direction`, because a bare
  negative next to two positive balances is what gets read backwards on the day it matters.
- **Its remedy deliberately does not say "record money in".** `RecordSubscription` always writes its own
  movement leg, so using it to repair a missing arrival would mint a *second* set of shares. The fix is a
  transaction on the account, dated the day the money actually arrived — the date matters, because the
  unbacked-claim matcher pairs on exact date, direction and amount, so a wrong date clears the balance
  check while leaving the claim standing.
- **One cause can raise two findings**, so the panel relates them arithmetically rather than presenting
  them as unrelated failures: an unbacked claim moves *predicted*, an unmatched row moves *derived*, and
  when what they jointly explain ties to the drift the panel says so outright.
- **Mark staleness** warns amber past 7 days and red past 30 (`MARK_STALE_DAYS` / `MARK_CRITICAL_DAYS`,
  exported constants). The banner is a nudge, not the safety mechanism — the actual protection is that
  every capital event hard-blocks on a freshly typed total.
- **Pooled accounts elsewhere**: `/accounts` rows and the account-detail header carry a **"Pooled"
  badge** from `AccountDto.isPooled`, and the account keeps showing its **full** balance by explicit
  decision. The **Performance card on that page is owner-only**, and says so — otherwise it would book
  the friends' capital as the user's contributions and their payouts as the user's withdrawals.
- **A pool has three exits, and the UI must not let them blur.** *Archive* winds down a real pool:
  it needs zero outside units, keeps the ledger and roster, and stays drillable — and it is now
  **reversible** via Unarchive, which re-arms every guard on the account. *Delete* is for a pool
  created by mistake — one that never moved anyone's money — and removes it entirely; a pool that
  has moved cash is refused with `pools.delete_has_movements`, surfaced inline as guidance to
  archive instead rather than as a failure. **Deleting a pool never touches the account's
  transactions**, including any balance adjustment the pool wrote, and the confirm says so — a
  user assuming their balance rolls back would be badly wrong.

**Known gap:** `unpaidDistributionCash` has no MDL equivalent on `PoolDto`, so the "Unpaid payouts"
tile can only total across pools that share a currency; otherwise it renders "Multiple currencies".
Adding `PoolDto.UnpaidDistributionCashMdl` would close it.

## Account Model UI summary

| Phase | UI surface | Where |
|-------|-----------|-------|
| 1 — taxonomy + currency | 7-type Select + currency Select in create-account dialog; account-type labels in table + cards | `accounts/create-account-dialog.tsx`, `accounts/accounts-table.tsx`, `dashboard/account-card.tsx` |
| 2 — FX rates + MDL-eq | New Settings → FX rates page (table with Source column showing Manual/BNM origin badges + add dialog + delete + "Refresh from BNM" button that POSTs to `/fx-rates/refresh` and toasts the insert/update/skip counts); MDL-eq column on accounts; muted MDL-eq line on account cards; net-worth card sums MDL-eq with missing-rate warning link | `app/settings/fx-rates/`, `components/settings/` (incl. `refresh-bnm-rates-button.tsx`), `dashboard/net-worth-card.tsx` |
| 3 — transfers | New "New transfer" dialog (MDL-only v1) next to "Add transaction"; transfer-flag toggle on add-transaction dialog (with counter-account picker); transfer tri-state filter on transactions page; Transfer badge + muted styling on transfer rows; per-row "Transfer" checkbox in import preview seeded by backend auto-suggest, plus an OPTIONAL counter-account dropdown next to it. Leave blank for A2A between accounts that both have PDFs (the other statement supplies the matching leg). Pick a counter to auto-create a matching leg on the chosen account — useful for ATM → Cash, Salary → XTB/Binance/Fagura, or any transfer where the destination has no statement. Dropdown is filtered by same currency, excludes the import account and archived accounts; no preselection, no submit blocking | `transactions/create-transfer-dialog.tsx`, `transactions/transactions-filters.tsx`, `transactions/transactions-table.tsx`, `transactions/import-preview.tsx` |
| 4 — multi-currency tx + balance adjustments | Amount label in add-transaction reflects selected account's currency; "Update balance" action in accounts-table row menu (only for Brokerage/CryptoExchange/P2PLending/BankDeposit) opens the balance-adjustment dialog; adjustment badge + muted styling on adjustment rows; adjustment tri-state filter; transactions display native currency with optional MDL-eq secondary line | `accounts/update-balance-dialog.tsx`, `accounts/accounts-table.tsx`, `transactions/transactions-table.tsx`, `dashboard/recent-transactions.tsx` |

**Aggregate-side trap**: any client-side or server-side income/expense aggregation must filter both `isTransfer === true` AND `isAdjustment === true`. `MonthlySummaryCard` consumes `/dashboard/summary`, which excludes transfers and adjustments server-side — no client-side filtering needed.

> **Corrected 2026-09-06.** This note used to claim that (a) net-worth-card "sums account balances (not transactions) so it's safe" and (b) `/dashboard/net-worth-trend` "already excludes transfers and adjustments server-side". Both were wrong. The trend deliberately includes transfer and adjustment rows — they move real balances — and pins that behaviour with `NetWorthTrendFilterDisciplineTests`, which exists precisely to stop someone "fixing" the code to match this paragraph. And the client-side sum was not safe: it counted borrowed money as wealth. There is no client-side money aggregation on the dashboard any more; all three headline tiles read a single server-computed `NetWorthDto`.
