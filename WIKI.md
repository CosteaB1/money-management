# Money Management — Project Wiki

A personal finance web app for tracking income, expenses, account balances, budgets, and savings goals. Single-user, self-hosted, no third-party sync.

This file covers the **product**: what the app does and the business concepts it works with.
For implementation, see:
- [BACKEND.md](./BACKEND.md) — architecture, data model, EF Core, Postgres
- [FRONTEND.md](./FRONTEND.md) — Next.js stack, theming, testing, accessibility

---

## Project Constraints (v1)

| Decision | Value | Rationale |
|----------|-------|-----------|
| Users | Single user, no auth | Self-hosted, trusted network only |
| Currency | Multi-currency (per-account ISO code) | Each account carries its own ISO currency (MDL/USD/EUR/RON/GBP/…); MDL is the reporting currency. Currency is open ISO-4217 data (any `^[A-Z]{3}$`), not an enum — the create-account/FX-rate dropdowns just pick which codes to offer, and BNM auto-fetch pulls rates for every currency BNM publishes. FX conversion lands in Phase 2 — see roadmap below. |
| Deployment | Local dev (`dotnet run` + `next dev`) | No Docker/host decisions yet |
| Database | PostgreSQL (EF Core code-first) | Production-grade ORM ergonomics; snake_case schema |
| Locale | Moldova (MDL, `ro-MD` / `en-MD`) | Currency formatting, date format |

---

## Account Model Roadmap

The account model is being expanded in four phases to cover a range of real-world account types — current/card accounts, term deposits, brokerage, crypto exchanges, and P2P lending.

| Phase | Scope | Status |
|-------|-------|--------|
| **1 — Account taxonomy + per-account currency** | Expand `AccountType` to 7 values; allow any 3-letter ISO currency per account; drop the MDL-only hard-coding. | **Done** (2026-05-19) |
| **2 — FX rates + MDL-equivalent view** | `FxRate` entity + `IFxConverter`; manual rates first, BNM (bnm.md) integration later. Compute MDL-equivalent on `AccountDto` for the dashboard net-worth aggregate. (`TransactionDto.AmountMdl` deferred to Phase 4.) | **Done** (2026-05-19) |
| **3 — Internal transfers** | `Transaction` carries `IsTransfer` + `CounterAccountId` (simple-flag model). Income/Expense aggregates exclude transfer rows so paired statement imports don't double-count. Manual creation via `POST /transfers` makes two opposing legs atomically. Import preview auto-suggests the flag via `ITransferDetector` (whole-token match on `A2A` / `Retragere` / `ATM` — the generic `Transfer` token is intentionally NOT a signal, since maib stamps "Transfer" on salary and payments too; prefix-matched exclusions `Achitare` / `Plată` / `Salariu` / … so inflected forms like `Salariul` are still excluded). | **Done** (2026-05-19) |
| **4 — Balance adjustments + multi-currency transactions** | `Transaction.IsAdjustment` flag (mutually exclusive with `IsTransfer`); `POST /accounts/{id}/balance-adjustments` computes delta against current balance and persists a single Income/Expense leg flagged `IsAdjustment`. Permitted on Brokerage / CryptoExchange / P2PLending / BankDeposit. `Transaction.SupportedCurrency = "MDL"` constraint removed — each transaction matches its account's currency. `TransactionDto` gains `Currency` + `AmountMdl` (FX-converted at the transaction date). | **Done** (2026-05-19) |

---

## Build Status

Last updated: **2026-05-28**

### Done

**Backend** — `src/` + `tests/`
- Clean Architecture skeleton: 5 src projects + 2 test projects, dependency direction enforced
- SharedKernel: `Entity`, `Result<T>`, `Error` + `ErrorType`, `IDomainEvent`, `IDomainEventHandler<T>`, `IDateTimeProvider`
- **Account slice** — `Account` entity with factory + validation, `Money` value object, `AccountType` enum (Cash, CreditCard, BankCurrent, BankDeposit, Brokerage, CryptoExchange, P2PLending), per-account ISO currency, `CreateAccount`/`GetAccounts`, endpoints `POST /accounts` + `GET /accounts`. `AccountDto` carries a single `balance` (live, computed on read as anchor + Σ income − Σ expense across all non-deleted rows) and `balanceMdl` (MDL-equivalent, nullable when no FX rate is available). The maib parser splits any row that has a `comision` column into two paired entries (principal = `ieșiri − comision`, fee = `comision`, category `Bank Fees`) so their sum equals the bank's actual per-row deduction; the live balance then naturally reconciles against maib's `Sold Disponibil` without any special filtering.
- **FxRate slice** — `FxRate` entity + `IFxConverter` (direct + inverse lookup; identity short-circuit; null when no usable rate ≤ asOf), `EfFxConverter`, endpoints `GET /fx-rates` + `POST /fx-rates` + `DELETE /fx-rates/{id}`. No rates are seeded — the table starts empty and is populated by manual entry (`POST /fx-rates`) or the BNM auto-fetch / backfill. Reporting currency `MDL` is centralized in `ReportingCurrencies.Mdl`; ISO validation lives in `CurrencyCodes.IsValidIso` shared by `Account` and `FxRate`.
- **BNM auto-fetch (FxRate `Source` enum)** — `FxRate` now carries a `Source = Manual | BnmAuto`. `IBnmRateProvider` (implemented by `BnmRateProvider` over `HttpClient` against `https://www.bnm.md/en/official_exchange_rates?get_xml=1&date=DD.MM.YYYY`) pulls daily MDL rates from the Banca Națională a Moldovei XML feed; `BnmAutoFetchService` (`BackgroundService`) does a 30-day backfill on startup + a daily refresh (both configurable under `Fx:AutoFetch`). Only currencies actually held by user accounts get pulled. `RefreshBnmRatesCommandHandler` orchestrates fetch → dedup → upsert; **manual rates always win** (`EfFxConverter` ties tied-date rows with `ThenBy(Source)` so `Manual = 0` outranks `BnmAuto = 1`). New endpoint `POST /fx-rates/refresh` triggers an on-demand pull and returns insert/update/skip counts. The `source` column has `defaultValue: "Manual"` (enforced via `HasDefaultValue(FxRateSource.Manual)` in `FxRateConfiguration` so the enum-read path is safe) and the unique index is `(from_currency, to_currency, as_of, source)` — two physical rows for the same (from, to, asOf) triple are allowed when sources differ.
- **Category slice** — hierarchical (self-ref `parentId`), `CategoryFlow` enum (Expense/Income/Both), `CreateCategory` / `GetCategories` / `UpdateCategory` / `ArchiveCategory`, endpoints `POST /categories` + `GET /categories` + `PUT /categories/{id}` (rename/edit name·flow·colour) + `DELETE /categories/{id}` (soft archive). Auto-categorization keyword rules live in the `category_patterns` table (DB-backed, seeded from the original suggester rule set), managed via `GET/POST/PUT/DELETE /category-patterns` and the **Settings → Categories** screen (expandable list: create/archive categories + add/remove each category's keyword rules as chips)
- **Transaction slice** — `Transaction` aggregate with `Money` (currency matches the parent account's currency — multi-currency since Phase 4), `TransactionDirection` (Income/Expense), `TransactionSource` (Manual/Imported), nullable `CategoryId`, optional `OriginalAmount` + `OriginalCurrency` for FX rows, `ImportBatchId` traceability, `IsTransfer` + `CounterAccountId` (Phase 3) so internal transfers stay out of income/expense aggregates, `IsAdjustment` (Phase 4) for monthly balance true-ups on investment accounts. `IsTransfer` and `IsAdjustment` are mutually exclusive. Soft delete via `HasQueryFilter`. Endpoints `POST /transactions` + `GET /transactions?accountId=&from=&to=&categoryId=&direction=&isTransfer=&isAdjustment=` + `DELETE /transactions/{id}` + `PUT /transactions/{id}/category` (recategorize a single row; rejects a category whose flow conflicts with the row direction) + `POST /transfers` (creates the two paired legs in one transaction; an optional note is stored on both legs) + `POST /accounts/{id}/balance-changes` (3-mode: Investment / Withdrawal / Balance adjustment). `TransactionDto` carries `AmountMdl` (FX-converted at the transaction date; null if no rate). Only the **transaction date** is stored — the bank's *processing date* is dropped at the parser boundary since card payments debit instantly.
- **Statement Import slice** — `ImportBatch` entity (filename + sha256 hash + bank source + counts), `IBankStatementParser` strategy with `MaibStatementParser` (PdfPig 0.1.10), `ICategorySuggester` (keyword rules read from the DB-backed `category_patterns` table), `ITransferDetector` (Phase 3, whole-token inclusion `A2A` / `Retragere` / `ATM` — the generic `Transfer` token was dropped because maib labels salary & payments "Transfer …" too; prefix-matched exclusions `Achitare` / `Plată` / `Salariu` / `MIA` / `Cashback` so inflected forms like `Salariul` are caught), duplicate detection via signature `sha256(accountId|date|amount|normalizedDescription)` taken as a snapshot of existing DB rows (not mutated during the import loop, so legitimately-repeated rows in one batch — e.g. two same-day same-amount ATM withdrawals — are both kept). Parser emits a paired `Comision: …` Expense row whenever a maib line has both `ieșiri` and `comision` columns populated, so bank fees are never silently dropped. Endpoints `POST /imports/parse` (multipart preview, carries per-row `isTransfer` auto-suggestion) + `POST /imports/commit` (transactional bulk insert, per-row `isTransfer`/optional `counterAccountId` for cases like ATM → Cash or Salary → Brokerage where the destination has no PDF, plus an optional per-row `notes` annotation persisted on the source row). Parser strips card/account section markers (`999999******0000 #Cardul`, `… #Cont`) before row extraction so boundary rows (last row of a section, last row of a page) aren't dropped — earlier versions silently lost ~3 transactions per statement.
- **Category seeding** — `CategorySeeder` hosted service inserts 10 default categories with deterministic GUIDs (9 original + `Balance Adjustment` (`...00000000a`, flow `Both`) for Phase 4 balance-adjustment rows, plus `Investment` (`...00e`) and `Withdrawal` (`...00f`) for the 3-mode balance-change action). The generic `Other` is split into `Other expenses` (`Expense`) + `Other income` (`...010`, `Income`); `Home` (`...011`, `Expense`) is the home-expenses bucket. The seeder also backfills any missing default by id on existing DBs so adding new defaults (like `Home`) doesn't require dropping the categories table — they appear on the next startup.
- **Dashboard slice** — read-only projections that feed the existing dashboard widgets. `GET /dashboard/summary?month=YYYY-MM` returns the calendar-month income/expense aggregate (FX-converted to MDL per-row at the transaction's date), savings rate, transaction count, and a `missingFxRate` flag. `GET /dashboard/net-worth-trend?months=N` (default 6, max 24) returns oldest-first points: previous month-ends + a live "today" point, summing each non-archived account's native balance converted to MDL at that as-of date. Both endpoints filter `IsTransfer = false` AND `IsAdjustment = false` (and `!IsDeleted` defensively) when reading transactions. `DashboardErrors.MonthsOutOfRange` lives in Application (Dashboard has no entity in Domain).
- **Budget slice** — `Budget` entity (one-active-per-category, MDL-only `MonthlyLimit`) + sibling `BudgetPeriod` (per-year-month spend rollup). Endpoints `POST /budgets`, `GET /budgets?year=&month=` (default = current UTC month), `PUT /budgets/{id}`, `DELETE /budgets/{id}` (soft archive). `BudgetDto` carries `Spent`, `Remaining`, and a precomputed `Status` enum (`OnTrack` <80%, `Warning` 80–100%, `Over` >100%) so the UI just colors what the server tells it. Active-budget uniqueness is enforced both by a handler pre-check (`BudgetErrors.AlreadyExistsForCategory` → HTTP 409) and a partial unique index `(category_id) WHERE is_archived = false`.
- **Domain-event handler pattern (first use)** — the Budget slice introduces `TransactionCreatedDomainEvent` (raised from `Transaction.Create`, which now takes `amountMdl` as a parameter — every call site FX-converts via `IFxConverter` before constructing the transaction). The first `IDomainEventHandler<T>` in the project lives at `Application/Features/Budgets/EventHandlers/UpdateBudgetPeriodOnTransactionCreatedHandler.cs`. It find-or-creates the matching `BudgetPeriod` and accumulates `Spent` reactively, skipping income, transfers, adjustments, uncategorized rows, and rows with no FX-convertible MDL value. The dispatcher fires after `SaveChanges` (same `DbContext` scope), then saves again for the budget-period mutation.
- **SavingsGoal slice** — `SavingsGoal` entity supports two modes in one row: **linked** (`LinkedAccountId` set → `Saved` is computed live as that account's MDL-converted balance via `IFxConverter`) or **manual** (`ManualSavedAmount` stored as a paired value+currency pair, since EF Core 10's nullable `ComplexProperty` couldn't model `Money?`). Endpoints `POST /goals`, `GET /goals`, `GET /goals/{id}` (detail), `PUT /goals/{id}`, `PATCH /goals/{id}/manual-saved` (rejects in linked mode; also writes a `SavingsGoalContribution` row capturing the signed delta), `DELETE /goals/{id}`. `GoalDto` carries `Saved`, `Remaining`, `ProgressPercent`, a precomputed `Status` (`OnTrack`/`AtRisk`/`Achieved`/`Behind` — pace = saved vs `target × monthsElapsed / monthsTotal`, AtRisk if below 90% of pace), `RequiredMonthlyContribution` (null when no target date or already achieved), `IsLinkedMode`, and `MissingFxRate`. FK to `accounts` uses `Restrict` on delete so a linked account can't be removed without unlinking first.
- **SavingsGoalContribution + goal detail** — second entity `SavingsGoalContribution` (`GoalId`, signed `Money` amount in MDL, `OccurredOn`, `Notes`) is a time-series table for **manual-mode** goals only. `UpdateManualSavedCommandHandler` now writes a row whenever the new total differs from the previous (positive = contribution, negative = withdrawal; zero-delta is skipped). `GET /goals/{id}` → `GoalDetailDto` adds: `CreatedOn`, `IsArchived`, a `Contributions[]` list (manual → DB rows; **linked** → derived per-row from the linked account's transactions, FX-converted at the row's date, `Source = LinkedAccountTransaction`), a `SavedHistory[]` monthly cumulative series (manual: running sum; linked: per-month-end balance of the linked account), and a `Pace` block (`avgMonthlyContribution` over the last 90 days, `projectedCompletionDate` and `monthsToAchieveAtPace` — null when pace ≤ 0 or already achieved; clamped at 50 years). Math is shared between `GetGoalsQueryHandler` and `GetGoalDetailQueryHandler` via a `GoalProjection` static helper so the two handlers can't drift. The `savings_goal_contributions` table has FK `ON DELETE CASCADE` and `ix_savings_goal_contributions_goal_id_occurred_on (goal_id, occurred_on DESC)`.
- **Reports slice** — five read-only endpoints projecting over Transactions + Accounts; same filter discipline as Dashboard (`!IsDeleted && !IsTransfer && !IsAdjustment` for income/expense aggregates). `GET /reports/monthly-summary?from=YYYY-MM&to=YYYY-MM` → array of points with the same shape as `DashboardSummaryDto` (income/expense/net/savingsRate/count/missingFxRate); defaults to the trailing 12 months when both params omitted, capped at a 24-month span. `GET /reports/category-breakdown?from=YYYY-MM-DD&to=YYYY-MM-DD&direction=Expense|Income` → `{ from, to, direction, totalMdl, missingFxRate, items[] }` sorted by `amountMdl` desc with an `Uncategorized` bucket (`categoryId = null`) for null categories; percentages sum to 1.0. `GET /reports/balance-over-time?accountId=&from=&to=&interval=Daily|Weekly|Monthly` (default `Monthly`) → per-interval native balance + nullable `balanceMdl` per as-of date. **Does NOT filter transfers/adjustments** — they DO move the per-account native balance even though they're excluded from income/expense aggregates; this is the slice's one intentional break from the otherwise-uniform filter rule, pinned by a unit test. `GET /reports/top-payees?from=&to=&direction=&limit=10` → array sorted by `amountMdl` desc, grouped by normalized description (`trim().ToLowerInvariant()`), `originalDescription` carries the first occurrence's raw casing for display. `GET /reports/transactions.csv?<same filters as /transactions>` → streaming RFC 4180 CSV with header `transaction_date,account,category,direction,amount,currency,amount_mdl,description,is_transfer,is_adjustment`; UTF-8 without BOM; account/category names resolved via single batched join; written directly to `Response.Body` via `StreamWriter` (no intermediate buffer). `ReportsErrors` colocated with the slice mirrors `DashboardErrors`.
- **DataPortability slice** — full-database JSON backup + destructive restore (Settings → Data). Entity-less, no schema change, no migration. `IBackupStore` (Application abstraction, same split as `IFxConverter`) implemented by `EfBackupStore` in Infrastructure — the one place with `Database`/transaction/`ExecuteDelete`/`IgnoreQueryFilters` access. `BackupDocument` (`SchemaVersion = 4` + `ExportedAtUtc` + flat `*Backup` arrays for 9 entities — accounts, categories, **category_patterns**, transactions, importBatches, budgets, budgetPeriods, savingsGoals, savingsGoalContributions) is a **faithful snapshot** of those tables: archived + soft-deleted rows included, exact IDs/audit/flags preserved. `fx_rates` is the one intentional exclusion (re-fetchable from BNM). *(category_patterns was added in v4 on 2026-05-29 — before that, a restore silently lost all learned keyword rules via the `categories` cascade.)* `GET /data/export` streams the JSON as a file download (same shape as `/reports/transactions.csv`, enums as names via the shared `JsonOptions`). `POST /data/import` (multipart `file`, 50MB cap, antiforgery disabled) deserializes → `ImportDataCommand` validates `SchemaVersion` (`DataErrors.UnsupportedSchemaVersion`) + non-null arrays (`MalformedBackup`) → `IBackupStore.RestoreAsync`. Restore is **destructive + transactional**: one transaction, wipe child-first (`ExecuteDeleteAsync` w/ `IgnoreQueryFilters`), reinsert parent-first via raw parameterized `NpgsqlParameter` INSERTs (table from `context.Model`, enums via `.ToString()`, `timestamptz` UTC-coerced) — bypasses domain factories so IDs/audit survive; any failure rolls back. The categories wipe nulls all `parent_id`s first because that self-ref FK is `ON DELETE RESTRICT` (non-deferrable in Postgres). Handler validation is unit-tested with a substituted `IBackupStore`; the EF store is manually verified (no Infrastructure test project — same deviation as `BnmRateProvider`). See Backend > DataPortability slice.
- Application infra: custom CQRS interfaces, `ValidationDecorator` + `LoggingDecorator` (Scrutor), `IApplicationDbContext`, `IDomainEventsDispatcher`
- Infrastructure: `ApplicationDbContext`, EF configs (snake_case, `ComplexProperty` for `Money`, enum-as-string, soft-delete `HasQueryFilter`), `DomainEventsDispatcher`, `AuditableEntitySaveChangesInterceptor`, `DateTimeProvider`
- Api: `Program.cs` with Serilog + Scalar, `IEndpoint` scan, `GlobalExceptionHandler`, `ResultExtensions`, **CORS** allowing `http://localhost:3000`, **`ApplyMigrations()`** on dev startup (creates DB + applies pending migrations, and logs the active DB name)
- **Real vs. test DB split**: the running app uses two databases on local Postgres — `money_management` (real daily data; the default/Visual-Studio Run profiles) and `money_management_test` (disposable QA/smoke-test DB; the **`qa`** launch profile). The split is by connection string per launch profile only; same code, same schema. See BACKEND.md > Local Postgres and QA.md.
- Account edit endpoint: `PUT /accounts/{id}` (`UpdateAccountCommand` → `Account.Update(name, notes)`) renames an account and edits its notes — name + notes only; currency, type, balance, and opening date are fixed at creation. 204 on success, 404 if missing, 400 on validation error. No schema change, no domain event (name/notes don't feed budgets/reports)
- Account archive endpoint: `DELETE /accounts/{id}` (soft archive via `Account.IsArchived`); unarchive via `POST /accounts/{id}/unarchive` (idempotent boolean flip, no migration); guarded hard delete via `DELETE /accounts/{id}/permanent` (409 if the account has any transactions, imports, or linked goals — archive instead)
- **All entity IDs use `Guid.CreateVersion7()`** (RFC 9562 UUIDv7) — sortable by creation time, sequential B-tree inserts. Native `uuid` column in Postgres, no schema change vs v4.
- **Single EF Core baseline migration** `20260528201519_InitialCreate` — generated via the EF CLI; creates the whole schema (all 10 tables with indexes/FKs/`Money` complex properties/enum-as-string/`created_at`+`updated_at`). The earlier incremental history was collapsed into this one clean baseline on 2026-05-28 (pre-release, no applied-migration history to preserve). Applied automatically on dev startup via `ApplyMigrations()`, which also creates the database if missing.
- **559/559 unit tests passing** (171 Domain + 388 Application). Build is warning-free with `TreatWarningsAsErrors`.

**Frontend** — `web/`
- Next.js 15 App Router + TypeScript strict + Tailwind v4 + Biome v2 + npm
- shadcn-style primitives hand-written for Tailwind v4 (Dialog, Select, Tabs, Checkbox, Textarea, etc.), `next-themes` (dark default + light/system toggle)
- App shell: sidebar nav (Transactions item with `ArrowLeftRight` icon), header with theme toggle
- **MSW removed from runtime.** Frontend now talks to the real backend at `http://localhost:5179` via `NEXT_PUBLIC_API_BASE_URL`. MSW is retained only for Vitest tests under `web/tests/mocks/` (slimmed handlers for the components that test fetch behaviour).
- TanStack Query v5 hooks for accounts, transactions, categories, imports, dashboard, budgets, goals; Zustand for sidebar collapse; React Hook Form + Zod for forms
- Dashboard page: net worth card, monthly summary, net-worth trend chart (Recharts), account cards, budget progress, recent transactions
- Accounts page: table with archive toggle, "Add account" dialog (with currency Select), MDL-equivalent column, "Update balance" action on row actions menu (visible only for Brokerage / CryptoExchange / P2PLending / BankDeposit accounts) opening a 3-mode dialog — **Investment** (money in), **Withdrawal** (money out), **Balance adjustment** (set new total = profit/loss). The transfer-based "New deposit/New withdrawal" buttons were removed in favour of these modes. Active rows expose "Archive"; archived rows (shown when the archive toggle is on) expose "Unarchive". **Account-name cell now links to `/accounts/[id]`** (row-action dropdown still works without navigating).
- **Account detail page (`/accounts/[id]`)** — per-account workbench backed by `GET /accounts/{id}` → `AccountDetailDto`. Renders a header strip (name + type/currency badges, opening date, live native balance + MDL-equivalent, action button group: New deposit / New withdrawal / Update balance / Archive — Update balance gated to investment types via the same `ADJUSTABLE_TYPES` set as the table), a Performance card (4-KPI grid Initial / +Contributions / −Withdrawals / ±Net P&L plus Current value, with YTD ↔ All-time toggle that swaps only the activity totals; native value appears alongside the MDL total for non-MDL accounts; missing-FX warning rendered via `<output>` for a11y), a Balance-over-time card (Recharts line + Daily/Weekly/Monthly interval Select scaling the window 30d/3mo/6mo respectively; sr-only enumeration of points; MDL-equivalent dashed line + sentinel for non-MDL accounts), and an Activity section with subtab presets (All / Contributions / Withdrawals / Adjustments / Other) on top of the existing `TransactionsTable`. New-deposit and New-withdrawal buttons open the existing transfer dialog with this account preselected as destination / source via new optional `defaultSourceAccountId` / `defaultDestinationAccountId` / `open` / `onOpenChange` props on `CreateTransferDialog` (non-breaking; the trigger button hides when `open` is supplied so the caller drives state). The `useAccountDetail` hook keys at `['accounts', 'detail', id]` under the existing `['accounts']` prefix so every transfer/adjust mutation invalidates it for free.
- Settings → FX rates page: list / add / delete rates with rate dialog backed by Zod (3-letter uppercase ISO, positive rate, distinct from/to). Now also shows a **Source** column (outline `Manual` / `BNM` badges) and a **"Refresh from BNM"** button that calls `POST /fx-rates/refresh`; the mutation invalidates `['fx-rates']` + `['accounts']` + `['dashboard']` + `['goals']` since rates ripple through every MDL-converted figure in the app. Sonner toast surfaces the insert/update/skip counts. A **"Backfill history"** button (next to Refresh) opens a dialog with a *From* date (defaulted to the earliest account opening date) and optional *To*, calling `POST /fx-rates/backfill` to pull official BNM rates for every business day in the range — used to fill historical rates for accounts opened before the daily auto-fetch window.
- **Settings landing + Data (backup & restore) page** — the `/settings` landing is now an index of link cards (FX rates, Data) instead of a placeholder, and the sidebar gains a Data entry. **`/settings/data`** has an Export card ("Download backup" → anchor-click download of `GET /data/export`, the same fetch-free technique as `ExportCsvButton`, no new deps) and an Import card (`.json` file picker → destructive **"Replace all data?"** confirm dialog spelling out that it permanently replaces ALL data → `POST /data/import` via `useImportData`). On success a Sonner toast summarizes the restored row counts (rendered defensively by iterating the response's numeric fields) and the picker resets; `ApiError.message` surfaces inline (`role="alert"`) + via toast for 400 unsupported-schema / malformed-file. The import mutation invalidates **every** query root (`accounts`, `transactions`, `categories`, `budgets`, `goals`, `fx-rates`, `dashboard`, `reports`) because a restore replaces all data. Hooks live in `web/src/lib/api/data.ts`.
- **Budgets page (`/budgets`)** — list table (Category / Limit / Spent / Remaining / progress bar / status pill / row actions), "Add budget" dialog (RHF + Zod, expense/both categories only, MDL limit), "Edit limit" dialog, "Archive" confirmation dialog. All mutations invalidate `['budgets']` *and* `['dashboard']`. Transaction/import/adjust-balance mutations now invalidate `['budgets']` too so the spend rollup stays in sync.
- **Goals page (`/goals`)** — list table (Name / Target / Saved / Progress bar / Status pill / Target date / mode badge — Linked vs Manual / row actions). Add and Edit dialogs (RHF + Zod) toggle between Linked-to-account (Select of non-archived accounts) and Manual mode via a fieldset/legend radio group (native inputs — no Radix RadioGroup primitive). "Update saved" row action only surfaces on Manual-mode goals; backend rejects PATCH for linked-mode and the dialog surfaces that 400 inline. All mutations invalidate `['goals']` + `['dashboard']`. Transaction / adjust-balance / import mutations also invalidate `['goals']` so linked-mode `Saved` recomputes when underlying balances change. The missing-FX warning uses a native `title=` attribute (no Tooltip primitive in the project).
- **Dashboard `/`** — net worth card sums `balanceMdl` (live, warns when any FX rate is missing). Account cards show native amount + muted MDL-equivalent secondary line. Recent transactions (col-span-7) render native currency + optional MDL-eq, with muted styling + badge for transfers and adjustments. **Monthly summary** card shows current-month income/expense/net + savings rate via `GET /dashboard/summary` (transfer- and adjustment-filtered server-side). **Net-worth trend** chart shows the last 6 months via `GET /dashboard/net-worth-trend` (Recharts `LineChart`, sr-only series fallback for a11y). **Budget progress** card shows the top 5 budgets by spend-percentage via `GET /budgets`. **Savings goals** widget (col-span-5, alongside Recent transactions) shows the top 3 goals by progress % via `GET /goals` — name + compact progress bar + status pill + "Saved / Target" line + "View all →" link. All widgets surface a "missing FX rate" / failure warning when the server flags incomplete data. Every empty-state placeholder on the dashboard is now wired.
- **Transactions page (`/transactions`)** — filterable table (account, date range, category, direction tabs, transfer tri-state, adjustment tri-state), native-currency amounts with optional muted MDL-eq secondary line, color-coded for income/expense, muted for transfers and adjustments with corresponding badges. When no account filter is applied ("All accounts"), each row also shows its owning account name as a muted sub-line under the description (omitted on a single-account list, e.g. the account-detail Activity table). "Add transaction" dialog (amount label dynamically reflects the picked account's currency) + "New transfer" dialog + "Import from PDF" link.
- **Add transaction dialog** — RHF + Zod, account/direction/amount/date/category/description, Zod schema mirrors backend rules
- **PDF import flow (`/transactions/import`)** — account picker → PDF upload (≤5MB) → preview table with per-row include checkbox, inline category select, duplicate highlighting, and **learn-with-confirm** (assigning a category the suggester missed proposes an editable keyword that is saved as a `category_patterns` rule on commit, so the next import auto-suggests it) → commit. Two-step state machine in one client page
- **Reports page (`/reports`)** — tabbed page with five sections backed by `useMonthlySummary`, `useCategoryBreakdown`, `useTopPayees`, `useBalanceOverTime`: Monthly Summary (grouped bar chart + table, trailing 12 months default), Categories (donut + table, Expense/Income toggle, current-month default), Top Payees (rank table, direction toggle, trailing 3 months default, limit 10), Balance Over Time (line chart per non-archived account, paired MDL line only when account currency ≠ MDL, Daily/Weekly/Monthly interval Select, trailing 6 months default), Year-over-Year (pure client transform of 24-month monthly-summary into current-12 vs prior-12 side-by-side bars — no separate endpoint). All chart components carry the sr-only `<ul>` data enumeration so component tests can assert on data without depending on Recharts layout. Date-range picker + direction toggle are shared widgets under `components/reports/`. Hooks live in `src/lib/api/reports.ts`, keys rooted at `['reports', '<sub>', params]`.
- **CSV export button on `/transactions`** — `ExportCsvButton` next to "Import from PDF" builds a URL from the active filter set and points at `/reports/transactions.csv`. Anchor-click triggers the browser download; no fetch indirection, no `file-saver` dep.
- **Goal detail page (`/goals/[id]`)** — per-goal workbench backed by `GET /goals/{id}` → `GoalDetailDto`. Header strip (name + target + target-date + mode badge ("Linked: <accountName>" or "Manual") + Archived badge, action group: Edit goal / Update saved (manual only) / Archive). Progress card (Saved figure + percent-of-target subtitle + 120%-capped bar + status pill + missing-FX warning). Pace card (3 cells: Avg monthly contribution, Projected completion + months-at-pace, Required monthly to hit target date — each with null-disambiguating subtitles: "Not enough history" / "Pace too slow" / "Goal already met" / "No target date"). History chart (Recharts line over `savedHistory` with dashed target reference line + conditional dot at `targetDate` when in range; sr-only point enumeration for jsdom tests). Contributions table (Date / signed Amount / Source badge — "Manual" vs "From <linkedAccountName>" / Notes). The goals-table name cell now links to `/goals/[id]` (row-action dropdown still works without navigating).
- **172 Vitest tests across 34 files + 1 Playwright spec for the import flow**, all passing (the +2 over the prior 170 cover the transactions-table account sub-line shown only when listing across all accounts; the Performance-card "Current value" native-amount test was tightened to reject the duplicated `… in <code>` suffix. The earlier +7 / +1 covers the Settings → Data page: `data-settings-page.test.tsx` — export download URL, card render, file-enables-restore, destructive-warning copy, confirm calls the mutation, success toast with counts + picker reset, API error surfaced). The earlier goal detail page work covers: GoalDetailHeader name/badges + linked-mode gating of Update-saved, GoalProgressCard sizing + status pill + missing-FX, GoalPaceCard three-cell null-subtitle matrix, GoalHistoryChart sr-only enumeration + empty state, GoalContributionsTable sort + sign coloring + source badge, GoalDetailView loading/404/generic/happy paths, and the goals-table name-cell-as-link change). Test setup polyfills `Element.{hasPointerCapture,scrollIntoView}`, `ResizeObserver`, and `window.matchMedia` (the last for sonner's `<Toaster>`); Recharts' `ResponsiveContainer` is mocked at fixed dimensions in chart tests since jsdom can't measure layout.
- `npm run gen:api` script ready (needs backend running on `localhost:5000`)

**Docs** — split into [WIKI.md](./WIKI.md) (product), [BACKEND.md](./BACKEND.md), [FRONTEND.md](./FRONTEND.md).

### Known rough edges

- shadcn CLI was skipped due to Tailwind v3/v4 config conflict — components are hand-written from canonical shadcn templates. Re-verify when shadcn ships Tailwind v4 support.
- `Intl.NumberFormat('ro-MD', { currency: 'MDL' })` renders as `L` in Node 22 / headless Chromium (ICU build). Real Chrome shows `MDL`. Tests assert digit grouping, not the currency code.
- ~~Frontend account cards show opening balance, not derived current balance~~ — **resolved**: `AccountDto.Balance` is the live derived balance (anchor + Σ income − Σ expense) and the cards render it.
- No mobile sidebar drawer — sidebar hides below `md` breakpoint.
- Migrations are generated but never run against a DB.
- **Transfer-leg and adjustment directions are mechanically right but semantically fragile** — a transfer's source leg is stored as `Direction = Expense` and destination leg as `Income`; an upward balance adjustment is `Income`, downward is `Expense`. Reports and aggregations must always filter BOTH `IsTransfer = false` AND `IsAdjustment = false` when computing "real" income/expense — there is no compiler check enforcing this. The dashboard handlers (`GetSummary`, `GetNetWorthTrend` — see `BACKEND.md` "Dashboard slice") apply the right filters and have unit tests pinning that behavior, so the current dashboard surface is covered; any *new* slice that touches income/expense aggregates still needs the same care.
- **Maib commission rows split principal and fee at parse time** — for any row where the `comision` column is populated, the parser emits TWO transactions: a primary at `ieșiri − comision` (the user's actual transfer) and a paired `Comision: …` fee row at `comision`, categorized `Bank Fees`. Sum equals the bank's per-row deduction, so the live balance reconciles against `Sold Disponibil` without any balance-calc filter. (The bank's `Sold final` summary number subtracts the fee a second time — that's a maib quirk, not the truth; we mirror `Sold Disponibil`.)
- **Cross-account currency invariant is enforced going forward only** — Phase 4 added the rule "every transaction's currency matches its account's currency" at all write boundaries. Pre-Phase-4 data in a long-lived DB might violate this; the `AdjustBalance` handler uses `Debug.Assert` rather than a hard failure if it encounters mismatched-currency historical rows.
- **`formatMDL` / `formatMDLCompact` helpers** in `web/src/lib/utils/currency.ts` have no remaining call sites in product code post-Phase-4. Safe to delete in a future cleanup.
- maib parser is regex-anchored over PdfPig's concatenated page text — best-effort. Section markers (`#Cont`, `#Cardul`) are stripped before row tokenization so boundary rows survive; tail is tokenized for decimal-formatted numbers only, so trailing junk (page footers, repeated column headers, card-number markers) is ignored. Unparseable rows are skipped rather than throwing. Only the maib statement format is recognised; other banks need their own `IBankStatementParser`.
- **Category multi-select filter** on the transactions page uses a single-value Select for v1 (no popover/checkbox primitive yet).
- **Budget rollover** is in the product brief but not yet implemented — unused budget does not carry over to the next month. Each month starts fresh from the configured `MonthlyLimit`.
- **Budget multi-currency** is deferred. `MonthlyLimit` is forced to `MDL` (the reporting currency) at the factory; non-MDL inputs fail `BudgetErrors.MdlOnly`.
- **Mid-month budget creation** — if a budget is created on the 15th, the corresponding `BudgetPeriod` will only reflect transactions created **after** that moment (the event handler runs on new writes, not historicals). No backfill yet.
- **Goal contribution history is manual-mode only** — `SavingsGoalContribution` rows are written by `PATCH /goals/{id}/manual-saved` capturing the signed delta. **Linked-mode** goals do NOT write to this table; their contributions are *derived* per-read from the linked account's transactions (so the table grows linearly with the user's manual-update cadence, not with their bank-import volume). The "projected completion at current pace" widget on `/goals/{id}` uses the last 90 days as its window; linked-mode pace is `(savedToday - savedAtWindowStart) / months`, manual-mode is `Σ contributions in window / months`. Pace returns null and the projected date returns null when there's less than 30 days of history or the pace is ≤ 0 (going backwards). Multi-currency is still deferred — contributions are MDL only.
- **Goal multi-currency** is deferred. `TargetAmount` is forced to `MDL` at the factory (`SavingsGoalErrors.MdlOnly`); the linked-account branch FX-converts at read time.
- **Goal auto-archive** — a goal that hits `Achieved` is *not* auto-archived; the user must do it manually from the row action.
- **BNM-fetched rates "come back" after delete** — `DELETE /fx-rates/{id}` works on both `Manual` and `BnmAuto` rows, but a deleted `BnmAuto` row will be re-inserted on the next scheduled refresh (or the next `POST /fx-rates/refresh`) as long as an account still holds that currency. The frontend surfaces a `title` tooltip on BNM-row delete actions explaining this. To suppress permanently, either delete the account that holds the currency or create a `Manual` rate (manual wins priority + dedup so the auto fetcher will skip).
- **Reports balance-over-time INCLUDES transfers/adjustments** — this is the slice's one intentional break from the otherwise-uniform `!IsTransfer && !IsAdjustment` rule. Per-account native balance is anchor + Σ signed transactions of EVERY non-deleted row (including transfers and balance adjustments), because those rows really do move the account's balance even though they're excluded from income/expense P&L. Pinned by `GetBalanceOverTimeQueryHandlerTests`. The other four reports (monthly-summary, category-breakdown, top-payees, and any future P&L aggregate) still filter transfers + adjustments — see `GetMonthlySummaryQueryHandler` for the canonical filter.
- **Data restore is destructive and unversioned-tolerant** — `POST /data/import` does a full wipe-then-load inside one transaction (so a failed import rolls back cleanly), but a *successful* import permanently replaces ALL existing data with no undo. It only accepts the current `SchemaVersion` (now **4** — bumped 2026-05-29 when `category_patterns` was added to the backup; see below); any other version is rejected with `UnsupportedSchemaVersion` before the store is touched (there's no cross-version migration of backup files yet). Upload cap is 50MB. The `EfBackupStore` wipe/reinsert is not covered by an automated integration test (no Infrastructure test project in v1) — only the command/query handler validation is unit-tested; the destructive round-trip (including the `category_patterns` fix) was verified manually via the UI on 2026-05-29 (see QA.md row 43). **Backup completeness:** the backup now includes `category_patterns` (learned/seeded keyword rules) — earlier versions silently lost them on restore via the `categories` cascade. `fx_rates` remains the one intentional exclusion (re-fetchable from BNM, never wiped). After a restore, BNM-sourced FX rates behave like the delete case: the next scheduled/on-demand refresh will re-insert any `BnmAuto` rows for currencies still held by an account, even if the restored backup didn't contain them.
- **Migration tooling** — migrations are generated **only** via the EF Core CLI (`dotnet ef migrations add`), with the API process stopped first so the tooling can load `MoneyManagement.Infrastructure.dll` (a running API/VS holds an exclusive lock). The generated migration body needs a one-line manual conversion from block-scoped to file-scoped namespace (the repo enforces `IDE0161`); the auto-generated `.Designer.cs` is exempt. `dotnet ef migrations has-pending-model-changes` must report clean after every migration. The schema is currently a single baseline (`20260528201519_InitialCreate`); the database is created + migrated on dev startup via `ApplyMigrations()`.

### Next steps (in rough order)

1. **Run both sides locally**: `dotnet run --project src/MoneyManagement.Api` (auto-creates DB + applies migrations + seeds 9 categories on first run) and `npm --prefix web run dev`. Upload the real maib PDF and exercise parse → preview → commit end-to-end.
2. ~~**Add `current_balance` derivation**~~ **Done** — `AccountDto` already exposes a live `Balance` (anchor + Σ income − Σ expense across all non-deleted rows) plus `BalanceMdl`; the dashboard account cards render the derived current balance, not the opening anchor. (This step predates the live-balance rework and was left stale in the list.)
3. ~~**Backend slice: `Budget`**~~ **Done (2026-05-22)** — Budget + BudgetPeriod entities, first domain-event handler (`TransactionCreated` → period rollup), `/budgets` page, dashboard widget. See Backend > Budget slice + the Known rough edges around delete/update drift, rollover, multi-currency, and mid-month creation.
4. ~~**Backend slice: `SavingsGoal`**~~ **Done (2026-05-22)** — hybrid linked/manual mode, 5 endpoints, dashboard widget. See Backend > SavingsGoal slice + rough edges around contribution history, multi-currency, and auto-archive.
5. ~~**Dashboard summary endpoints** — `GET /dashboard/summary` and `GET /dashboard/net-worth-trend`.~~ **Done (2026-05-22)** — see Backend > Dashboard slice. Frontend wiring done in parallel.
6. ~~**BNM auto-fetched FX rates**~~ **Done (2026-05-23)** — promoted from the v2 backlog. `FxRateSource` enum, `BnmRateProvider` + `BnmAutoFetchService`, `POST /fx-rates/refresh`, frontend Source column + Refresh button. See Backend > BNM auto-fetch (FxRate Source enum).
7. ~~**Reports page**~~ **Done (2026-05-23)** — five-section tabbed `/reports` page + CSV export button on `/transactions`. See Backend > Reports slice + Frontend > Reports page. Year-over-Year is a pure client transform of the trailing 24 months of monthly-summary — no separate endpoint.
8. ~~**Settings: data export / import**~~ **Done (2026-05-23)** — JSON backup + destructive full-replace restore. `GET /data/export` (streamed file download) + `POST /data/import` (multipart, schema-versioned, transactional wipe-then-load) + `/settings/data` page with a destructive confirm dialog. `pg_dump`-backed full-DB export was scoped out of v1 (it depends on `pg_dump` on PATH and is provider-specific; the app-level JSON backup is portable and is the foundation for restore). See Backend > DataPortability slice + Frontend > Settings → Data page, and the Known rough edges around the destructive restore.
9. ~~**Consolidate migrations to a single CLI baseline**~~ **Done (2026-05-28)** — the incremental migration history was collapsed into one clean EF CLI-generated baseline (`20260528201519_InitialCreate`); the app now points at a fresh database (`money_management_v2`) created on startup. Done alongside an audit-field refactor: `CreatedAt`/`UpdatedAt` were hoisted onto the base `Entity` and the redundant `CreatedAtUtc` (Budget/SavingsGoal) was removed — `CreatedAt` is now the single UTC creation timestamp, set by the polymorphic `AuditableEntitySaveChangesInterceptor` (which previously skipped Budget/BudgetPeriod/SavingsGoal/SavingsGoalContribution/CategoryPattern — latent bug fixed). 559/559 tests pass, `has-pending-model-changes` clean.
10. ~~**Budget drift mitigations**~~ **Done (2026-05-27)** — `TransactionDeletedDomainEvent` + `TransactionCategoryChangedDomainEvent` raised from `Transaction.MarkDeleted` / `Transaction.SetCategory`, consumed by two new `IDomainEventHandler<T>`s that apply the inverse update to the matching `BudgetPeriod` (with clamp-at-zero for FX drift). `DeleteTransaction` and `UpdateTransactionCategory` command handlers now inject `IFxConverter` and pass the per-row-date MDL amount through. Plus a `RebuildBudgetPeriods` escape hatch (`POST /budgets/{id}/rebuild-periods` and `POST /budgets/rebuild-all-periods`) that replays every non-deleted expense in the budget's category and rewrites the `BudgetPeriod` rows from scratch — the canonical correction path for any pre-fix drift. See Backend > Budget slice.
11. ~~**Goal contribution history**~~ **Done (2026-05-23)** — `SavingsGoalContribution` entity + `GET /goals/{id}` detail endpoint + `/goals/[id]` detail page with pace stats (projected completion + avg monthly + required to hit date), saved-over-time chart, contributions table. See Backend > SavingsGoal slice for the manual/linked-mode contribution sourcing rules.

> **`RecurringTemplate` dropped from v1.** The original product brief called for templated recurring transactions (rent, salary, subscriptions) generated as pending rows on their due date. In practice the maib bank statement is imported at end-of-month, which already contains *every* card-side recurring debit. Pre-creating templated rows would duplicate the bank-side rows and add reconciliation friction. The slice is deferred to v2 — only worth revisiting if cash-only recurring obligations or forward-looking balance forecasting become needed.

---

## Core Concepts

### Accounts

Seven account types, each tracked separately. *(Note: "BankDeposit" here means the **account type** — the place money lives. A "Savings" **category** is something different — see Categories below.)*

| Type | Description | Example |
|------|-------------|---------|
| **Cash** | Physical wallet / cash on hand | MDL cash at home |
| **CreditCard** | Card balance; spending adds to debt, payments reduce it | Visa with revolving credit |
| **BankCurrent** | Transactional bank/card account; everyday spending and salary inflows | salary card, daily-use debit card |
| **BankDeposit** | Interest-bearing savings/term deposit at a bank | emergency-fund deposit, foreign-currency term deposit |
| **Brokerage** | Investment brokerage holding cash + securities; tracked as a periodically updated cash-value snapshot | XTB |
| **CryptoExchange** | Centralized crypto exchange wallet; tracked as a periodically updated cash-value snapshot | Binance, Bybit |
| **P2PLending** | Peer-to-peer lending platform | Fagura |

Each account has:
- Name (e.g. "Salary card", "Brokerage", "Crypto wallet")
- Type (one of the seven above)
- **Currency** — 3-letter ISO code (MDL, USD, EUR, RON, …). The account's native currency. MDL is the reporting currency for aggregate views (see FX Rates).
- Opening balance and opening date
- Current calculated balance (derived from transactions; Phase 4 will add monthly balance-adjustment transactions for investment-type accounts)
- Optional notes / description
- `is_archived` flag — archived accounts hide from the dashboard but keep their history

#### FX Rates and MDL-equivalent view (Phase 2)

The user's accounts span multiple currencies but they think in MDL. The dashboard net-worth card, the per-account list, and aggregate reports therefore need an MDL-equivalent value. This is computed at the **read side only** via an `IFxConverter` service backed by an `FxRate` table. Phase 2 ships with manual rate entry; a later iteration may pull rates from BNM (Banca Națională a Moldovei, bnm.md) which publishes official MDL pairs daily.

### Transactions

Every money movement is a transaction. Fields:

| Field | Notes |
|-------|-------|
| Date | When it happened |
| Amount | Positive number (sign comes from `type`) |
| Type | Income / Expense / Transfer |
| Account | Which account it affects |
| To Account | Only for transfers (the destination) |
| Category | User-defined (see below); not used for transfers |
| Payee / Description | Free text |
| Notes | Optional extra detail |
| Tags | Optional comma-separated labels *(deferred to v2 — not implemented)* |
| Attachment | Optional receipt image (deferred to v2) |

**Transfer** moves money between two accounts (debit source, credit destination). Transfers are **excluded from income/expense reports** — they're internal movement, not P&L. Source and destination can be **different currencies** (e.g. 1,000 MDL leaves a bank account, 55 USDT lands on a crypto exchange): you enter the amount received on the destination side (pre-filled from the FX rate, editable), and the effective rate is derived from the two amounts. Works in both the manual "New transfer" dialog and the PDF-import counter-account picker.

**Savings category vs. transfer-to-savings-account.** Both exist intentionally:
- A *transfer* to your "ING Savings" account moves money between your own pockets — no expense.
- A "Savings" *category* on an expense (e.g. buying a bond, paying a third-party fund) records money leaving your control — counts as expense.

Don't double-count: if you transfer to your own savings account, don't also tag it with a "Savings" category.

### Categories

User-defined, hierarchical (parent → subcategory). Examples:

- Food & Drink → Groceries, Restaurants, Coffee
- Transport → Fuel, Public Transport, Parking
- Housing → Rent, Utilities, Internet
- Health → Pharmacy, Doctor
- Entertainment → Streaming, Games
- Income → Salary, Freelance, Dividends
- Savings → Emergency Fund, Vacation Fund *(external, not your own savings account)*

Each category is tagged **Income** or **Expense** type — drives report grouping.

---

## Features

### 1. Dashboard

Landing page showing a snapshot of the current financial state:

- **Net worth card** — sum of all non-archived account balances
- **Account balance cards** (one per account)
- **Current month** — income vs expenses vs savings rate
- **Budget status** — progress bars for top categories with budgets
- **Savings goals** — progress bars
- **Recent transactions** (last 10)
- **Quick-add transaction** button
- **Net worth trend** — small line chart, last 6 months

### 2. Transactions

- Full transaction list with filters: date range, account, category, direction, transfer/adjustment tri-state *(tags + free-text description/payee search deferred to v2 — not implemented)*
- Add + delete (soft delete — see BACKEND.md) + inline recategorize + inline note edit. *(A full-row edit dialog — amount / date / description — is deferred to v2; imported rows are treated as source-of-truth, so only category and notes are user-mutable today.)*
- **Notes** — optional free-text annotation on any transaction, separate from the description (the bank memo / label). Set it when adding a transaction, or edit it inline on any existing row (including imported ones) via a per-row note editor.
- Bulk actions: delete, re-categorize, tag *(deferred to v2 — not implemented)*
- **Pagination** — offset-based, **25 per page** *(cursor-based 50/page was the original plan; the shipped implementation is offset with `pageSize=25`)*
- **PDF import** — upload a maib monthly statement; parser splits each row into principal + commission, auto-suggests transfer flag + category, preview before commit. maib groups rows by card/section in the PDF, so the preview re-sorts them by date (oldest first) before you review and commit. This is the primary mechanism for getting recurring obligations (rent, salary, subscriptions) into the DB — see roadmap note about dropped `RecurringTemplate`.
- **CSV import** *(deferred)* — was planned for non-maib banks; the maib PDF flow currently covers the user's setup.
  - Duplicate detection: signature `(date, amount, description)` matched against existing DB rows in the same period. Snapshot semantics — two identical rows in the same import batch (e.g. two ATM withdrawals of the same amount on the same day) are both kept; only rows that match something already in the DB get default-excluded.

### 3. Accounts

- List all accounts with current balance
- Add / edit / archive / unarchive accounts; permanently delete only when an account has no transactions, imports, or linked goals (otherwise archive)
- Account detail view: full transaction history, balance-over-time chart
- *(Reconciliation checkpoints — deferred to v2)*

### 4. Budgets

- Define monthly spending limits per category (e.g. Food = 1500 MDL/month)
- Dashboard and dedicated Budget page show:
  - Spent so far vs limit (progress bar, color-coded: green < 80%, yellow 80–100%, red > 100%)
  - Remaining amount
  - Rollover option: carry unused budget into next month (configurable per budget) *(deferred to v2 — not implemented; each month starts fresh from the configured `MonthlyLimit`)*
- Budget history — how each category performed in past months

### 5. Savings Goals

- Create a goal with:
  - Name (e.g. "Emergency Fund", "Vacation Greece")
  - Target amount
  - Target date (optional)
  - Linked account (optional — tracks the balance of that account toward the goal)
  - Current saved amount (manual or auto from linked account)
- Dashboard and Goals page show:
  - Progress bar (saved / target)
  - Projected completion date based on average monthly contribution
  - Required monthly contribution to hit target date

### 6. Reports

All reports are filterable by date range, account(s), and category/tag.

| Report | Description |
|--------|-------------|
| **Monthly Summary** | Table + bar chart: income, expenses, net savings per month |
| **Category Breakdown** | Pie chart + table of expense (or income) split by category for a period |
| **Balance Over Time** | Line chart of one or more account balances over a period |
| **Net Worth Over Time** | Line chart summing **all** account balances over a period (distinct from per-account view). *Currently lives on the **dashboard** (net-worth trend, last 6 months) — not yet surfaced as a standalone Reports tab.* |
| **Top Payees** | Ranked list of where your money goes (or comes from) |
| **Year-over-Year** | Compare a month or category against the same period last year |
| **Export to CSV** | Download any report or filtered transaction list as CSV |

### 7. Settings

- Manage categories (add, rename, recolour, archive) *(reorder deferred)*
- Manage accounts
- **Theme** — light / dark / system (default: dark)
- Reporting currency (MDL fixed; per-account currency is set on the account itself)
- Data export: full JSON backup (`GET /data/export`, streamed file download; filename carries the UTC date **and time** so same-day exports don't overwrite each other). FX rates are **not** included — they're re-fetchable from BNM, so they're treated as a local cache outside backup scope. *(A `pg_dump`-based raw dump was scoped out of v1 — the app-level JSON backup is portable and provider-agnostic.)*
- Data import: restore from a JSON backup (`POST /data/import`) — destructive full replace of everything **except** FX rates (those are preserved), behind a confirm dialog

---

## Out of Scope (v1)

| Item | Notes |
|------|-------|
| Multi-user / authentication | Single-user, self-hosted; no auth in v1 |
| Real-time bank sync | No Plaid / open banking |
| Live investment prices | No Yahoo Finance / Alpha Vantage |
| Mobile native app | Web is responsive, no native wrapper |
| Investment positions (asset + quantity + price) | Investment/crypto accounts are tracked as monthly cash-value snapshots, not positions (Phase 4) |
| Auto-fetched market prices (Yahoo, CoinGecko) | Manual balance adjustments only |
| Receipt attachments | Schema supports it but UI deferred |
| Reconciliation checkpoints | Modeled in v2 |
| Recurring transaction templates | Dropped — bank statement is the source of truth, pre-created templates would create reconciliation friction. Reconsider if cash-only recurring obligations become a need. |
| Notifications panel (budget exceeded, goal milestones) | v2 — would need persisted entity + polling/WebSocket |
| PWA / offline mode | v2+ if ever |

---

## v2 Candidate Backlog

(Not committed — captured so we don't lose them.)

- Reconciliation checkpoints (`ReconciliationCheckpoint` entity)
- Receipt image attachments (file storage in API + thumbnail in UI)
- Notifications: budget exceeded, goal milestones
- Multi-user with basic auth
- PWA / offline-first with sync
- Investment portfolio with live prices
- Mobile native wrapper (Capacitor / .NET MAUI)
- `RecurringTemplate` (only if cash-only recurring obligations or forecast-style "what's my balance on date X" become a real need — see rationale in Out of Scope above)
- CSV import for non-maib banks (or a second `IBankStatementParser` for whichever bank statement format the user starts receiving)
