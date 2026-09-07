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

### Pooled capital — outside investors (built 2026-09-06/07, uncommitted)

Two friends each invested USD 1,000 into the Binance account in September 2026, with monthly
profit-share payouts. That breaks a core assumption the model had held until then: **every dollar in
an account belongs to the user.**

The same class of bug affected loans, and that half shipped first (2026-09-06 — `IExternalClaimSource`,
three dashboard tiles). Pooled capital rides a **second, separate seam**, and the reason is
structural: a loan claim is a fixed principal that only ever *declines*, while a pool stake *rises*
when the account rises, so nothing in a claim record can express it. See BACKEND.md >
"Account ownership and net worth".

The arrangement is modelled as an actual fund — everyone including the user holds units, priced at
NAV — and is summarised under Core Concepts below. Design rationale, the settled decisions and a
worked numeric example live in **[POOLED-CAPITAL.md](./POOLED-CAPITAL.md)**, which is deleted once
this work is committed and its content has fully migrated here.

---

## Build Status

Last updated: **2026-09-07**

### Done

**Backend** — `src/` + `tests/`
- Clean Architecture skeleton: 5 src projects + 2 test projects, dependency direction enforced
- SharedKernel: `Entity`, `Result<T>`, `Error` + `ErrorType`, `IDomainEvent`, `IDomainEventHandler<T>`, `IDateTimeProvider`
- **Account slice** — `Account` entity with factory + validation, `Money` value object, `AccountType` enum (Cash, CreditCard, BankCurrent, BankDeposit, Brokerage, CryptoExchange, P2PLending), per-account ISO currency, `CreateAccount`/`GetAccounts`, endpoints `POST /accounts` + `GET /accounts`. `AccountDto` carries a single `balance` (live, computed on read as anchor + Σ income − Σ expense across all non-deleted rows) and `balanceMdl` (MDL-equivalent, nullable when no FX rate is available). The maib parser splits any row that has a `comision` column into two paired entries (principal = `ieșiri − comision`, fee = `comision`, category `Bank Fees`) so their sum equals the bank's actual per-row deduction; the live balance then naturally reconciles against maib's `Sold Disponibil` without any special filtering.
- **FxRate slice** — `FxRate` entity + `IFxConverter` (direct + inverse lookup; identity short-circuit; null when no usable rate ≤ asOf), `EfFxConverter`, endpoints `GET /fx-rates` + `POST /fx-rates` + `DELETE /fx-rates/{id}`. No rates are seeded — the table starts empty and is populated by manual entry (`POST /fx-rates`) or the BNM auto-fetch / backfill. Reporting currency `MDL` is centralized in `ReportingCurrencies.Mdl`; ISO validation lives in `CurrencyCodes.IsValidIso` shared by `Account` and `FxRate`.
- **BNM auto-fetch (FxRate `Source` enum)** — `FxRate` now carries a `Source = Manual | BnmAuto`. `IBnmRateProvider` (implemented by `BnmRateProvider` over `HttpClient` against `https://www.bnm.md/en/official_exchange_rates?get_xml=1&date=DD.MM.YYYY`) pulls daily MDL rates from the Banca Națională a Moldovei XML feed; `BnmAutoFetchService` (`BackgroundService`) does a 30-day backfill on startup + a daily refresh (both configurable under `Fx:AutoFetch`). Only currencies actually held by user accounts get pulled. `RefreshBnmRatesCommandHandler` orchestrates fetch → dedup → upsert; **manual rates always win** (`EfFxConverter` ties tied-date rows with `ThenBy(Source)` so `Manual = 0` outranks `BnmAuto = 1`). New endpoint `POST /fx-rates/refresh` triggers an on-demand pull and returns insert/update/skip counts. The `source` column has `defaultValue: "Manual"` (enforced via `HasDefaultValue(FxRateSource.Manual)` in `FxRateConfiguration` so the enum-read path is safe) and the unique index is `(from_currency, to_currency, as_of, source)` — two physical rows for the same (from, to, asOf) triple are allowed when sources differ.
- **Category slice** — hierarchical (self-ref `parentId`), `CategoryFlow` enum (Expense/Income/Both), `CreateCategory` / `GetCategories` / `UpdateCategory` / `ArchiveCategory`, endpoints `POST /categories` + `GET /categories` + `PUT /categories/{id}` (rename/edit name·flow·colour) + `DELETE /categories/{id}` (soft archive). Auto-categorization keyword rules live in the `category_patterns` table (DB-backed, seeded from the original suggester rule set), managed via `GET/POST/PUT/DELETE /category-patterns` and the **Settings → Categories** screen (expandable list: create/archive categories + add/remove each category's keyword rules as chips). Active category names are **unique per flow** (case-insensitive, 409 on duplicate; archiving frees the name) — the same name across different flows is deliberate, e.g. an Income and an Expense category for the same counterparty
- **Transaction slice** — `Transaction` aggregate with `Money` (currency matches the parent account's currency — multi-currency since Phase 4), `TransactionDirection` (Income/Expense), `TransactionSource` (Manual/Imported), nullable `CategoryId`, optional `OriginalAmount` + `OriginalCurrency` for FX rows, `ImportBatchId` traceability, `IsTransfer` + `CounterAccountId` (Phase 3) so internal transfers stay out of income/expense aggregates, `IsAdjustment` (Phase 4) for monthly balance true-ups on investment accounts. `IsTransfer` and `IsAdjustment` are mutually exclusive. Soft delete via `HasQueryFilter`. Endpoints `POST /transactions` + `GET /transactions?accountId=&from=&to=&categoryId=&direction=&isTransfer=&isAdjustment=` + `DELETE /transactions/{id}` + `PUT /transactions/{id}/category` (recategorize a single row; rejects a category whose flow conflicts with the row direction) + `POST /transfers` (creates the two paired legs in one transaction; an optional note is stored on both legs) + `POST /accounts/{id}/balance-changes` (3-mode: Investment / Withdrawal / Balance adjustment). `TransactionDto` carries `AmountMdl` (FX-converted at the transaction date; null if no rate). Only the **transaction date** is stored — the bank's *processing date* is dropped at the parser boundary since card payments debit instantly.
- **Statement Import slice** — `ImportBatch` entity (filename + sha256 hash + bank source + counts), `IBankStatementParser` strategy with `MaibStatementParser` (PdfPig 0.1.10), `ICategorySuggester` (keyword rules read from the DB-backed `category_patterns` table), `ITransferDetector` (Phase 3, whole-token inclusion `A2A` / `Retragere` / `ATM` — the generic `Transfer` token was dropped because maib labels salary & payments "Transfer …" too; prefix-matched exclusions `Achitare` / `Plată` / `Salariu` / `MIA` / `Cashback` so inflected forms like `Salariul` are caught), duplicate detection via signature `sha256(accountId|date|amount|normalizedDescription)` taken as a snapshot of existing DB rows (not mutated during the import loop, so legitimately-repeated rows in one batch — e.g. two same-day same-amount ATM withdrawals — are both kept). Parser emits a paired `Comision: …` Expense row whenever a maib line has both `ieșiri` and `comision` columns populated, so bank fees are never silently dropped. Endpoints `POST /imports/parse` (multipart preview, carries per-row `isTransfer` auto-suggestion) + `POST /imports/commit` (transactional bulk insert, per-row `isTransfer`/optional `counterAccountId` for cases like ATM → Cash or Salary → Brokerage where the destination has no PDF, plus an optional per-row `notes` annotation persisted on the source row). Parser strips card/account section markers (`999999******0000 #Cardul`, `… #Cont`) before row extraction so boundary rows (last row of a section, last row of a page) aren't dropped — earlier versions silently lost ~3 transactions per statement.
- **Category seeding** — `CategorySeeder` hosted service inserts 10 default categories with deterministic GUIDs (9 original + `Balance Adjustment` (`...00000000a`, flow `Both`) for Phase 4 balance-adjustment rows, plus `Investment` (`...00e`) and `Withdrawal` (`...00f`) for the 3-mode balance-change action). The generic `Other` is split into `Other expenses` (`Expense`) + `Other income` (`...010`, `Income`); `Home` (`...011`, `Expense`) is the home-expenses bucket. The seeder also backfills any missing default by id on existing DBs so adding new defaults (like `Home`) doesn't require dropping the categories table — they appear on the next startup.
- **Dashboard slice** — read-only projections that feed the existing dashboard widgets. `GET /dashboard/summary?month=YYYY-MM` returns the calendar-month income/expense aggregate (FX-converted to MDL per-row at the transaction's date), savings rate, transaction count, and a `missingFxRate` flag. `GET /dashboard/net-worth-trend?months=N` (default 6, max 24) returns oldest-first points: previous month-ends + a live "today" point, summing each non-archived account's native balance converted to MDL at that as-of date. **Only `/dashboard/summary` filters `IsTransfer = false` AND `IsAdjustment = false`** (plus `!IsDeleted`). The net-worth endpoints deliberately do **not**: transfers and adjustments genuinely move an account's balance even though they are excluded from income/expense aggregates, so filtering them would make net worth wrong. `NetWorthTrendFilterDisciplineTests.NetWorth_IncludesTransfersAndAdjustments_InTheBalance` exists specifically to stop someone "fixing" the code to match an earlier, incorrect version of this sentence. `DashboardErrors.MonthsOutOfRange` lives in Application (Dashboard has no entity in Domain).
- **Budget slice** — `Budget` entity (one-active-per-category, MDL-only `MonthlyLimit`) + sibling `BudgetPeriod` (per-year-month spend rollup). Endpoints `POST /budgets`, `GET /budgets?year=&month=` (default = current UTC month), `PUT /budgets/{id}`, `DELETE /budgets/{id}` (soft archive). `BudgetDto` carries `Spent`, `Remaining`, and a precomputed `Status` enum (`OnTrack` <80%, `Warning` 80–100%, `Over` >100%) so the UI just colors what the server tells it. Active-budget uniqueness is enforced both by a handler pre-check (`BudgetErrors.AlreadyExistsForCategory` → HTTP 409) and a partial unique index `(category_id) WHERE is_archived = false`.
- **Domain-event handler pattern (first use)** — the Budget slice introduces `TransactionCreatedDomainEvent` (raised from `Transaction.Create`, which now takes `amountMdl` as a parameter — every call site FX-converts via `IFxConverter` before constructing the transaction). The first `IDomainEventHandler<T>` in the project lives at `Application/Features/Budgets/EventHandlers/UpdateBudgetPeriodOnTransactionCreatedHandler.cs`. It find-or-creates the matching `BudgetPeriod` and accumulates `Spent` reactively, skipping income, transfers, adjustments, uncategorized rows, and rows with no FX-convertible MDL value. The dispatcher fires after `SaveChanges` (same `DbContext` scope), then saves again for the budget-period mutation.
- **SavingsGoal slice** — `SavingsGoal` entity supports two modes in one row: **linked** (`LinkedAccountId` set → `Saved` is computed live as that account's MDL-converted balance via `IFxConverter`) or **manual** (`ManualSavedAmount` stored as a paired value+currency pair, since EF Core 10's nullable `ComplexProperty` couldn't model `Money?`). Endpoints `POST /goals`, `GET /goals`, `GET /goals/{id}` (detail), `PUT /goals/{id}`, `PATCH /goals/{id}/manual-saved` (rejects in linked mode; also writes a `SavingsGoalContribution` row capturing the signed delta), `DELETE /goals/{id}`. `GoalDto` carries `Saved`, `Remaining`, `ProgressPercent`, a precomputed `Status` (`OnTrack`/`AtRisk`/`Achieved`/`Behind` — pace = saved vs `target × monthsElapsed / monthsTotal`, AtRisk if below 90% of pace), `RequiredMonthlyContribution` (null when no target date or already achieved), `IsLinkedMode`, and `MissingFxRate`. FK to `accounts` uses `Restrict` on delete so a linked account can't be removed without unlinking first.
- **SavingsGoalContribution + goal detail** — second entity `SavingsGoalContribution` (`GoalId`, signed `Money` amount in MDL, `OccurredOn`, `Notes`) is a time-series table for **manual-mode** goals only. `UpdateManualSavedCommandHandler` now writes a row whenever the new total differs from the previous (positive = contribution, negative = withdrawal; zero-delta is skipped). `GET /goals/{id}` → `GoalDetailDto` adds: `CreatedOn`, `IsArchived`, a `Contributions[]` list (manual → DB rows; **linked** → derived per-row from the linked account's transactions, FX-converted at the row's date, `Source = LinkedAccountTransaction`), a `SavedHistory[]` monthly cumulative series (manual: running sum; linked: per-month-end balance of the linked account), and a `Pace` block (`avgMonthlyContribution` over the last 90 days, `projectedCompletionDate` and `monthsToAchieveAtPace` — null when pace ≤ 0 or already achieved; clamped at 50 years). Math is shared between `GetGoalsQueryHandler` and `GetGoalDetailQueryHandler` via a `GoalProjection` static helper so the two handlers can't drift. The `savings_goal_contributions` table has FK `ON DELETE CASCADE` and `ix_savings_goal_contributions_goal_id_occurred_on (goal_id, occurred_on DESC)`.
- **Reports slice** — five read-only endpoints projecting over Transactions + Accounts; same filter discipline as Dashboard (`!IsDeleted && !IsTransfer && !IsAdjustment` for income/expense aggregates). `GET /reports/monthly-summary?from=YYYY-MM&to=YYYY-MM` → array of points with the same shape as `DashboardSummaryDto` (income/expense/net/savingsRate/count/missingFxRate); defaults to the trailing 12 months when both params omitted, capped at a 24-month span. `GET /reports/category-breakdown?from=YYYY-MM-DD&to=YYYY-MM-DD&direction=Expense|Income` → `{ from, to, direction, totalMdl, missingFxRate, items[] }` sorted by `amountMdl` desc with an `Uncategorized` bucket (`categoryId = null`) for null categories; percentages sum to 1.0. `GET /reports/balance-over-time?accountId=&from=&to=&interval=Daily|Weekly|Monthly` (default `Monthly`) → per-interval native balance + nullable `balanceMdl` per as-of date. **Does NOT filter transfers/adjustments** — they DO move the per-account native balance even though they're excluded from income/expense aggregates; this is the slice's one intentional break from the otherwise-uniform filter rule, pinned by a unit test. `GET /reports/top-payees?from=&to=&direction=&limit=10` → array sorted by `amountMdl` desc, grouped by normalized description (`trim().ToLowerInvariant()`), `originalDescription` carries the first occurrence's raw casing for display. `GET /reports/transactions.csv?<same filters as /transactions>` → streaming RFC 4180 CSV with header `transaction_date,account,category,direction,amount,currency,amount_mdl,description,is_transfer,is_adjustment`; UTF-8 without BOM; account/category names resolved via single batched join; written directly to `Response.Body` via `StreamWriter` (no intermediate buffer). `ReportsErrors` colocated with the slice mirrors `DashboardErrors`.
- **DataPortability slice** — full-database JSON backup + destructive restore (Settings → Data). Entity-less, no schema change, no migration. `IBackupStore` (Application abstraction, same split as `IFxConverter`) implemented by `EfBackupStore` in Infrastructure — the one place with `Database`/transaction/`ExecuteDelete`/`IgnoreQueryFilters` access. `BackupDocument` (`SchemaVersion = 6` + `ExportedAtUtc` + flat `*Backup` arrays for 14 entities — accounts, categories, **category_patterns**, transactions, importBatches, budgets, budgetPeriods, savingsGoals, savingsGoalContributions, **loans**, **loanPayments**, **pools**, **poolParticipants**, **poolUnitEvents**) is a **faithful snapshot** of those tables: archived + soft-deleted rows included, exact IDs/audit/flags preserved. `fx_rates` is the one intentional exclusion (re-fetchable from BNM). *(category_patterns was added in v4 on 2026-05-29 — before that, a restore silently lost all learned keyword rules via the `categories` cascade.)* `GET /data/export` streams the JSON as a file download (same shape as `/reports/transactions.csv`, enums as names via the shared `JsonOptions`). `POST /data/import` (multipart `file`, 50MB cap, antiforgery disabled) deserializes → `ImportDataCommand` validates `SchemaVersion` (`DataErrors.UnsupportedSchemaVersion`) + non-null arrays (`MalformedBackup`) → `IBackupStore.RestoreAsync`. Restore is **destructive + transactional**: one transaction, wipe child-first (`ExecuteDeleteAsync` w/ `IgnoreQueryFilters`), reinsert parent-first via raw parameterized `NpgsqlParameter` INSERTs (table from `context.Model`, enums via `.ToString()`, `timestamptz` UTC-coerced) — bypasses domain factories so IDs/audit survive; any failure rolls back. The categories wipe nulls all `parent_id`s first because that self-ref FK is `ON DELETE RESTRICT` (non-deferrable in Postgres). Handler validation is unit-tested with a substituted `IBackupStore`, and the EF store now has real integration coverage in `tests/MoneyManagement.Infrastructure.Tests/Backup/EfBackupStoreTests.cs` *(the older "no Infrastructure test project" note is obsolete — that project exists and holds 113 tests)*. See Backend > DataPortability slice.
- **Loans slice** (2026-08-26) — personal interest-free lending, both directions (`Loan` + `LoanPayment` entities; direction `Given | Received`, free-text counterparty, any-ISO principal fixed at creation). Read side computes `TotalRepaid` / `Outstanding` / `Active|Settled` status / `OutstandingMdl` (+`MissingFxRate`) — nothing stored. Optional per-movement `AccountId` on create-loan and record-payment synthesizes an `IsTransfer = true` transaction (no counter account, seeded category **Loan** `…012` flow `Both`) so balances stay truthful while every income/expense aggregate ignores loan money — the Investment/Withdrawal precedent, zero new filter sites. Overpayment rejected (`PaymentExceedsOutstanding`); v1 is same-currency (payment + linked account must match the loan's currency). Deletion is coupled both ways: deleting a payment soft-deletes its linked transaction, and deleting a loan-linked transaction from the transactions page reactively removes the payment via `RemoveLoanPaymentOnTransactionDeletedHandler` (consumes the existing `TransactionDeletedDomainEvent`; idempotent). Endpoints: `POST /loans`, `GET /loans?includeArchived=` (`LoanDto.IsArchived` for row badging), `GET /loans/{id}` (archived drillable), `PUT /loans/{id}` (counterparty + notes only), `POST /loans/{id}/payments`, `DELETE /loans/{id}/payments/{paymentId}`, `DELETE /loans/{id}` (soft archive), `POST /loans/{id}/unarchive` (idempotent flip — archive/unarchive are bookkeeping-only and never touch transactions). First incremental migration since the baseline (`20260826202025_AddLoans`, EF CLI); backup `SchemaVersion` bumped **4 → 5** (old backup files stop importing — take a fresh export). See BACKEND.md > Loans slice.
- **Pools slice** (2026-09-06/07, **uncommitted**) — outside investors sharing one account, modelled as a fund: everyone including the user holds units, `NAV = pool value / units`, money in or out priced at the prevailing NAV so nobody else's per-unit value moves. Three entities (`Pool`, `PoolParticipant`, `PoolUnitEvent`), 11 endpoints, migration `20260906212228_AddPools`, backup `SchemaVersion` **5 → 6**. Net worth counts only the user's **share** of a pooled account via a second seam (`IAccountOwnershipSource` — a dated owner *fraction* applied at the asset leg, because a pool stake **rises** and an `ExternalClaim` can only decline). Close and pay are two steps, and a `Distribution` retires its units at **settlement**, not at close — otherwise net worth credits the user with cash already promised to the friends, permanently, on every month-end trend point. A 15-row guard table blocks every write path that could put un-unitised cash on a pooled account, and a four-check reconciliation tripwire replays the ledger against the account and **reports without correcting**. See BACKEND.md > Pools slice and > Account ownership and net worth.
- Application infra: custom CQRS interfaces, `ValidationDecorator` + `LoggingDecorator` (Scrutor), `IApplicationDbContext`, `IDomainEventsDispatcher`
- Infrastructure: `ApplicationDbContext`, EF configs (snake_case, `ComplexProperty` for `Money`, enum-as-string, soft-delete `HasQueryFilter`), `DomainEventsDispatcher`, `AuditableEntitySaveChangesInterceptor`, `DateTimeProvider`
- Api: `Program.cs` with Serilog + Scalar, `IEndpoint` scan, `GlobalExceptionHandler`, `ResultExtensions`, **CORS** allowing `http://localhost:3000`, **`ApplyMigrations()`** on dev startup (creates DB + applies pending migrations, and logs the active DB name)
- **Real vs. test DB split**: the running app uses two databases on local Postgres — `money_management` (real daily data; the default/Visual-Studio Run profiles) and `money_management_test` (disposable QA/smoke-test DB; the **`qa`** launch profile). The split is by connection string per launch profile only; same code, same schema. See BACKEND.md > Local Postgres and QA.md.
- Account edit endpoint: `PUT /accounts/{id}` (`UpdateAccountCommand` → `Account.Update(name, notes)`) renames an account and edits its notes — name + notes only; currency, type, balance, and opening date are fixed at creation. 204 on success, 404 if missing, 400 on validation error. No schema change, no domain event (name/notes don't feed budgets/reports)
- Account archive endpoint: `DELETE /accounts/{id}` (soft archive via `Account.IsArchived`); unarchive via `POST /accounts/{id}/unarchive` (idempotent boolean flip, no migration); guarded hard delete via `DELETE /accounts/{id}/permanent` (409 if the account has any transactions, imports, or linked goals — archive instead)
- **All entity IDs use `Guid.CreateVersion7()`** (RFC 9562 UUIDv7) — sortable by creation time, sequential B-tree inserts. Native `uuid` column in Postgres, no schema change vs v4.
- **EF Core migrations: baseline + AddLoans** — `20260528201519_InitialCreate` (generated via the EF CLI; creates the original 10-table schema with indexes/FKs/`Money` complex properties/enum-as-string/`created_at`+`updated_at`; the earlier incremental history was collapsed into this clean baseline on 2026-05-28, pre-release) followed by `20260826202025_AddLoans` (`loans` + `loan_payments`, first incremental migration since the baseline, same CLI workflow). Applied automatically on dev startup via `ApplyMigrations()`, which also creates the database if missing.
- **Backend: 4 test projects** — Domain / Application / Infrastructure / Api. Domain **263** + Application **537** verified live 2026-08-26 (+44/+50 from the Loans slice); the Infrastructure + Api integration suites (require Postgres; ~107 + ~140 at the last QA count) bring the backend total to ~1050. See QA.md for the live full-suite number. Build is warning-free with `TreatWarningsAsErrors`.

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
- **Loans page (`/loans`)** (2026-08-26) — new sidebar item (HandCoins icon, between Goals and Reports). Two summary tiles ("Owed to me" = Σ outstanding of Given loans, "I owe" = Σ of Received, MDL, with the net-worth-card-style partial-sum + amber warning when any loan lacks an FX rate) above a table: counterparty (links to `/loans/[id]`), Borrowed/Lent badge, principal, repaid, outstanding (+ muted MDL-eq), 100%-capped progress bar, Active/Settled pill, loan date, row actions (Record payment — hidden on Settled — / Edit / Archive). Add-loan dialog: native-radio direction ("I borrowed money" / "I lent money"), counterparty, principal + currency Select, date, **optional** account Select ("No account" default; non-archived accounts filtered to the picked currency; muted hint that linking records a real transaction), notes. Record-payment dialog mirrors it with amount capped at the outstanding client-side (server re-validates). A **"Show archived" switch** (accounts-page pattern) reveals archived loans — Archived badge, row menu collapsed to **Unarchive**, and the summary tiles keep counting only active loans; the loan detail header swaps its action group for a single Unarchive button when archived. The archive confirm warns when the loan still has outstanding > 0 (settle-then-archive is the intended flow). Hooks in `lib/api/loans.ts` keyed at `['loans']`; the money-moving mutations invalidate the same seven query roots as transfers (unarchive invalidates `['loans']` only).
- **Loan detail page (`/loans/[id]`)** (2026-08-26) — goals-detail-pattern workbench backed by `GET /loans/{id}`: header strip (back link, counterparty, direction/status/archived badges, Record payment / Edit / Archive actions — mutating actions hidden when archived), progress card (Outstanding big number + MDL-eq + "repaid X of Y" + capped bar + missing-FX warning), "Disbursed via account on date" line when the disbursement was account-linked, payments table (date / amount / account or — / notes / per-row delete whose confirm warns it will also delete the linked transaction when one exists), skeleton + distinct 404 path.
- **Goal detail page (`/goals/[id]`)** — per-goal workbench backed by `GET /goals/{id}` → `GoalDetailDto`. Header strip (name + target + target-date + mode badge ("Linked: <accountName>" or "Manual") + Archived badge, action group: Edit goal / Update saved (manual only) / Archive). Progress card (Saved figure + percent-of-target subtitle + 120%-capped bar + status pill + missing-FX warning). Pace card (3 cells: Avg monthly contribution, Projected completion + months-at-pace, Required monthly to hit target date — each with null-disambiguating subtitles: "Not enough history" / "Pace too slow" / "Goal already met" / "No target date"). History chart (Recharts line over `savedHistory` with dashed target reference line + conditional dot at `targetDate` when in range; sr-only point enumeration for jsdom tests). Contributions table (Date / signed Amount / Source badge — "Manual" vs "From <linkedAccountName>" / Notes). The goals-table name cell now links to `/goals/[id]` (row-action dropdown still works without navigating).
- **589 Vitest tests across 76 files + 1 Playwright spec for the import flow** — **all green** (verified 2026-08-27; first all-green run since 2026-07-01). *(Two environment breakages from 2026-08-26 were fixed 2026-08-27: Node 26's experimental `localStorage` global shadowed jsdom's and broke the zustand-persist suites — `tests/setup.ts` now probes the ambient binding and swaps in an in-memory `Storage` when unusable; and the `edit-goal-dialog` fixture's stale `targetDate: '2026-08-15'` — the "Unable to find … boom" symptom — now computes its date a year ahead of today. Both tracked in QA.md Known issues.)* The +79 (2026-08-26, all green) cover the Loans feature: loans-table badges/links/MDL-eq/empty state, create-loan-dialog validation + currency-filtered account options + payload shape, record-payment max-amount + payload, archive + delete-payment confirms incl. the linked-transaction warning copy, detail-view branches, summary-tile math + missing-FX case, and the loans hooks' invalidation sets. (The earlier +2 over the prior 170 cover the transactions-table account sub-line shown only when listing across all accounts; the Performance-card "Current value" native-amount test was tightened to reject the duplicated `… in <code>` suffix. The earlier +7 / +1 covers the Settings → Data page: `data-settings-page.test.tsx` — export download URL, card render, file-enables-restore, destructive-warning copy, confirm calls the mutation, success toast with counts + picker reset, API error surfaced). The earlier goal detail page work covers: GoalDetailHeader name/badges + linked-mode gating of Update-saved, GoalProgressCard sizing + status pill + missing-FX, GoalPaceCard three-cell null-subtitle matrix, GoalHistoryChart sr-only enumeration + empty state, GoalContributionsTable sort + sign coloring + source badge, GoalDetailView loading/404/generic/happy paths, and the goals-table name-cell-as-link change). Test setup polyfills `Element.{hasPointerCapture,scrollIntoView}`, `ResizeObserver`, and `window.matchMedia` (the last for sonner's `<Toaster>`); Recharts' `ResponsiveContainer` is mocked at fixed dimensions in chart tests since jsdom can't measure layout.
- `npm run gen:api` script ready (needs backend running on `localhost:5000`)

**Docs** — split into [WIKI.md](./WIKI.md) (product), [BACKEND.md](./BACKEND.md), [FRONTEND.md](./FRONTEND.md).

### Known rough edges

- **A pooled account shows pool-gross almost everywhere, by explicit decision.** `/accounts`,
  the Balance-over-time report and the monthly summary all show the **full** Binance balance, not the
  user's share, and carry a "Pooled" badge instead — the account really does hold that money. Only
  net worth, the net-worth trend and the account-detail Performance card apply the owner's fraction.
- **One pool = one account.** Enforced by an unfiltered unique index. A pool spanning Binance *and*
  Bybit is a materially harder problem and was deliberately deferred; Bybit transfers are owner
  redemptions at NAV.
- **Backup `SchemaVersion` 6 invalidates every v5 export.** There is no cross-version migration of
  backup files, so re-export the day the pools work is committed.
- **The unit model is only as good as the valuations typed into it.** A missed month-end snapshot is
  recoverable (the next payout is simply larger); a **capital event recorded without a valuation is
  not**. The app hard-blocks the latter, but nothing can reconstruct a pre-money value that was never
  captured.
- **~~The Performance card mis-splits a mark that shares its date with a capital event.~~ FIXED
  2026-09-07.** The card used to attribute each balance-adjustment mark at the ownership fraction in
  force *before that day* (`OwnedFractionAsOf(date − 1)`), which is right whenever at most one
  capital event happens per day — the normal case, and what the worked example assumes — and wrong
  when a subscription and a *later* mark land on the **same** calendar date, where the mark was
  attributed as though the pool were still wholly yours. Observed in a same-day bootstrap: a +208.00
  USD mark credited entirely to the owner instead of 1092/2092 of it, over-stating Net P&L by
  **99.43 USD**. (Net worth was never affected — it uses the end-of-day fraction.) Each mark is now
  attributed at the units state in force at the instant it was written, paired to its unit event
  through the pre-money arithmetic the reconciliation tripwire already performs. Two rarer same-day
  orderings still have no evidence in the data to resolve them and fall back to the day's closing
  fraction — see `PoolMarkAttribution`.
- **Per-participant stakes can leave a one-cent residual against the pool total.** Each stake is
  priced independently as `units × nav`, so three stakes rounding to 2dp need not sum to the pool
  value exactly. This is the deliberate price of the owner holding *real* units rather than being
  modelled as a residual — which is the only reason the `Σ participantUnits == totalUnits` check is
  worth anything.

- shadcn CLI was skipped due to Tailwind v3/v4 config conflict — components are hand-written from canonical shadcn templates. Re-verify when shadcn ships Tailwind v4 support.
- `Intl.NumberFormat('ro-MD', { currency: 'MDL' })` renders as `L` in Node 22 / headless Chromium (ICU build). Real Chrome shows `MDL`. Tests assert digit grouping, not the currency code.
- ~~Frontend account cards show opening balance, not derived current balance~~ — **resolved**: `AccountDto.Balance` is the live derived balance (anchor + Σ income − Σ expense) and the cards render it.
- ~~No mobile sidebar drawer — sidebar hides below `md` breakpoint.~~ **Resolved (2026-07-01)** — a `md:hidden` nav drawer (Radix-Dialog-based `Sheet` + shared `SidebarNav`) opens from the header toggle; the desktop rail still collapses. See FRONTEND.md. *(The old "migrations are generated but never run" edge was also retired — the schema is applied on dev startup via `ApplyMigrations()`, as documented in the Backend Done list above.)*
- **Transfer-leg and adjustment directions are mechanically right but semantically fragile** — a transfer's source leg is stored as `Direction = Expense` and destination leg as `Income`; an upward balance adjustment is `Income`, downward is `Expense`. Reports and aggregations must always filter BOTH `IsTransfer = false` AND `IsAdjustment = false` when computing "real" income/expense — there is no compiler check enforcing this. The dashboard handlers (`GetSummary`, `GetNetWorthTrend` — see `BACKEND.md` "Dashboard slice") apply the right filters and have unit tests pinning that behavior, so the current dashboard surface is covered; any *new* slice that touches income/expense aggregates still needs the same care.
- **Maib commission rows split principal and fee at parse time** — for any row where the `comision` column is populated, the parser emits TWO transactions: a primary at `ieșiri − comision` (the user's actual transfer) and a paired `Comision: …` fee row at `comision`, categorized `Bank Fees`. Sum equals the bank's per-row deduction, so the live balance reconciles against `Sold Disponibil` without any balance-calc filter. (The bank's `Sold final` summary number subtracts the fee a second time — that's a maib quirk, not the truth; we mirror `Sold Disponibil`.)
- **Cross-account currency invariant is enforced going forward only** — Phase 4 added the rule "every transaction's currency matches its account's currency" at all write boundaries. Pre-Phase-4 data in a long-lived DB might violate this; the `AdjustBalance` handler uses `Debug.Assert` rather than a hard failure if it encounters mismatched-currency historical rows.
- **`formatMDL` / `formatMDLCompact` removed (2026-07-01)** — both dead helpers were deleted; `formatMoney` (currency-aware) is the single money formatter. The module-level `formatter` const they wrapped is kept (it backs `formatMoney`'s invalid-code fallback).
- maib parser is regex-anchored over PdfPig's concatenated page text — best-effort. Section markers (`#Cont`, `#Cardul`) are stripped before row tokenization so boundary rows survive; tail is tokenized for decimal-formatted numbers only, so trailing junk (page footers, repeated column headers, card-number markers) is ignored. Unparseable rows are skipped rather than throwing. Only the maib statement format is recognised; other banks need their own `IBankStatementParser`.
- **Adjacent monthly maib statements do not tile cleanly at the month boundary — importing them separately can silently lose a day.** A maib statement filters rows by **transaction date** (`Data tranzacției`), but a card purchase made on the last day of a month often **settles** (`Data procesării`) a couple of days into the next month. When that happens, the row can fall through the crack between two month statements: the month-N statement was generated before the row settled and **cuts off at the prior day** (so it never lists the last-day rows), while the month-(N+1) statement **starts at day 1** and does *not* list them either — instead it **bakes their effect into its opening balance** (`Sold inițial`). The two statements then fail to chain: month-N's `Sold final` ≠ month-(N+1)'s `Sold inițial`, and the missing rows are imported by neither. **Confirmed instance (MAIB Gama, investigated 2026-06-21):** two 2026-05-31 expenses — `"PIVZARIADKA" Magazin` (620.00) and `SC VICTORIA CIOBANU SR` (558.99), both processed 06-02 — appeared on *neither* the May (01.05–31.05, ended 05-30, `Sold final` 3 397.87) nor the June (01.06–…, `Sold inițial` 2 218.88) statement. `3 397.87 − 2 218.88 = 1 178.99 = 620.00 + 558.99`, which surfaced as the account reading **1 178.99 too high** in the app (2 366.30 vs the true 1 187.31). A single continuous 01.05–21.06 statement listed both rows and chained correctly. **Import tactic to avoid this:** don't import disjoint per-month statements back-to-back; instead pull statements with **overlapping ranges** (each new import spans from a few days *before* the previous import's last date through today) — the `(date, amount, description)` duplicate dedup will skip the re-listed rows and pick up any boundary stragglers — or periodically import one continuous statement and rely on dedup. The straggler rows can also just be added manually for the affected month. **Guard (shipped — Next steps item 12, Phase 1, 2026-06-21):** the import preview now reconciles the statement's opening balance against the app's running balance and shows a non-blocking warning when they diverge, so this gap surfaces at parse time rather than weeks later.
- **Category multi-select filter** on the transactions page uses a single-value Select for v1 (no popover/checkbox primitive yet).
- **Budget rollover** is in the product brief but not yet implemented — unused budget does not carry over to the next month. Each month starts fresh from the configured `MonthlyLimit`.
- **Budget multi-currency** is deferred. `MonthlyLimit` is forced to `MDL` (the reporting currency) at the factory; non-MDL inputs fail `BudgetErrors.MdlOnly`.
- **Mid-month budget creation** — if a budget is created on the 15th, the corresponding `BudgetPeriod` will only reflect transactions created **after** that moment (the event handler runs on new writes, not historicals). No backfill yet.
- **Goal contribution history is manual-mode only** — `SavingsGoalContribution` rows are written by `PATCH /goals/{id}/manual-saved` capturing the signed delta. **Linked-mode** goals do NOT write to this table; their contributions are *derived* per-read from the linked account's transactions (so the table grows linearly with the user's manual-update cadence, not with their bank-import volume). The "projected completion at current pace" widget on `/goals/{id}` uses the last 90 days as its window; linked-mode pace is `(savedToday - savedAtWindowStart) / months`, manual-mode is `Σ contributions in window / months`. Pace returns null and the projected date returns null when there's less than 30 days of history or the pace is ≤ 0 (going backwards). Multi-currency is still deferred — contributions are MDL only.
- **Goal multi-currency** is deferred. `TargetAmount` is forced to `MDL` at the factory (`SavingsGoalErrors.MdlOnly`); the linked-account branch FX-converts at read time.
- **Goal auto-archive** — a goal that hits `Achieved` is *not* auto-archived; the user must do it manually from the row action.
- **BNM-fetched rates "come back" after delete** — `DELETE /fx-rates/{id}` works on both `Manual` and `BnmAuto` rows, but a deleted `BnmAuto` row will be re-inserted on the next scheduled refresh (or the next `POST /fx-rates/refresh`) as long as an account still holds that currency. The frontend surfaces a `title` tooltip on BNM-row delete actions explaining this. To suppress permanently, either delete the account that holds the currency or create a `Manual` rate (manual wins priority + dedup so the auto fetcher will skip).
- **Reports balance-over-time INCLUDES transfers/adjustments** — this is the slice's one intentional break from the otherwise-uniform `!IsTransfer && !IsAdjustment` rule. Per-account native balance is anchor + Σ signed transactions of EVERY non-deleted row (including transfers and balance adjustments), because those rows really do move the account's balance even though they're excluded from income/expense P&L. Pinned by `GetBalanceOverTimeQueryHandlerTests`. The other four reports (monthly-summary, category-breakdown, top-payees, and any future P&L aggregate) still filter transfers + adjustments — see `GetMonthlySummaryQueryHandler` for the canonical filter.
- **Data restore is destructive and unversioned-tolerant** — `POST /data/import` does a full wipe-then-load inside one transaction (so a failed import rolls back cleanly), but a *successful* import permanently replaces ALL existing data with no undo. It only accepts the current `SchemaVersion` (now **6** — bumped 2026-09-06 when the three `pools` tables were added; v5 was the 2026-08-26 `loans` + `loan_payments` addition; v4 was the 2026-05-29 `category_patterns` addition; see below); any other version is rejected with `UnsupportedSchemaVersion` before the store is touched (there's no cross-version migration of backup files yet). Upload cap is 50MB. The `EfBackupStore` wipe/reinsert **is** covered by integration tests now (`Infrastructure.Tests/Backup/EfBackupStoreTests.cs`) in addition to the handler-level unit tests; the destructive round-trip was also verified manually via the UI on 2026-05-29 (see QA.md row 43). **Backup completeness:** the backup now includes `category_patterns` (learned/seeded keyword rules) — earlier versions silently lost them on restore via the `categories` cascade. `fx_rates` remains the one intentional exclusion (re-fetchable from BNM, never wiped). After a restore, BNM-sourced FX rates behave like the delete case: the next scheduled/on-demand refresh will re-insert any `BnmAuto` rows for currencies still held by an account, even if the restored backup didn't contain them.
- **Migration tooling** — migrations are generated **only** via the EF Core CLI (`dotnet ef migrations add`), with the API process stopped first so the tooling can load `MoneyManagement.Infrastructure.dll` (a running API/VS holds an exclusive lock). The generated migration body needs a one-line manual conversion from block-scoped to file-scoped namespace (the repo enforces `IDE0161`); the auto-generated `.Designer.cs` is exempt. `dotnet ef migrations has-pending-model-changes` must report clean after every migration. The schema baseline is `20260528201519_InitialCreate` + incremental migrations from there (first: `AddLoans`); the database is created + migrated on dev startup via `ApplyMigrations()`.
- ~~**Loans are excluded from net worth**~~ — **Resolved (2026-09-06).** Outstanding borrowed money now reduces net worth and outstanding lent money adds to it, on both the card and the as-of trend. The gap had grown to EUR 6,000 + MDL 14,152 of unrepaid `Received` loans — the dashboard read MDL 183,864 against a true MDL 49,484. See "External claims and net worth" in BACKEND.md. Two behaviours worth knowing, both deliberate:
  - **Only account-linked loans count.** A loan recorded without picking an account never moved a tracked balance, so its cash isn't in gross assets; subtracting the obligation anyway would push net worth wrong the other way. Record loans against an account if you want them in net worth.
  - **Archived loans still count as debt.** Archiving is bookkeeping-only and the app permits archiving with outstanding > 0, so a hidden loan is still subtracted. This means the dashboard "I owe" tile and the `/loans` page "I owe" tile can legitimately disagree — the loans page mirrors its table (active, linked or not), the dashboard mirrors reality.
- **Loan repayments are same-currency only** — a payment's currency (and its optional linked account's currency) must equal the loan's currency. EUR loan repaid from an MDL card: either record the payment without an account link, or FX-transfer to a EUR account first and pay from there. Cross-currency payments (dual-amount, like transfers) are deferred.
- **Loan movements ride the `IsTransfer` flag** — synthesized loan transactions are `is_transfer = true` with no counter account (the established Investment/Withdrawal precedent), so every existing income/expense aggregate excludes them with zero new filter sites. Consequences: they show a "Transfer" badge on the transactions list and land in the account Performance card's Contribution/Withdrawal buckets. A dedicated third flag was rejected — it would touch every fragile `!IsTransfer && !IsAdjustment` filter in the app.
- **Bank-side repayments vs. statement import** — if you repay via bank transfer and later import that month's maib statement, either record the loan payment *without* an account link (the import brings the bank row, and the transfer detector flags it), or link it and rely on the transfer-aware dedup to catch the A2A row at import time. Linking an *existing imported transaction* to a loan payment retroactively is deferred.

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
12. ~~**Import balance-reconciliation warning**~~ **Phase 1 done (2026-06-21)** — **opening-balance reconciliation** at parse time. `ParseStatementCommandHandler` now compares the statement's printed opening balance (`Sold inițial`, already parsed into `ParsedStatementSummary.OpeningBalance`) against the app's computed balance just *before* the statement period (`anchor + Σ signed amounts of all non-deleted rows with date < Period.From` — the same live-balance formula as `GetAccountDetailQueryHandler`) and returns a `ReconciliationDto { StatementOpeningBalance, AppBalanceBeforeStatement, OpeningDelta, OpeningMatches }` on `StatementPreviewDto`. The preview page renders a **non-blocking amber banner** when `!openingMatches` (tolerance 0.01); Commit stays enabled. This is the guard that catches the 1 178.99 MAIB Gama month-boundary gap (see "Adjacent monthly maib statements do not tile cleanly" under Known rough edges) at import time — it fires the moment you parse the next month's statement, because that statement's opening no longer matches the app's running balance. **Phase 2 (deferred):** closing-balance reconciliation at *commit* time (statement `Sold final`/booked closing vs computed post-import balance through `Period.To`) to also catch within-import incompleteness (unchecked rows, over-aggressive dedup) — needs the statement summary + end date threaded into the commit command; not built yet.

13. ~~**UX & robustness polish (PR A)**~~ **Done (2026-07-01)** — mobile sidebar nav drawer (Radix-Dialog `Sheet` + shared `SidebarNav`); import-preview row list virtualized with `@tanstack/react-virtual` (clears the large-statement lag noted under Known rough edges); deleted the dead `formatMDL`/`formatMDLCompact` helpers; refreshed this Build Status (counts + retired the stale mobile-drawer / migrations-never-run / formatMDL edges). Frontend 510 Vitest tests across 67 files, all green. See FRONTEND.md + QA.md rows 52–53.

14. ~~**Loans slice (core)**~~ **Done (2026-08-26)** — personal interest-free lending, both directions, with per-movement optional account linkage riding the `IsTransfer` flag, two-way deletion coupling, `/loans` + `/loans/[id]` pages, backup v5, first incremental EF migration (`AddLoans`). Smoke-tested UI→API→DB (QA.md rows 54–56; P&L exclusion proven live). **Archive-UX polish (2026-08-27, follow-up from user testing):** `GET /loans?includeArchived=` + `POST /loans/{id}/unarchive` + Show-archived toggle + Unarchive row/detail actions + outstanding-balance warning on the archive confirm — archived loans are now browsable and reversibly shelved instead of URL-only. **Follow-ups agreed at design time:** ~~fold outstanding loans into dashboard net worth~~ **done 2026-09-06**; cross-currency repayments; retro-linking an imported transaction to a loan payment. Also outstanding: fix the 2 pre-existing Vitest environment breakages (QA.md Known issues 2026-08-26).

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

### Loans (personal)

Informal lending between the user and people they know — cash borrowed from parents, money lent to a friend. Explicitly **not** bank credit: no interest, no repayment schedule, just a principal and partial repayments until settled. Lives on its own **Loans** tab.

| Field | Notes |
|-------|-------|
| Direction | **Received** (I borrowed — I owe it back) or **Given** (I lent — they owe me) |
| Counterparty | Free-text person name ("Parents", "Ion") — not an account |
| Principal + currency | Fixed at creation; any ISO currency (e.g. 5,000 EUR) |
| Loan date | When the money changed hands (≤ today) |
| Payments | Partial repayments over time; outstanding = principal − Σ payments |
| Status | Computed, never stored: **Active** while outstanding > 0, **Settled** at zero (no auto-archive — mirrors goals) |

**Money-side rule** — creating a loan and recording a payment each take an *optional* account. When one is picked, the app writes a real transaction on that account (`is_transfer = true`, seeded category **Loan**, no counter account — the Phase-4 Investment/Withdrawal precedent) so the account balance stays truthful while income/expense aggregates ignore the movement: borrowing money is not income, and repaying it is not an expense. When no account is picked, the loan/payment exists standalone (money that never touched a tracked account).

Borrowing *more* from the same person is a **new loan**, not an edit — principal, currency, direction, and date are immutable after creation (same reasoning as account currency).

---

### Pooled capital (Binance)

The Binance account is no longer wholly the user's: two friends put in **USD 1,000 each** in
September 2026, for a share of the monthly profit. It is modelled as a **fund**, not as a loan or a
fixed percentage.

- **Everyone holds units, including the user.** `NAV = pool value / units outstanding`. Money in
  issues units at the prevailing NAV; money out retires units at the prevailing NAV. Because a
  subscription or redemption *at NAV* leaves NAV unchanged, nobody else's per-unit value moves by a
  cent — which is what makes mid-month movements free. The user demonstrably does move money
  mid-month, and every non-unit model needs a special case for that.
- **The friends bear losses, pro-rata.** A negative month pays nothing *and* their stake genuinely
  falls; it is not covered from the user's own money.
- **High-water mark, with no stored field.** `payable = max(0, stake value − capital base)`, where
  `capital base = subscriptions − redemptions`. A payout redeems at NAV, which sweeps the stake back
  to exactly the base, so the rule is permanent with no reset logic. **The cost, which was stated to
  the friends in writing: a green month can pay zero** — down 15% then up 10% leaves them below
  their 1,000 and nobody is paid.
- **The pool is the whole Binance account, one typed number.** Futures / earn / fiat / spot are not
  tracked separately, so moving money between them is invisible to the app. Value leaves the pool
  only when it lands in **another tracked account** (Bybit) — one rule, no special cases. Include the
  **BNB balance** in the total every time; either convention nets out, but *switching* between them
  manufactures phantom profit.
- **Close and pay are separate steps.** The month is marked at month-end; the USDT physically leaves
  at the start of the next month. Dating the payout at close would leave that cash in the next
  snapshot, where it is re-attributed as fresh profit — paying the friends twice on the same money,
  every month.
- **Every capital event forces a valuation.** The app makes typing Binance's real total a hard
  requirement before any money crosses the boundary. That is roughly 2–4 forced valuations a month,
  and it is the price of the unit model's exactness.
- **No performance fee**, but **real costs are shared**: server bills the user pays from their own
  pocket are reimbursed pro-rata as a unit transfer — no cash leg, NAV unchanged. BNB trading fees
  are recorded nowhere, because they are bought with pool money and spent on pool trades, so the
  snapshot already carries them.
- **Reinvestment needs no code.** A friend who says "leave it in" simply takes no distribution; their
  distributable accumulates and the high-water rule keeps it honest.

Net worth counts **only the user's share** of the account, and a "Not my money" figure reports the
rest. `/accounts`, Balance-over-time and the monthly summary deliberately keep showing the **full**
account value with a "Pooled" badge — the account really does hold that money. The account-detail
**Performance card is owner-only**, because its formulas would otherwise book the friends' capital as
the user's contributions and their payouts as the user's withdrawals.

**Tax is not modelled**, here or anywhere in the app. Distributions default to gross. This is not tax
advice.

## Features

### 1. Dashboard

Landing page showing a snapshot of the current financial state:

- **Total assets / I owe / Net worth** — three tiles reading left to right as the equation itself. *Total assets* = **your share** of every non-archived account balance in MDL — for a pooled account that is your fraction of it, not the whole thing, with a sub-line naming how much sits in your accounts without being yours. *I owe* = outstanding on account-linked loans you borrowed. *Net worth* = assets − owed + money lent out (the last term only shows when non-zero). Backed by `GET /dashboard/net-worth`
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
  - **Month-boundary gap — import overlapping ranges, not disjoint months.** Because maib filters by transaction date but settles last-day purchases into the next month, importing strictly month-by-month can drop rows that straddle the boundary (see "Adjacent monthly maib statements do not tile cleanly" under Known rough edges). Pull each statement starting a few days before the previous import's last date (or import a continuous range); dedup skips the overlap and catches the stragglers.

### 3. Accounts

- List all accounts with current balance
- Add / edit / archive / unarchive accounts; permanently delete only when an account has no transactions, imports, linked goals **or capital pool** (otherwise archive)
- Account detail view: full transaction history, balance-over-time chart
- A **"Pooled" badge** on any account that holds other people's money. Such an account still shows its **full** balance here and in the balance-over-time chart — it really does hold that money — but its **Performance card counts only your share**, so the other participants' money in and out is excluded rather than booked as your contributions and withdrawals
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

### 6. Loans

- Dedicated **Loans** tab listing loans **given** and **received**: counterparty, principal, repaid so far, outstanding (native + MDL-equivalent), progress bar, Active/Settled status
- Two summary tiles: **Owed to me** (Σ outstanding given) and **I owe** (Σ outstanding received), in MDL
- Record partial repayments; per-movement optional account link writes the matching transfer-flagged transaction (excluded from income/expense reports, included in account balances)
- Deleting a payment also soft-deletes its linked transaction; deleting a loan-linked transaction from the Transactions page reactively removes the payment (domain event handler)
- Loan detail page: progress, disbursement info, payment history
- Edit is counterparty + notes only; archive when done (settled loans stay visible until archived). **Archive is bookkeeping-only** — it hides the loan from the list and the summary tiles but never touches the account or its transactions (archive ≠ undo; delete the disbursement transaction on the Transactions page to reverse the money movement)
- **Show archived** toggle on the list (accounts-page pattern) reveals archived loans with an Archived badge; archived rows/detail expose **Unarchive**. Archiving a loan that still has outstanding > 0 shows a warning in the confirm dialog (the intended lifecycle is settle → archive)
- v1 limits: repayment currency must equal the loan currency (record the payment without an account link, or FX-transfer into a matching-currency account first); borrowing more from the same person = a second loan

### 7. Pools

For an account that holds other people's money alongside your own.

- List of pools with your share, outside capital, pool value and any unpaid payouts; **Show archived** toggle
- Pool detail: value and price-per-share, who holds what **as percentages**, the full movement ledger, and a reconciliation panel that appears only when something does not add up
- Record money in / money out, close the month, mark a payout as sent, record a shared cost, add a participant, archive the pool
- **Every action that moves money first demands the exchange's real total right now** — including BNB — because a share is priced against the pool's value at that moment. The app refuses to record the movement without it
- **Closing a month and paying it out are two separate steps.** Closing works out what is owed and retires the matching shares but moves no money; you mark each payout as sent on the day the transfer actually leaves
- Nobody is paid below what they put in, so **a month the pool went up can still pay out nothing** — that is the high-water rule working, not a bug

### 8. Reports

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

### 9. Settings

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
