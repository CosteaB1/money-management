# QA & Manual Smoke-Testing

This file owns **manual / end-to-end verification** for the app: the pre-commit smoke-test protocol AI assistants and humans follow, and a living A→Z test matrix of every feature with its last verified status.

Related: [WIKI.md](./WIKI.md) (product), [BACKEND.md](./BACKEND.md), [FRONTEND.md](./FRONTEND.md). Automated tests live in `tests/MoneyManagement.*.Tests` (backend, xUnit) and `web/tests` (Vitest) + `web/e2e` (Playwright spec). This file is about the **interactive UI→API→DB** checks that automated tests don't replace.

---

## Pre-commit smoke-test protocol

**Rule (also in `CLAUDE.md`): before committing any change — feature or bugfix — run a Playwright smoke test that exercises the touched flow through the real UI, hitting the backend and database, and confirm nothing regressed.** A green automated suite is necessary but not sufficient; this catches wiring/integration breaks the unit tests can't.

> **Real vs. test database — read this first.** The app ships with two databases on the local Postgres (`localhost:5432`):
> - **`money_management`** — your **real** data. The default launch profiles (`http` / `https`, and a bare Visual Studio "Run") point here. **Never run a smoke test against it.**
> - **`money_management_test`** — the disposable **QA / smoke-test** DB. Selected only by the **`qa`** launch profile, which overrides the connection string via `ConnectionStrings__Default`. The committed `.mcp.json` points the Postgres MCP here.
>
> **Smoke tests MUST run under the `qa` profile.** The API logs `Using database '<name>' on host '<host>'` at startup — confirm it says `money_management_test` before driving any flow. Both DBs are auto-created + migrated + seeded on first run by `ApplyMigrations()`; no manual `createdb` step.

### Setup (once per session)

0. **MCP servers** — committed in project-scope `.mcp.json` at the repo root: **two** read-only Postgres servers (`postgres-test` → `money_management_test`, `postgres-real` → `money_management`) plus Playwright. First time you open this repo in Claude Code on a new machine, approve the trust prompt for project-scoped MCPs; then restart the session so the `mcp__postgres-test__*`, `mcp__postgres-real__*`, and `browser_*` tools surface. The first `npx` invocations will fetch each server and (for Playwright) Chromium.
1. **Postgres** running on `localhost:5432`. On the current dev machine this is a **native install** (PostgreSQL 17 service `postgresql-x64-17`); no Docker needed. (`docker compose up -d` from the repo root is the portable alternative on machines without a native install — see BACKEND.md > Local Postgres.)
2. **Backend** — the smoke-test API runs on its **own port `:5180`** against the **test** DB, deliberately separate from the real dev API on `:5179`. **Select the `qa` launch profile** (it sets `applicationUrl=http://localhost:5180` + overrides the connection string to `money_management_test`):
   - Visual Studio: pick **`qa`** in the run-profile dropdown, then Run.
   - CLI: `dotnet run --project src/MoneyManagement.Api --launch-profile qa`
   - ⚠️ **MANDATORY before any write/curl:** confirm the API actually came up on `:5180` AND its startup log reads `Using database 'money_management_test' …`. The dedicated port means a *failed* start fails loudly (connection-refused on `:5180`) instead of silently hitting the real API on `:5179`. **Never `curl`/drive `:5179` during a smoke test** — that is the real app. (A `TestCrypto` account once leaked into the real DB this way: a backgrounded `qa` API hadn't bound `:5180`, and a `curl` to `:5179` hit the running real app.)
3. **Frontend** — the smoke-test web runs on its **own port `:3001`**, pointing at the `:5180` test API, so it never collides with the real web on `:3000`:
   `NEXT_PUBLIC_API_BASE_URL=http://localhost:5180 npm --prefix web run dev -- -p 3001`
   (The API's CORS allows both `:3000` and `:3001`.) Drive the Playwright browser at `http://localhost:3001`. The committed `web/.env.local` keeps `:5179` for the real web; the inline env var above overrides it for the QA run only.
   - ⚠️ **The web instance — not the backend port — decides which API/DB is hit** (`NEXT_PUBLIC_API_BASE_URL` is baked per instance). Running a second backend on `:5180` does **nothing** on its own: if you drive the real web on `:3000` it still goes `:3000 → :5179 → real DB`. So you MUST bring up the `:3001` QA web and **drive only `:3001` — never `:3000`** during a smoke test.
   - ⚠️ **MANDATORY verify before any mutating action:** after the first page load, check `browser_network_requests` and confirm the API calls target **`http://localhost:5180`** (not `:5179`). A `:3001` web can only reach `:5180` (or 404 on its own origin) — it has no path to the real API — so this check positively proves you're isolated.
4. **DB inspection** — smoke tests run against `money_management_test`. Two options:
   - **Postgres MCP** — use `mcp__postgres-test__query` for test-DB assertions during smoke tests. (`mcp__postgres-real__query` exists too, pointing at the real `money_management` DB for ad-hoc debugging — both are read-only, so neither can mutate data.)
   - **Host psql** — native `psql.exe` is present at `C:\Program Files\PostgreSQL\17\bin\psql.exe`. One-shot:
     `& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -U postgres -h localhost -d money_management_test -c "SELECT …"` (set `$env:PGPASSWORD` first, or use the docker `docker exec money-management-db psql …` form when running the container alternative).

### Per-change loop

1. Identify which flow(s) the change touches.
2. Drive those flow(s) in the browser via the Playwright MCP (`browser_navigate` / `browser_snapshot` / `browser_click` / `browser_fill_form` / `browser_file_upload` / `browser_evaluate`).
3. After each mutating action, **cross-check the database** (counts, amounts, flags, `created_at`) — the UI value and the DB value must agree.
4. Check the browser **console** (`browser_console_messages`) — no new errors/warnings introduced.
5. Run the **core regression path** below regardless of what changed.
6. Record the result in the matrix (date + pass/fail + notes). Commit only when green.

### Driving the Radix UI from Playwright (lessons from the 2026-05-29 pass)

The UI is built on Radix dialogs/selects. Two traps cost real time; encode them in any future automation:

- **Stale refs**: element refs from a `browser_snapshot` are invalidated on every React re-render, and Radix re-renders constantly. Do NOT batch many ref-dependent steps in one message — capture, act, re-capture. Better: drive forms with `browser_evaluate` (find element + native value-setter + dispatch `input`/`change`) which is ref-free and compact.
- **Native value setter**: React ignores `el.value = x`. Use `Object.getOwnPropertyDescriptor(proto,'value').set.call(el, v)` then dispatch `input`+`change`. Call it via `s.call(el,v)` — never destructure the setter into a bare variable (`Illegal invocation`).
- **Duplicate testids**: some controls (e.g. `add-transaction-button`) appear twice (header + table empty-state). A bare locator throws a strict-mode violation — target index 0 via evaluate, or scope to the dialog.
- **Radix Selects are not `<select>`**: click the `[role=combobox]` trigger, then click the `[role=option]` — `browser_select_option` fails on them.
- **Field selectors** (Add-transaction dialog): `input[name="amount"]`, `textarea[name="description"]`, `textarea[name="notes"]`; account/category are Radix selects `[data-testid="transaction-account-select"]` / `[data-testid="transaction-category-select"]`; submit is `[role=dialog] button[type=submit]`.

### Core regression path (always run)

- Dashboard loads; net worth + monthly summary render without error.
- Accounts list loads; create one account → appears in table + DB row matches.
- Add one transaction → balance recomputes in UI and reconciles in DB (`anchor + Σincome − Σexpense`).
- No new console errors/warnings.

---

## A→Z feature test matrix

Legend: ✅ verified via UI→API→DB · 🟡 verified via API/DB only (UI not driven) or partial · ⬜ not yet exercised manually (may have automated coverage).

**Last full pass:** 2026-06-04 → 05 (overnight autonomous full-surface sweep on isolated `money_management_test` via `:3001`/`:5180`; isolation re-proven — zero `:5179` requests all session). Covered: automated baseline (**934 backend** across 4 projects — Domain 213 / Application 474 / Infrastructure 107 / Api.Tests 140, the latter two NEW since 2026-06-01 — + **484 frontend**); **all 48 API endpoints** (happy + 17-case negative battery, DB-verified); full CRUD via UI+API (accounts incl. archive/unarchive/permanent-delete, transactions, transfers, balance-adjust, categories, budgets, goals, fx incl. refresh/backfill); real maib PDF import parse→commit→duplicate + all preview interactive controls (incl. cross-currency counter); destructive backup/restore round-trip; reports controls, filters/pagination, CSV, dashboard, header/theme. **Result: 3 bugs found — all FIXED + verified.** **B-1** import-preview duplicate React key (`key={idx}` + Vitest test). **B-2** editing an already-manual goal wiped its saved amount (data loss) — handler now switches goal mode only on an actual change. **B-3** date defaults/validation were judged in different timezones (UI local vs backend UTC) → default-dated creates failed nightly for UTC+ users — resolved by judging dates in **UTC on both ends** (location-neutral; no timezone assumption). _(Prior full pass: 2026-06-01 — bug-hunt sweep, 1 bug F-3.1 malformed→500 since fixed.)_

| # | Area | Flow | Status | Last result / notes |
|---|------|------|--------|---------------------|
| 1 | Dashboard | Loads, empty states | ✅ | Renders on empty DB; all widgets show empty placeholders; 0 console errors |
| 2 | Dashboard | Net worth (cross-account + FX) | ✅ | 9.750,00 MDL = 1.000 MDL + 500 USD×17.50 (8.750) ✓; API `balanceMdl` matches |
| 3 | Dashboard | Monthly summary / recent transactions | ✅ | `/dashboard/summary` returns income/expense/net; transfers + adjustments excluded server-side ✓ |
| 4 | Accounts | Create MDL account | ✅ | `maib salary card` BankCurrent 1000 MDL → DB exact; `created_at` real UTC |
| 5 | Accounts | Create non-MDL account | ✅ | `Binance` CryptoExchange 500 USD; MDL eq 8.750,00 ✓ |
| 6 | Accounts | Balance adjustment (3-mode) | ✅ | UI: Update balance → Balance adjustment → new total 600 USD → synthetic `Income 200 USD is_adjustment=true` cat Balance Adjustment; balance 400→600; excluded from `/dashboard/summary` ✓. **2026-06-01 fix:** the dialog's "Notes" field was being written as the transaction **Description** (and real Notes left null) — now stored as **Notes**, with Description fixed to the kind label ("Investment"/"Withdrawal"/"Balance adjustment"). xUnit updated; UI→API→DB re-drive pending on next smoke run |
| 7 | Accounts | Archive / unarchive | ✅ | Archive → `is_archived=true`, hidden from list + dropdowns; Show-archived reveals; Unarchive → false |
| 8 | Accounts | Account detail page (perf + balance-over-time) | ✅ | Performance KPIs, balance-over-time chart w/ MDL-eq + missing-FX notice, Activity table, opening-balance footer; 0 console errors |
| 9 | Accounts | Permanent delete guard (409 w/ history) | ✅ | Account with txns → 409 "archive instead", row preserved; empty `TempDelete` → permanently removed |
| 10 | Transactions | Add expense (MDL) | ✅ | 200 MDL Groceries → balance 1000→800 ✓; flow-filtered category list ✓ |
| 11 | Transactions | Add transaction on USD account | ✅ | Amount label switches to "Amount (USD)"; 100 USD → balance 500→400 ✓; `balanceMdl` 7000 |
| 12 | Transactions | Account sub-line in all-accounts view | ✅ | Owning account shown as sub-line; omitted on single-account list |
| 13 | Transactions | Inline recategorize | ✅ | Linella → Restaurants → Groceries; `updated_at` advances past `created_at` ✓ |
| 14 | Transactions | Inline note add/edit | ✅ | "paid by card at Linella" persisted via note dialog → DB ✓ |
| 15 | Transactions | Delete transaction | ✅ | Soft-delete (`is_deleted=true`) + balance reverts. Delete control now on **both** the main `/transactions` list (added 2026-05-29) and the account-detail Activity table. Re-verified via UI on the main list: `is_deleted=true`, account balance reverts by the deleted row's amount. |
| 16 | Transactions | Filters (account/date/category/direction/transfer/adjustment) | ✅ | All facets narrow rows; default 30-day window + pagination (pageSize 25) |
| 17 | Transfers | Manual cross-currency transfer dialog | ✅ | maib (MDL) → Binance (USD) 175 MDL → dest auto-fills 10,00 USD @ 17.50; two legs `is_transfer=true`; both excluded from `/dashboard/summary` ✓ |
| 18 | PDF Import | Parse + summary reconciliation | ✅ | Sample statements; "opening + in − out = closing" reconciles |
| 19 | PDF Import | Row-derived In/Out/Fees parse check | ✅ | Header totals match row-derived block |
| 20 | PDF Import | Transfer auto-detection | ✅ | A2A / ATM / Retragere rows flagged `is_transfer=true` |
| 21 | PDF Import | Auto-categorization (suggester) | ✅ | LINELLA→Groceries, MAX KEBAB→Restaurants, etc. **2026-06-01:** suggester precedence changed from longest-keyword to **earliest-occurrence wins** (ties→longest); `ATM` keyword repointed Transfers→**Withdrawal**. Verified via `/imports/parse` on the Salary PDF: all 7 `ATM …` rows (incl. `ATM MAIB LINELLA MOSILOR`, previously Groceries) → Withdrawal; OCN IUTE→Credit Payment, ORANGE/APA CANAL/ENERGOCOM→Bills, A2A→Transfers, salary/dividende→Salary all unregressed |
| 22 | PDF Import | Commission split (fees in Out) | ✅ | `Comision: …` rows emitted, category Bank Fees |
| 23 | PDF Import | Commit → DB + import batch | ✅ | 1174 transactions across 3 batches; balances reconcile |
| 24 | PDF Import | Duplicate detection (re-import) | ✅ | Re-import same PDF → "Already imported", Import 0, 0 new rows |
| 25 | PDF Import | Original FX amount capture | ✅ | Foreign-currency card rows store original amount + currency |
| 26 | PDF Import | Learn-with-confirm keyword rule | 🟡 | Pattern table writes confirmed via Categories UI (row 29); learn-on-commit path not separately re-driven this pass |
| 27 | PDF Import | Cross-currency counter account (MDL→USD etc.) | ✅ | **2026-06-01 (driven UI→API→DB) — FIXED a latent bug.** Import endpoint's `CommitTransactionRequest` was missing `CounterAmount`, so the UI-entered amount was silently dropped → *every* cross-currency import 400'd `counter_amount_required`. Added `CounterAmount` to the request DTO + mapping. Verified: assigned a USD counter (ReproUSD) to a 1000-MDL transfer row, entered 55.5 → counter leg persisted **55.50 USD**; a same-currency counter (ReproCash) leg persisted **1000 MDL** with no amount needed. 0 console errors. (Never exercised before — the gap that hid this.) |
| 28 | Categories | Seeded on first run | ✅ | **2026-06-01** (`money_management_test`): **17 categories** + 34 patterns seeded — added **Home** (Expense, `…011`, `#6366f1`), backfilled by id into the existing DB ("Seeded 1 default categories"); appears in Categories UI |
| 29 | Categories | Manage (create/rename/archive) + patterns UI | ✅ | **2026-06-01**: created `Smoke Pets` (Expense) via the refactored shared **`CreateCategoryDialog`** → DB row + count 18 ✓, 0 console errors (1 pre-existing benign `<input type=color>` warning). Added Expense w/ **Home** category → Smoke Wallet balance 1000→750 reconciles in UI + DB ✓. (Prior 2026-05-29: `QA Test Category` + keyword `QATESTSHOP` → `category_patterns` `Learned` ✓) |
| 30 | FX rates | Conversion at read (account MDL-eq) | ✅ | 500 USD → 8.750,00 MDL |
| 31 | FX rates | No seeding (table starts empty) | ✅ | Table started empty; only manual/BNM populate |
| 32 | FX rates | BNM refresh + backfill | ✅ | Refresh inserted 3 BnmAuto USD rows (Manual still wins on collision); backfill over a date range added historical BnmAuto rows |
| 33 | FX rates | Manual add / delete / source column (UI) | ✅ | Manual USD→MDL 17.50 added; Source column shows Manual/BNM badges; manual rate add+delete confirmed |
| 34 | Budgets | Create | ✅ | Groceries 1500 MDL; `created_at` real UTC ✓ |
| 35 | Budgets | Spent rollup | ✅ | Over pre-existing data Spent shows 0 until rebuild (by design); after `rebuild-all-periods` → 13 periods, current month Spent 3.051,11 MDL, status "Over" ✓ |
| 36 | Budgets | Edit limit / archive / rebuild periods | ✅ | Edit limit → 2000; archive → `is_archived=true`; **"Rebuild periods" button now surfaced on /budgets** (added 2026-05-29). Clicked via UI → recomputed Groceries May `3051.11 → 3151.11` (corrected real reactive-rollup drift) |
| 37 | Goals | Create (linked mode) | ✅ | `Clean Linked Goal` linked to Binance → `linked_account_id` persists ✓ (re-verified with correct `goal-linked-account-select`) |
| 38 | Goals | Manual mode + update saved + contribution history | ✅ | Manual goal; "Update saved" 8000 → `manual_saved_amount_value=8000` + 1 `savings_goal_contributions` row (delta 8000) ✓ |
| 39 | Goals | Goal detail page (pace, history chart) | ✅ | Renders pace card + history chart. **Duplicate-key warning FIXED (2026-05-29)** — `savedHistory` now strictly increasing by `asOf` (backend dedupe + guarded baseline insert). Re-verified manual + linked goals via UI: 0 console warnings |
| 40 | Reports | Monthly summary / category / payees / balance / YoY | ✅ | All 5 tabs render, 0 console errors; category-breakdown numbers reconcile to DB |
| 41 | Reports | CSV export | ✅ | `/reports/transactions.csv` → 200 text/csv with snake_case header + rows |
| 42 | Data | Backup export (JSON download) | ✅ | `/data/export` 200 JSON; schemaVersion present; arrays for all entities (accounts 3, transactions 1174, …) |
| 43 | Data | Restore (destructive replace) | ✅ | **Driven 2026-05-29** (round-trip export→import on live DB, via UI). **Found + fixed a DATA-LOSS bug** (see Known issues): restore cascade-wiped `category_patterns` because they weren't in the backup. Now backed up (schema bumped **v3→v4**). v4 round-trip preserves all 9 entities (incl. 1 soft-deleted tx) + `fx_rates` (30, by design); old v3 backup now rejected with 400 |
| 44 | Settings | Theme toggle / sidebar collapse | ✅ | Sidebar 240→64px collapse/restore; theme toggle fires |
| 45 | Cross-cutting | `created_at`/`updated_at` stored UTC on every entity | ✅ | Confirmed real UTC on accounts, transactions, budget, goal, adjustment, transfer; 0 epoch rows |
| 46 | PDF Import | Inline "+ New category…" during import | 🟡 | **2026-06-01**: per-row category picker's "+ New category…" opens the same shared `CreateCategoryDialog` (flow defaulted to row direction) and assigns the new id to that row — Vitest-covered (2 tests in `import-preview.test.tsx`) + the shared create→DB path driven live via row 29. Full PDF-upload drive skipped (no fixture statement on this machine) |
| 47 | PDF Import | Per-row note ("Add note" → commit `notes`) | ✅ | **2026-06-01 (driven UI→API→DB)**: uploaded real maib Salary PDF (46 rows) on `money_management_test`; reveal-on-demand StickyNote affordance in Description cell → added note to row 0 (`OCN IUTE CREDIT SR`) → commit → DB: that row `notes='SMOKE NOTE alpha-7391'`, and **exactly 1 of 46** imported rows has a non-null note (blank rows omit `notes` ✓). Persisted on ≤500. Backend `TransactionToImport.Notes` + validator + `Transaction.Create(notes:)`. Also Vitest + xUnit covered. 0 console errors |
| 48 | Transfers | Note mirrored onto **both** legs | ✅ | **2026-06-01 (driven UI→API→DB)**: (a) imported transfer w/ counter account → note now copied to the counter leg too (verified ATM→Cash note on both legs); (b) manual New-transfer dialog gained an optional Notes field → recorded ReproImport→ReproCash 777 MDL note `XFER NOTE gamma-99` → DB shows the note on **both** the source (Expense) and destination (Income) legs. 0 console errors; xUnit both-legs tests added (import + transfer) |
| 49 | Accounts | Edit (rename + notes) | ✅ | **2026-06-08 (driven UI→API→DB)**: new `PUT /accounts/{id}` + Edit dialog (Pencil button on detail page). Renamed `E2E ImportUI` → `E2E ImportUI RENAMED`: dialog prefilled from account, PUT → **204**, DB `name` updated + `updated_at>created_at`, heading re-rendered, dialog closed, 0 console errors. Name + notes only by design. (A suspected double-PUT was investigated and ruled out — `fetch`-instrumented single click fires exactly **1** PUT; the earlier appearance was an artifact of `browser_network_requests` showing the cumulative session log across two separate manual submits.) |
| 50 | Accounts | GBP currency support | ✅ | **2026-06-08 (driven UI→API→DB)**: added `GBP` to the create-account + FX-rate currency dropdowns (currency is open ISO data; backend/format/BNM-fetch already agnostic). Verified dropdown lists `MDL/USD/EUR/RON/GBP`; created `Smoke GBP Wallet` Cash 250 GBP → POST **201** → DB `balance_currency=GBP`, UI row renders `250,00 GBP` (MDL-eq `—` until a GBP→MDL rate exists, expected). 0 console errors. |

### Known issues found during testing

#### 2026-06-04 → 05 overnight autonomous full-surface sweep — 3 bugs found

Full E2E pass on the isolated test stack (`:3001`/`:5180`, DB `money_management_test`) extended overnight to every endpoint, CRUD path, and UI button + the automated baseline (934 backend / 484 frontend). Isolation re-proven: zero `:5179` requests all session. **3 bugs found (1 fixed, 2 fixed on follow-up)** — summarised here:

- **B-1 [FIXED · frontend]** Import-preview duplicate React key (non-unique row key collided on the parser's intentional duplicate statement rows → 6 console errors/statement). `key={idx}` + Vitest regression test. *(Missed by the 2026-06-01 sweep.)*
- **B-2 [FIXED · backend, data loss]** Editing an already-manual goal wiped its saved amount to 0 (`UpdateGoalCommandHandler` always called `Unlink()`). Now only switches mode on an actual change.
- **B-3 [FIXED · frontend+backend, timezone]** Date defaults/validation were judged in different zones — the UI defaulted/validated transaction/transfer/adjust/goal dates in the browser's **local** tz, the backend future-check used **UTC** → default-dated creates failed nightly for UTC-positive users (`transaction.date_in_future`). Resolved by judging dates in **UTC on both ends**: the frontend now defaults/validates with `new Date().toISOString().slice(0,10)` (UTC) and the backend keeps `DateOnly.FromDateTime(DateTime.UtcNow)`. Location-neutral — no timezone assumption, correct for a user anywhere (trade-off: "today" rolls over at UTC midnight, not the user's local midnight). *(An interim Europe/Chisinau "reporting timezone" fix was tried and reverted in favour of UTC-everywhere so a non-Moldova user isn't pinned to Moldova time.)* Audit timestamps stay UTC.
- **[Verified clean]** All 48 endpoints; 17-case negative battery (correct 4xx incl. malformed→400); full CRUD + lifecycle + import + backup/restore round-trip + reports/filters/CSV/dashboard/chrome; all prior regression fixes hold.

- **[Verified clean — driven UI→API→DB]** Account create (MDL `E2E Salary MDL` 5000 + USD `E2E Crypto USD` 1000, real UTC `created_at`, MDL-eq ×17.31); expense add → balance 5000→4800 reconciled; inline recategorize (Groceries→Restaurants, `updated_at` advanced), note add, soft-delete + balance revert to 5000; account/direction filters wire through (`pageSize=25`); cross-currency transfer 175 MDL→10.10 USD (both legs `is_transfer=true` + counter, **note mirrored on both legs**); 3-mode balance adjustment 1010.10→1100 USD (synthetic Income 89.90, `is_adjustment=true`, **Notes stored as Notes not Description** — c440550 fix holds); FX manual RON→MDL add + delete; category create+archive (`E2E Travel`); budget create (Restaurants 1500) + **reactive spend rollup** (new expense → `BudgetPeriod` 200) + rebuild-periods (no drift); goal linked (live MDL-eq 19.038,80 from 1100 USD) + manual update-saved 2000→3500 (+contribution delta 1500); goal detail page **0 duplicate-key warnings**; all 5 report tabs render + Categories reconciles to DB; CSV export 200 `text/csv` exact header; **full destructive backup/restore round-trip via the UI** (schemaVersion 4, all 9 entities preserved incl. **`category_patterns` 35→35**, soft-deleted rows, linked-goal FK, fx_rates; app healthy post-restore). 0 console errors throughout (only the known benign `<input type=color>` warning on the category dialog).
- **[Verified clean — 17-case negative/edge battery via API]** All guards return correct 4xx: flow-mismatch→400, same-account transfer→400, amount ≤ 0 (zero & negative)→400, bad currency (2-char)→400, **malformed enum→400 + malformed JSON→400** (2026-06-02 `GlobalExceptionHandler` fix holds), fx same-currency→400, cross-currency transfer missing dest amount→400, permanent-delete with history→**409**, adjustment on non-eligible BankCurrent→400, unknown id→404, duplicate keyword→409, dashboard months-out-of-range→400.

#### 2026-06-01 bug-hunt sweep — full regression + edge testing

Full A→Z re-drive (all 47 rows) + negative/edge battery on the isolated test stack. **One bug found.**

- **[FIXED 2026-06-02 · was Low severity · backend]** **Malformed request bodies returned HTTP 500 instead of 400.** Any POST/PUT with an invalid enum value (`{"type":"Bogus"}`, `{"direction":"Sideways"}`, `{"kind":"Bogus"}`) or syntactically broken JSON returned `500 Server Failure` instead of a `400` ProblemDetails. Root cause: `GlobalExceptionHandler` (`src/MoneyManagement.Api/Infrastructure/GlobalExceptionHandler.cs`) unconditionally mapped every exception to 500; minimal-API binding throws `JsonException`/`BadHttpRequestException` on bad input. **Fix:** the handler now branches via an `IsMalformedRequest` helper — `BadHttpRequestException` → its `.StatusCode` (400), bare `JsonException` → 400, logged at `LogWarning`; all other exceptions keep `LogError` + 500. Re-verified live: the 4 repro cases now return 400 with `"The request body is malformed or contains invalid values."`; typed FluentValidation 400s and happy paths unchanged. No automated test (Api project has no test project — same v1 deviation as `EfBackupStore`); verified via curl. (No user-facing path ever triggered this — the typed UI never sends bad input.)
- **[Verified clean]** All 14 business-rule guards return correct 4xx (flow-mismatch, dup-keyword 409, months-range, same-account transfer, bad currency, non-positive amount, fx same-currency, unsupported/malformed backup, missing cross-currency dest amount, archived-account drill-through 200, 404s, non-eligible-type adjustment). All previously-fixed regressions (c440550 adjustment-notes, cross-currency import `counterAmount`, restore `category_patterns` preservation, ATM→Withdrawal suggester, transfer note on both legs, goal-detail duplicate-key) re-verified holding.

#### 2026-05-29 third sweep — fixes pass + restore drive

All four open items from the second sweep were addressed, and driving the previously-skipped restore (row 43) surfaced a new data-loss bug that is now fixed. Everything below was re-verified through the real UI → API → DB.

- **[FIXED · was high-severity · DATA LOSS — backend]** **Destructive restore silently destroyed all `category_patterns`.** `POST /data/import` wipes `categories`; the `category_patterns.category_id` FK is `ON DELETE CASCADE`, so the wipe cascade-deleted every learned/seeded keyword rule — but `category_patterns` was **not** in the backup and was never reinserted. Proven live: a round-trip took `category_patterns` 35 → **0**. The UI even claimed the backup is "everything needed to restore from scratch" — it wasn't. **Fix:** `category_patterns` is now a first-class backed-up entity (export + explicit wipe-before-categories + reinsert-after-categories in `EfBackupStore`); `BackupSchemaVersion.Current` bumped **3 → 4**; `BackupDocument`/`ImportDataResult` carry the new array/count; handler rejects a doc missing the array as malformed. Re-verified: v4 round-trip preserves `category_patterns` (34 → 34) and all other entities; old **v3 backups are now rejected** with `400 unsupported_schema_version` (before the store is touched). `fx_rates` remains intentionally excluded + preserved. Unit tests updated (563 backend tests pass). NB: `EfBackupStore` itself still has no automated integration coverage (no Infrastructure test project — accepted deviation); the round-trip was verified manually here.
- **[FIXED · frontend+backend]** **Goal detail history chart duplicate-key warning** (row 39). Root cause was backend, not cosmetic-only: `GetGoalDetailQueryHandler` emitted two `SavedHistory` points with the same `asOf` for a goal created today with a same-day contribution (the `(createdOn,0)` baseline collided with `(today,saved)`). Fixed by guarding the baseline insert on `createdOn < firstPoint.asOf`, collapsing the empty/created-today case to a single point, and a final `DedupeByAsOf` pass on all three builders. Re-verified: 0 console warnings on the manual goal that originally repro'd it, and on a linked goal.
- **[FIXED · frontend]** **No delete control on the main `/transactions` list** (row 15). Added `allowDelete` to the main-list `TransactionsTable`. Verified: per-row Trash → confirm → `DELETE /transactions/{id}` → `is_deleted=true` + balance reverted.
- **[FIXED · frontend]** **No "Rebuild periods" button on `/budgets`** (row 36). Added `useRebuildBudgetPeriods` + a header button (`POST /budgets/rebuild-all-periods`). Verified: recomputed a budget period from `3051.11 → 3151.11` (corrected real drift).
- **[NOT a bug — reclassified]** **"Mutations are silent" (second-sweep note D).** Audited every mutation hook + its callers: **all** flagged flows (FX add/refresh/backfill, budget create/edit/archive, goal create/update/manual-saved, category create, balance adjustment) already fire both success and error sonner toasts at the component level. The earlier observation was a Playwright capture-timing artifact — sonner toasts auto-dismiss before a snapshot grabs them (seen again this pass on the rebuild + restore toasts, both of which provably succeeded against the DB). No code change.
- **[Doc drift — fixed this pass]** Backup schema was documented as `SchemaVersion = 1` in BACKEND.md/WIKI but the live export was already **v3** (now **v4** after the fix). Docs corrected. Also: WIKI overstated several unbuilt features (tags, bulk actions, budget rollover, cursor-50 pagination, full-row transaction edit, a standalone Net-Worth report tab) — all demoted to "deferred/v2" to match the code.

#### 2026-05-29 second sweep

- **[✅ Fixed 2026-05-29 — see third sweep]** ~~Goal detail history chart emits a React duplicate-key warning~~ on a manual goal with a same-month contribution. Root cause was backend (`savedHistory` emitted two points with the same `asOf`); fixed + re-verified.
- **[✅ Fixed 2026-05-29 — see third sweep]** ~~No delete control on the main `/transactions` list.~~ `allowDelete` now passed to the main-list table; verified via UI.
- **[✅ Fixed 2026-05-29 — see third sweep]** ~~No "Rebuild periods" button surfaced on `/budgets`.~~ Button + `useRebuildBudgetPeriods` hook added (`POST /budgets/rebuild-all-periods`); verified via UI (corrected a real drift).
- **[✅ Not a bug — audited 2026-05-29, see third sweep]** ~~Mutations are silent.~~ Every flagged mutation already fires success+error sonner toasts at the component level; the earlier observation was snapshot-timing vs. auto-dismiss. No code change.
- **[Setup gap — fixed this pass]** `web/.env.local` was **missing** on the checkout, so `NEXT_PUBLIC_API_BASE_URL` was empty and every API call 404'd against the Next origin. Recreated with `http://localhost:5179`. Documented in Setup step 3 above. The file is git-ignored, so this recurs on fresh clones.
- **[Investigated — NOT bugs (test-harness artifacts)]** Two flows initially looked broken when driven with the wrong selectors but are correct when driven properly:
  - *Linked-mode goal* persists `linked_account_id` — the working selector is `[data-testid="goal-linked-account-select"]` (an earlier attempt used a non-existent `goal-account-select` and submitted with no account).
  - *Keyword-pattern add* persists a `category_patterns` row (`POST /category-patterns` 200) — you must expand the category (aria-label `Expand <name> keywords`), type into the "Add keyword" input, and click the form's **Add** submit button.

#### Prior (2026-05-29 first pass) — still valid

- **[Open · feature]** Imported balance doesn't reconcile to the statement's printed balance — the account anchor is set at creation, independent of the statement opening, so the live balance won't match the real bank balance. → motivates **reconciliation checkpoints** (v2 backlog).
- **[Open · perf]** Import preview renders all rows unvirtualized (918 rows OK, but large statements could lag).
- **[By design — now self-serviceable]** Budget created over pre-existing transactions shows `spent = 0` until "rebuild periods" (event handler only fires on new writes). As of 2026-05-29 there's a **"Rebuild periods" button on `/budgets`** so the user can correct this in-app (previously API-only).
- **[Tester note — not a bug]** `DELETE /accounts/{id}` (no `/permanent`) = archive (204). Don't confuse with the guarded hard delete `DELETE /accounts/{id}/permanent` (409 when the account has history).
