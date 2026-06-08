# Backend & Database

ASP.NET Core REST API + EF Core code-first against PostgreSQL.

Related docs:
- [WIKI.md](./WIKI.md) — product/business concepts
- [FRONTEND.md](./FRONTEND.md) — Next.js client

---

## Stack

| Concern | Choice |
|---------|--------|
| Runtime | .NET 10 (LTS) |
| API style | ASP.NET Core Minimal APIs |
| ORM | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Naming | `UseSnakeCaseNamingConvention()` — snake_case columns |
| Validation | FluentValidation |
| DI scanning + decoration | Scrutor |
| Logging | Serilog (structured, console + file sink) |
| API docs | Scalar (at `/scalar`) |
| Health checks | `AspNetCore.HealthChecks.NpgSql` |
| PDF parsing | `PdfPig` 0.1.10 (MIT, namespace `UglyToad.PdfPig`) — extracts text from bank statement PDFs |
| Testing | xUnit + FluentAssertions + NSubstitute |
| Database | PostgreSQL 16 (local) |

### What is explicitly NOT used

| Skipped | Reason |
|---------|--------|
| MediatR | Custom CQRS is simpler and teaches the pattern directly |
| JWT / Auth | Single-user, self-hosted — no auth in v1 |
| Repository pattern | EF Core's `DbSet<T>` via `IApplicationDbContext` is sufficient |
| CQRS read/write split | One Postgres instance is fine for personal use |
| AutoMapper | Manual mapping is explicit and trivial for this scale |
| SQLite | Decided on Postgres up front — no provider abstraction needed |

---

## Architecture

Clean Architecture, 5 projects. Dependencies flow inward — Domain knows nothing about anything else.

```
MoneyManagement.SharedKernel    ← no dependencies
MoneyManagement.Domain          ← depends on SharedKernel
MoneyManagement.Application     ← depends on Domain + SharedKernel
MoneyManagement.Infrastructure  ← depends on Application + Domain
MoneyManagement.Api             ← depends on all (composition root)
```

Tests mirror the src structure:
```
tests/
  MoneyManagement.Domain.Tests/
  MoneyManagement.Application.Tests/
```

### Layer Responsibilities

#### SharedKernel
Primitives with zero dependencies.

- `Entity` — base class with domain events list (`Raise`, `ClearDomainEvents`) and the `CreatedAt` / `UpdatedAt` audit fields (`internal set`, UTC; assigned by the SaveChanges interceptor — SharedKernel exposes internals to Infrastructure via `InternalsVisibleTo`)
- `Result<T>` + `Error` — explicit failure returns; no exceptions for business logic
- `IDomainEvent` — marker interface
- `IDomainEventHandler<T>` — handler interface
- `IDateTimeProvider` — abstraction over `DateTime.UtcNow` (makes time-dependent handlers and queries testable)
- **Dates are judged in UTC, end to end.** "Today" for the "not in the future" guards (`Transaction`, balance-adjustment `Date`, savings-goal contribution `occurred_on`) is `DateOnly.FromDateTime(DateTime.UtcNow)`. The frontend defaults & validates those date fields in UTC too (`new Date().toISOString().slice(0,10)`), so the two agree regardless of where the user is — the app makes no timezone assumption, which keeps it correct for any location (not just Moldova). Audit timestamps (`created_at`/`updated_at`) are stored UTC as well; the only place a date is rendered in the user's local zone is display. *(An earlier 2026-06-05 attempt judged "today" in a Europe/Chisinau reporting timezone; that was reverted in favour of UTC-everywhere so a non-Moldova user isn't pinned to Moldova time — the trade-off is that "today" flips at UTC midnight, not the user's local midnight.)*

#### Domain
Pure business logic. No EF Core, no HTTP, no infrastructure.

- Entities (built): `Account`, `Category`, `Transaction`, `ImportBatch`, `FxRate` (carries `FxRateSource = Manual | BnmAuto`), `Budget`, `BudgetPeriod`, `SavingsGoal`, `SavingsGoalContribution`. The Reports and Dashboard slices have no entities — pure projections over the above.
- No more entities planned for v1. The original brief had `RecurringTemplate`; dropped because the maib statement import already brings in every card-side recurring debit (rent, salary, subscriptions). Reconsider only if cash-only recurring obligations or forward-looking balance forecasting become a real need.
- Domain events: `AccountCreatedDomainEvent`, `TransactionCreatedDomainEvent`, `TransactionDeletedDomainEvent`, `TransactionCategoryChangedDomainEvent` (the last three are consumed by the budget event handlers — see "Domain event handlers" below).
- Static error definitions per entity: e.g. `AccountErrors.NotFound(id)`, `CategoryErrors.NameRequired`, `TransactionErrors.AmountMustBePositive`, `BudgetErrors.AlreadyExistsForCategory(id)`
- Value objects: `Money` (amount + currency)
- Strategy interfaces: `IBankStatementParser` (one implementation per bank — `MaibStatementParser` lives in Infrastructure)

#### Application
Orchestration. Defines *what* the system can do, not *how*.

- **CQRS** — custom (no MediatR):
  - `ICommand` / `ICommand<TResponse>` / `ICommandHandler<TCommand, TResponse>`
  - `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>`
- **Behaviors (Decorators via Scrutor)**:
  - `ValidationDecorator` — runs FluentValidation before the handler
  - `LoggingDecorator` — logs command/query name + result
- **FluentValidation** — one `AbstractValidator<TCommand>` per command
- `IApplicationDbContext` — EF Core interface, only Application touches it (no raw repos)
- `IDomainEventsDispatcher` — interface dispatched after `SaveChanges`

#### Infrastructure
Implements interfaces defined in Application. All EF Core + Postgres lives here.

- `ApplicationDbContext` : `DbContext`, `IApplicationDbContext`
- EF Core entity configurations (one `IEntityTypeConfiguration<T>` per entity)
- `.UseNpgsql()` + `.UseSnakeCaseNamingConvention()`
- `DomainEventsDispatcher` — dispatches domain events after `SaveChanges`
- `DateTimeProvider` — wraps `DateTime.UtcNow`
- Migrations live here (`Database/Migrations/`)

#### Api
Composition root only. No business logic.

- `Program.cs` — DI wiring, middleware, Serilog, CORS (`http://localhost:3000` in dev), JSON options (`JsonStringEnumConverter` so enums round-trip as names, e.g. `"Cash"`)
- Minimal API endpoints via `IEndpoint` interface — one file per feature (e.g. `TransactionEndpoints.cs`)
- `GlobalExceptionHandler` — catches unhandled exceptions, returns Problem Details (RFC 7807)
- `ResultExtensions` — maps `Result<T>` to HTTP responses (`200`, `404`, `422`, etc.)
- `MigrationExtensions.ApplyMigrations()` — runs on dev startup; creates the DB if missing and applies any pending migrations
- Scalar UI at `/scalar` (browser launches there in dev via `launchSettings.json`)

---

## Key Patterns

### Result<T> instead of exceptions

```csharp
public async Task<Result<AccountDto>> Handle(GetAccountQuery query, CancellationToken ct)
{
    var account = await db.Accounts.FindAsync([query.Id], ct);
    if (account is null)
        return Result.Failure<AccountDto>(AccountErrors.NotFound(query.Id));
    return Result.Success(account.ToDto());
}
```

### Domain Events

Raised inside the entity, dispatched by infrastructure after `SaveChanges`.

```csharp
// Inside Transaction entity
public static Transaction Create(...) {
    var tx = new Transaction(...);
    tx.Raise(new TransactionCreatedDomainEvent(tx.Id));
    return tx;
}

// Handler reacts — e.g. check if budget is now exceeded
public class UpdateBudgetOnTransactionCreated
    : IDomainEventHandler<TransactionCreatedDomainEvent> { ... }
```

### Entity IDs — UUIDv7

All entities use `Guid` primary keys, but generated via `Guid.CreateVersion7()` (the IETF-blessed RFC 9562 v7 UUID, built into .NET 9+) rather than `Guid.NewGuid()` (v4, random).

Why: v7 puts a 48-bit Unix-ms timestamp in the high bits, so the bytes sort by creation time. Postgres compares `uuid` columns byte-wise, which means our primary-key B-tree gets near-sequential inserts — no page-split fragmentation as the row count grows, and `ORDER BY id` ≈ `ORDER BY created_at` for cheap.

Trade-off vs auto-increment `int`: 12 extra bytes per row (negligible at this app's scale), in exchange for client-side generation (factory can set `Id` before `SaveChanges`, so `Raise(new XCreatedEvent(this.Id))` still works), and stable deterministic IDs for seed data (e.g. the 9 seeded categories with `00000000-0000-0000-0000-00000000000{1..9}`).

```csharp
// Inside Account.Create(...)
var account = new Account(Guid.CreateVersion7(), ...);
account.Raise(new AccountCreatedDomainEvent(account.Id));
```

### Custom CQRS (no MediatR)

```csharp
public interface ICommandHandler<TCommand, TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}
```

Resolved by Scrutor; decorated by `ValidationDecorator` and `LoggingDecorator`.

### Minimal API Endpoints

```csharp
public class TransactionEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/transactions", GetAll);
        app.MapPost("/transactions", Create);
    }
}
```

`Program.cs` scans assembly for `IEndpoint` and calls `MapEndpoints` on each.

---

## Data Model

EF Core code-first. The `CreatedAt` / `UpdatedAt` audit fields live on the base `Entity` and are set automatically in `SaveChangesAsync` by `AuditableEntitySaveChangesInterceptor`, which iterates `ChangeTracker.Entries<Entity>()` polymorphically (UTC via `IDateTimeProvider`) — so every entity gets `created_at` + `updated_at` columns and values uniformly.

### Built

```
Account
  id, name, type (one of Cash | CreditCard | BankCurrent | BankDeposit |
                  Brokerage | CryptoExchange | P2PLending, stored as string),
  balance_amount, balance_currency (3-letter ISO; MDL/USD/EUR/RON/...) — the anchor set at account creation; the live balance is computed on read as anchor + Σ(income) − Σ(expense) across ALL non-deleted transactions (Bank Fees rows included — they correspond to fee-only ledger entries that pair with a reduced principal row, so summing them all reproduces maib's actual per-row debit and matches "Sold Disponibil"),
  opening_date, is_archived, notes,
  created_at, updated_at

Category
  id, name, parent_id (nullable, self-ref FK),
  flow (Expense|Income|Both, stored as string),
  color (nullable, "#RRGGBB"), icon (nullable),
  is_archived, created_at, updated_at

Transaction
  id, account_id (FK), category_id (nullable FK),
  transaction_date,
  direction (Income|Expense, stored as string),
  amount_value, amount_currency (Money complex property; currency must equal the parent account's currency — multi-currency since Phase 4),
  description (max 500),
  notes (nullable, max 500) — optional free-text user annotation, distinct from description (the bank memo / label); editable on any existing row via PUT /transactions/{id}/notes,
  original_amount (nullable, decimal), original_currency (nullable, 3 chars) — for FX rows (e.g. USD purchases imported from a maib statement),
  source (Manual|Imported, stored as string),
  import_batch_id (nullable FK, for traceability),
  is_transfer (bool, Phase 3 — Income/Expense aggregates filter this out),
  counter_account_id (nullable FK to accounts, ON DELETE SET NULL — present only when a transfer's other leg is known),
  is_adjustment (bool, Phase 4 — monthly balance true-ups on Brokerage/Crypto/P2P/Deposit accounts; mutually exclusive with is_transfer; aggregates filter it out),
  is_deleted (bool, soft delete via HasQueryFilter),
  created_at, updated_at

ImportBatch
  id, account_id (FK), file_name, file_hash (sha256 hex),
  bank_source (Maib, stored as string),
  imported_at (UTC), imported_count, skipped_duplicate_count,
  created_at, updated_at

FxRate
  id, from_currency (3-char ISO), to_currency (3-char ISO),
  rate (numeric(18,6)), as_of (date),
  source (Manual|BnmAuto, stored as string; Manual is the back-compat default),
  created_at, updated_at
  -- unique index on (from_currency, to_currency, as_of, source)
  --   a Manual row may sit alongside a BnmAuto row for the same (from, to, as_of) triple;
  --   the converter prefers Manual on collision

Budget
  id, category_id (FK),
  monthly_limit_amount, monthly_limit_currency (Money complex property; MDL-only in v1),
  is_archived (bool, default false; default filter via HasQueryFilter),
  created_at, updated_at
  -- filtered unique index `ix_budgets_category_id_active` on (category_id) WHERE is_archived = false
  --   enforces "at most one active budget per category" at the DB level

BudgetPeriod
  id, budget_id (FK),
  year (int), month (int 1..12),
  spent_amount, spent_currency (Money complex property; MDL),
  created_at, updated_at
  -- composite unique index `ix_budget_periods_budget_id_year_month` on (budget_id, year, month)

SavingsGoal
  id, name (max 100),
  target_amount_value, target_amount_currency (Money complex property; MDL-only in v1),
  target_date (date, nullable),
  linked_account_id (uuid, nullable, FK to accounts ON DELETE RESTRICT) — set in linked-account mode,
  manual_saved_amount_value (numeric(18,2), nullable),
  manual_saved_amount_currency (varchar(3), nullable) — paired Money? as two scalar columns; both populated in manual mode, both NULL in linked mode,
  is_archived (bool, default false; default filter via HasQueryFilter),
  created_at, updated_at
  -- FK index on linked_account_id (`ix_savings_goals_linked_account_id`)
```

Indexes: `ix_categories_name`, `ix_categories_parent_id`, `ix_transactions_account_id_transaction_date`, `ix_import_batches_account_id_file_hash`, `ix_budgets_category_id_active`, `ix_budget_periods_budget_id_year_month`, `ix_savings_goals_linked_account_id`.

### FX Conversion

`IFxConverter` is the only abstraction that touches FX. Defined in `MoneyManagement.Application.Abstractions.FxRates`, implemented by `EfFxConverter` in Infrastructure.

```csharp
Task<decimal?> ConvertAsync(
    decimal amount, string fromCurrency, string toCurrency,
    DateOnly asOf, CancellationToken ct);
```

Lookup order:
1. Identity case (`from == to`) → return `amount` immediately.
2. Direct lookup: most recent `FxRate` with `from_currency = from && to_currency = to && as_of <= asOf`.
3. Inverse lookup: most recent `FxRate` with `from_currency = to && to_currency = from && as_of <= asOf`, return `amount / rate`.
4. Nothing usable → `null`.

`GetAccountsQueryHandler` runs a single grouped query against `Transactions` (per-account income/expense totals), loads the full `FxRates` table once, and returns the live `Balance` (= anchor + Σ income − Σ expense) and `BalanceMdl` (converted at today's rate) for each account. The DTO no longer exposes the anchor separately — the user-facing concept is a single `Balance`. `MDL` is constant in `ReportingCurrencies.Mdl`; ISO format check is `CurrencyCodes.IsValidIso(string)`, shared between `Account.Create` and `FxRate.Create`.

`GET /accounts/{id:guid}` → `AccountDetailDto` is the per-account detail endpoint feeding the frontend "Performance" card on `/accounts/{id}`. `GetAccountDetailQueryHandler` loads the account with `IgnoreQueryFilters()` (so archived accounts are still drillable), then loads every non-deleted row for that account in one query, and bucketing them per the canonical transfer/adjustment rules: `IsTransfer && Income` → contribution (inbound transfer leg), `IsTransfer && Expense` → withdrawal, `IsAdjustment` rows → `NetPnLMdl` signed by direction, everything else → `RealActivityCount`. The bucket sums are MDL-converted **per row date** via `IFxConverter` (mirrors `GetSummaryQueryHandler` — not at "now"). Two `AccountActivityTotalsDto`s are emitted: `AllTime` since inception and `YearToDate` (`[Jan 1 of current UTC year, today]` inclusive). `InitialCapital` is the opening anchor (`Account.Balance.Amount`). `Balance` and `BalanceMdl` mirror the list-endpoint arithmetic. Native bucket totals are intentionally NOT exposed — the card is multi-currency-aware and always speaks MDL; for MDL accounts the totals equal the native sums by FX identity. A row whose currency lacks a usable rate at its `TransactionDate` flips `MissingFxRate = true` on its bucket and is omitted from the sum — same null-or-flag contract as `BalanceMdl` and `GetSummaryQueryHandler`. Unknown ID → `AccountErrors.NotFound` (404).

`PUT /accounts/{id}` edits the account's user-mutable metadata (`UpdateAccountCommand(Id, Name, Notes)` → `Account.Update(name, notes)`): renames the account and edits its notes. Name + notes only — currency, type, balance, and opening date are immutable after creation (changing currency/balance would corrupt denominated history; type/opening-date would distort reports). Loads by id (404 → `AccountErrors.NotFound`), validates name (NotEmpty, ≤100) and notes (≤1000) via `UpdateAccountCommandValidator`, returns 204 / 400 / 404. No domain event is raised — name/notes feed no aggregate or report computation — and no EF migration (no schema change).

`DELETE /accounts/{id}` soft-archives (`ArchiveAccountCommand` → `Account.Archive()`); `POST /accounts/{id}/unarchive` reverses it (`UnarchiveAccountCommand` → `Account.Unarchive()`). Both load by id (404 → `AccountErrors.NotFound`), are idempotent, and return 204. Unarchive is a boolean flip on the existing `is_archived` column — no EF migration. The list endpoint surfaces archived rows only when `includeArchived=true`.

`DELETE /accounts/{id}/permanent` is a **guarded hard delete** (`DeleteAccountCommand`). It loads with `IgnoreQueryFilters()` (archived accounts are deletable), then refuses with `AccountErrors.HasLinkedRecords` → **409 Conflict** if the account has any transaction (as `AccountId` *or* `CounterAccountId`, counting soft-deleted rows via `IgnoreQueryFilters`), any import batch, or any linked savings goal (counting archived goals — the `savings_goals.linked_account_id` FK is `ON DELETE RESTRICT`, so the pre-check turns a would-be DB exception into a friendly Conflict). Only a truly empty account is removed (`db.Accounts.Remove`, 204). Use archive for accounts with history.

No FX rates are seeded — the `fx_rates` table starts empty. Rates come from manual entry via `POST /fx-rates` (always `Manual`) or the BNM auto-fetch / backfill (`BnmAuto`); `DELETE /fx-rates/{id}` removes any row regardless of source.

`GET /fx-rates/convert?from=&to=&date=&amount=` → `ConvertFxResult { convertedAmount, rate, hasRate }` is a thin read-side wrapper over `IFxConverter` (`ConvertFxQuery`/handler). Identity (`from==to`) returns the amount with `rate=1`; no usable rate returns `hasRate=false` with null amount/rate; invalid ISO codes 400 (`fx.invalid_currency`). The frontend uses it to pre-fill the editable destination/counter amount on cross-currency transfers.

#### BNM auto-fetch

`FxRate.Source` is `Manual | BnmAuto`. The DB unique index is `(from_currency, to_currency, as_of, source)` so a hand-entered Manual row may sit alongside a scheduled BnmAuto row for the same triple. `EfFxConverter`'s LINQ ordering is `OrderByDescending(AsOf).ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)`, which EF renders as a `CASE` expression in the `ORDER BY` that puts Manual first. This is the "manual rates always win" guarantee. **Pitfall — do not order by `r.Source` directly:** `FxRate.Source` is mapped with `HasConversion<string>()`, so `ThenBy(r => r.Source)` translates to `ORDER BY source` on the *string* column, where `'BnmAuto' < 'Manual'` lexicographically — that returns BnmAuto first and silently breaks the guarantee in production (the CLR-side `Manual = 0 < BnmAuto = 1` reasoning only holds for an int enum, which an in-memory provider would honor but Npgsql does not). The Infrastructure integration test `Convert_OnSameTriple_PrefersManualRate` pins this against the real provider.

- **Feed**: `https://www.bnm.md/en/official_exchange_rates?get_xml=1&date=DD.MM.YYYY` (note the `DD.MM.YYYY` format, not ISO). Returns a `<ValCurs Date="...">` document with one `<Valute>` element per currency carrying `<CharCode>`, `<Nominal>`, `<Value>`. The effective per-unit rate is `Value / Nominal` (so JPY's `100 / 11.3120` becomes `0.113120` MDL per yen). MDL itself is filtered out defensively.
- **Failure modes collapse to "empty"**: weekends/holidays, future dates pre-publication (BNM publishes around 14:00 Moldova time), HTTP 4xx/5xx, malformed XML, and network failures all return an empty list rather than throwing. The caller treats absence as "nothing to update".
- **Hosted service**: `BnmAutoFetchService` (a `BackgroundService`) backfills the last `BackfillDays` days on startup (default 30, today included), one date at a time so each day's counts land in the log, then loops on `Task.Delay(RefreshIntervalHours)` re-fetching today. Resolves a fresh scope per dispatch so the scoped `DbContext` doesn't leak. Honours `OperationCanceledException` cleanly on shutdown.
- **Refresh command**: `RefreshBnmRatesCommand(Date?, CurrencyFilter?)` is dispatched by both the hosted service and the manual `POST /fx-rates/refresh` endpoint. `Date` defaults to today UTC; `CurrencyFilter` defaults to the distinct non-MDL currencies on `Accounts.Currency`. For each fetched rate matching the filter the handler resolves one of: skip (Manual row exists for that triple — Manual wins even if the values differ), skip (BnmAuto row exists with the same value), update (BnmAuto row exists with a different value — `FxRate.UpdateRate`), or insert (no row yet — `FxRate.Create(..., FxRateSource.BnmAuto)`). One `SaveChangesAsync` at the end. Counts surface to callers as `RefreshBnmRatesResponse(Fetched, Inserted, Updated, Skipped)`.
- **Backfill command**: `BackfillBnmRatesCommand(From, To?)` → `POST /fx-rates/backfill`. Replays the single-date refresh over the inclusive `[From, To]` range (`To` defaults to / is clamped to today UTC), aggregating into `BackfillBnmRatesResponse(DaysProcessed, Fetched, Inserted, Updated, Skipped)`. Guards (in the handler, since they need the clock): `From` not in the future, `To >= From`, span ≤ ~800 days. Reuses `RefreshBnmRatesCommandHandler` per day so Manual-wins/dedup/currency-from-accounts all carry over; idempotent (already-covered days just skip). Synchronous — a wide range can take a minute. This is the path for filling history older than the startup `BackfillDays` window (e.g. accounts opened before the auto-fetch began).
- **Configuration**: bound from the `Fx:AutoFetch` section of `appsettings.json` into `FxAutoFetchOptions { Enabled, BackfillDays, RefreshIntervalHours, BnmBaseUrl }`. Disabled wholesale by setting `Enabled = false` — the hosted service exits in `ExecuteAsync` before any HTTP call. Tests skip the real HTTP client entirely: `RefreshBnmRatesCommandHandlerTests` substitutes `IBnmRateProvider`, and the XML parsing logic is exercised directly via `BnmRateProvider.Parse(string)` (a static method — contract deviation: there's no Infrastructure test project per the v1 layout, so the parser is tested at the Application boundary). The hosted service itself is not unit-tested; manual verification on `dotnet run` covers it.
- **No seed rates**: the `fx_rates` table starts empty; the BNM service populates BnmAuto rows for every held currency on first boot. A Manual rate for a given (from, MDL, asOf) triple always wins over BnmAuto. Deleting a BnmAuto row via `DELETE /fx-rates/{id}` works but the next scheduled refresh will recreate it — documented, not a bug.
- **HttpClient**: registered as a typed-named client via `services.AddHttpClient<IBnmRateProvider, BnmRateProvider>` with a 10-second timeout. The BNM URL comes from `FxAutoFetchOptions.BnmBaseUrl`.

### Soft Delete

`Transaction.is_deleted` is hidden from all queries via an EF Core global query filter:

```csharp
modelBuilder.Entity<Transaction>().HasQueryFilter(t => !t.IsDeleted);
```

This means `db.Transactions.Find(id)` will not return deleted rows. To include them (e.g. for an "undo" flow), use `.IgnoreQueryFilters()`.

Other entities use hard delete — they're not financial events, and cascading deletes are cleaner.

### PDF Statement Import (maib)

The user uploads a bank-issued PDF statement; the server parses it, suggests categories, flags duplicates, returns a preview. The user reviews and confirms, then the server bulk-inserts.

**Endpoints**
- `POST /imports/parse` (multipart: `file` PDF ≤5MB, `accountId`) → `StatementPreviewDto` { fileHash, statementPeriod, bankSource, summary, transactions[] }
- `POST /imports/commit` (JSON: accountId, fileName, fileHash, bankSource, transactions[]) → `CommitResultDto` { importBatchId, importedCount, skippedDuplicates }. Each `transactions[]` item may carry an optional `notes` (≤500, `Transaction.NotesMaxLength`) persisted on the source-row transaction **and mirrored onto the paired counter leg**, so the same annotation is visible from both accounts. For a transfer row whose `counterAccountId` is in a **different currency** than the import account, the item MUST also carry `counterAmount` (the destination-native amount); the endpoint forwards it through `CommitTransactionRequest` → `TransactionToImport.CounterAmount`. *(Regression note: `CounterAmount` was previously missing from `CommitTransactionRequest`, so it was silently dropped at the API boundary and every cross-currency import failed `counter_amount_required` — fixed 2026-06-01, see QA.md row 27.)*

**Parser strategy** — `IBankStatementParser` interface, one impl per bank. Currently only `MaibStatementParser`:
- Uses PdfPig with `NearestNeighbourWordExtractor` to recover word boundaries from glyph positions, then joins words with a single space. `page.Text` alone is unreliable — PdfPig's default extractor sometimes glues adjacent runs together (e.g. `Transfer RetragereCashback` for "Transfer Retragere Cashback").
- Regex-anchored scan locates adjacent date pairs (`YYYY-MM-DD YYYY-MM-DD`) as row anchors; each segment is parsed with a row-body regex into description, source-currency signed amount + currency code, and tail numeric tokens.
- Tail layout in the extracted text is `<abs-mdl-amount> <running-balance>` (two tokens). The bank's table headers `ieșiri/intrări/comision` columns are not emitted by PdfPig for empty cells, so the populated amount is always the first tail token regardless of direction.
- Pending section (`Tranzacții în procesare`) is sliced off before scanning so pending rows can never be emitted.
- Returns `Result<ParsedStatement>` with `UnsupportedFormat` error if the document doesn't look like a maib statement (bank code `AGRNMD2X` is the sniff, performed on PdfPig-extracted text — raw-byte sniffing doesn't work because PDF content streams are FlateDecode-compressed).
- Unparseable rows are skipped silently; whole-document failure only on no-rows-found.

**Parser selection** — `ParseStatementCommandHandler` hands the PDF to each registered `IBankStatementParser` in turn. A parser that doesn't recognise the file returns `UnsupportedFormat` and the handler tries the next; any other error short-circuits and propagates. With only `MaibStatementParser` today, this is a single attempt.

**Preview ordering** — maib groups statement rows by card/section, not chronologically, and the parser faithfully preserves that PDF order. The handler sorts the preview rows by `TransactionDate` **ascending** (stable `OrderBy`, so same-date rows keep their PDF/section order) before returning the DTO. Sorting lives in the handler, not the parser, so the parser stays a faithful reader; dedup, the transfer-leg fallback, and the summary all run before the sort and are order-independent. The committed order follows, since the frontend submits rows in the order received.

**Auto-categorization** — `ICategorySuggester` reads keyword→category rules from the **`category_patterns`** table (seeded from the original rule set by the `CategoryPatternSeeder` hosted service), matches them case-insensitively against the description (**earliest occurrence in the description wins**, so a leading transaction-type token like `ATM …` beats an embedded merchant name like `LINELLA`; ties at the same index break by longest keyword), and returns a `CategoryId`. Patterns are managed via `GET /category-patterns`, `POST /category-patterns` (`{ keyword, categoryId }` → 409 on duplicate keyword; stored upper-cased, source `Learned`), `PUT /category-patterns/{id}`, `DELETE /category-patterns/{id}`. **Learn-with-confirm:** `POST /imports/commit` accepts an optional `learnedPatterns` (`{ keyword, categoryId }[]`) — rules the user confirmed in the import preview — upserted as `Learned` patterns best-effort inside the import transaction (blank / already-existing keyword / unknown category are skipped; learning never fails the import). They take effect on the next import. Examples: `LINELLA` → Groceries, `MCDONALD` → Restaurants, `APPLE.COM`/`CLAUDE.AI` → Subscriptions, `A2A DE INTRARE`/`A2A DE IESIRE` → Transfers, `ATM` → **Withdrawal** (so `ATM MAIB LINELLA MOSILOR` is a withdrawal, not groceries). Earliest-match wins; no match → null (user picks during preview).

**Duplicate detection** — for each parsed row, compute a signature `sha256(accountId|transactionDate|amount|normalizedDescription)` (normalize = trim, collapse whitespace, lowercase). Server queries existing transactions in the statement's period window, recomputes their signatures, and marks `isDuplicate=true` on matches. The frontend defaults those rows to *excluded*. On `commit`, the server re-checks (the preview is not trusted) and skips actual duplicates, surfacing `skippedDuplicates` in the response.

**Within-batch identical rows are NOT auto-deduped.** Real bank statements legitimately contain multiple rows with identical `(date, amount, description)` — e.g. two ATM withdrawals at the same cashpoint on the same day for the same amount, or two `A2A …5875 -5,000` transfers on the same date. The dedup `HashSet` is a *snapshot* of existing DB signatures, taken once before the row loop, and checked via `Contains` only — never mutated during the loop. So if the DB has zero matching rows and the batch contains 2 identical rows, both are persisted. (An earlier implementation used `HashSet.Add` for dedup, which silently dropped the second copy as a "duplicate" and lost ~23K MDL of real expenses on a one-year statement. Don't reintroduce.)

**Transfer-aware fallback** — descriptions differ across the two ends of an A2A transfer (source-side reads "A2A de iesire pe cardul X", destination-side reads "A2A de intrare pe cardul Y"), so the description-based signature misses cross-statement matches. For rows the parser flagged as transfers (`ITransferDetector.IsLikelyTransfer == true`), the dedup falls back to a description-agnostic check: existing transaction on the same account with the same `transaction_date`, same `amount`, same `direction`, and `is_transfer = true` → flag as duplicate. This catches the case where the destination's statement is imported after the source's: the source-side leg already exists on the destination account (e.g. paired-leg leftovers from the now-removed counter-account feature, or simply imported earlier from the source's PDF), and the destination's PDF row matches by `(date, amount, direction)` despite the different wording.

**FX rows** — maib reports the transaction-currency amount alongside the MDL value (e.g. `-120.00 USD 2 091.20`, `-145.00 EUR 2 883.91`, `-1 175.00 TRY 507.64`, `-200.00 RON 789.22`). The parser's row-body regex accepts any 3-letter uppercase ISO code (`[A-Z]{3}`), not a fixed list — earlier versions only matched `MDL|USD` and silently dropped every other-currency row (an entire Turkey/Italy/Romania trip's worth of card purchases). We store the **MDL value** in `amount` (since the account currency is MDL — the bank pre-converted it), and the original source amount + source currency in `original_amount` / `original_currency` for traceability.

**Category seeding** — `CategorySeeder` (an `IHostedService`) backfills missing seeded categories on every startup (it skips ids that already exist, so appending new entries doesn't require dropping the table). Default set with deterministic GUIDs: Groceries (`…001`), Restaurants (`…002`), Transport (`…003`), Subscriptions (`…004`), Shopping (`…005`), Bills (`…006`), Home (`…011`, home-expenses bucket), Salary (`…007`), Transfers (`…008`), Other (`…009`), Balance Adjustment (`…00a`, used by Phase 4 balance true-ups), Credit Payment (`…00b`), Cashback (`…00c`), Bank Fees (`…00d`). The stable ids keep the auto-categorization rules' target IDs consistent across environments.

**Commission/fee rows** — when a maib row has both `ieșiri` and `comision` columns populated (tail has 3 tokens: `amount commission balance`), the `comision` value is the *fee portion* of the `ieșiri` amount, NOT an additional debit. Bank's per-row running balance subtracts only `ieșiri` (already includes the fee). The parser splits the row into TWO `ParsedStatementRow`s so principal and fee are tracked separately while still summing to the bank's actual deduction:

  - **Primary row**: amount = `ieșiri − comision` (the actual transfer amount; e.g. 990 when `ieșiri = 1,000, comision = 10`).
  - **Fee row**: description = `"Comision: {original description}"`, amount = `comision`, category `Bank Fees` (id `…00d`) via the suggester's `COMISION` keyword.

  Sum of the two equals the original `ieșiri`, so the live balance computation (no special filter) reconciles against `Sold Disponibil`. Fees are ALWAYS `is_transfer = false` (real spending, never net-zero) — the parser produces no transfer flag, and `SubstringTransferDetector`'s inclusion list doesn't include `COMISION`. Edge case: if `comision == ieșiri` (principal would be 0), the parser emits only the fee row.

  *Closing balance is COMPUTED, not read:* `ParsedStatementSummary.ClosingBalance = opening + intrări − ieșiri` — the booked balance, exactly what the imported account reconciles to (Σ income − Σ expense rows, fees included). We deliberately do NOT read maib's printed end-balances, because neither is reliable across statements: `Sold final` double-subtracts the commission (`anchor + intrări − ieșiri − comision`, even though `ieșiri` already includes the fee), and `Sold Disponibil` nets out pending authorization holds (so on a statement with a pending hold it's *lower* than the booked balance). The `opening + intrări − ieșiri` identity always matches what the import produces. `ParsedStatementSummary.TotalFees` carries maib's separate `Total comision` total, surfaced in the preview for display only (never subtracted from the balance).

### Dashboard slice

Two read-only endpoints feeding the dashboard widgets. Both are pure projections over `Transaction` + `Account` — no schema, no validators (queries don't run through the `ValidationDecorator`, so any input validation lives inline in the handler).

- `GET /dashboard/summary?month=YYYY-MM` → `DashboardSummaryDto { month, income, expense, net, savingsRate, transactionCount, missingFxRate }`. Window is `[firstDay, firstDayOfNextMonth)` on the transaction date. Filters out `IsDeleted`, `IsTransfer`, `IsAdjustment` — this is the slice's whole reason to exist, see the rough-edges note in `WIKI.md`. Each row is FX-converted via `IFxConverter` at its own transaction date (mirrors `TransactionDto.AmountMdl`); unconvertible rows are omitted from the totals and flip `missingFxRate = true`. `savingsRate = net / income` (or `0` when income is `0`). `month` defaults to the current UTC month (via `IDateTimeProvider`).
- `GET /dashboard/net-worth-trend?months=N` (default 6, range `[1, 24]`) → `NetWorthTrendPointDto[]` oldest first. Each point's as-of date is the last day of that calendar month (UTC), except the last point which is "today" so the live current-month value is real-time, not month-end-projected. For each as-of date, every non-archived account's native balance is computed exactly like `GetAccountsQueryHandler` (anchor + Σ income − Σ expense across **all** non-deleted rows dated ≤ asOf), FX-converted to MDL at the same asOf, and summed. Accounts whose balance can't be converted at that asOf are omitted from the point's sum and flip `missingFxRate = true` for that point only — mirrors the `AccountDto.BalanceMdl` null semantics. Out-of-range `months` returns `Result.Failure` with `DashboardErrors.MonthsOutOfRange` (400).

Both handlers explicitly write `Where(t => !t.IsDeleted)` in addition to relying on the EF Core `HasQueryFilter`, so unit tests against the in-memory `FakeApplicationDbContext` (which bypasses model configuration) exercise the same predicate the production query relies on.

`DashboardErrors` lives in `Application/Features/Dashboard` rather than `Domain/<Entity>` because Dashboard has no entity — the convention for entity-less features is to colocate error codes with the slice.

### Reports slice

Five read-only endpoints projecting over `Transaction` + `Account` + `Category`. Filter discipline mirrors Dashboard: every income/expense aggregate filters `!IsDeleted && !IsTransfer && !IsAdjustment`. The lone exception is `GetBalanceOverTime`, which intentionally includes transfers and adjustments because those rows DO move the per-account native balance — flagged with an inline comment and pinned by a unit test.

- `GET /reports/monthly-summary?from=YYYY-MM&to=YYYY-MM` → `IReadOnlyList<MonthlySummaryPointDto>` oldest-first; each point is the same shape as `DashboardSummaryDto` (`month, income, expense, net, savingsRate, transactionCount, missingFxRate`). When both params omitted, returns the trailing 12 months. Span clamped to ≤24 months via `ReportsErrors.RangeOutOfBounds`. Per-row FX conversion at the row's transaction date (mirrors `TransactionDto.AmountMdl`). Zero-activity months still appear as `0/0/0` points so the chart doesn't gap-out.
- `GET /reports/category-breakdown?from=YYYY-MM-DD&to=YYYY-MM-DD&direction=Expense|Income` → `CategoryBreakdownDto { from, to, direction, totalMdl, missingFxRate, items[] }`. `items` are `{ categoryId (Guid?), categoryName, amountMdl, percentage, transactionCount }` sorted by `amountMdl` desc. Uncategorized rows go into a single bucket with `categoryId = null` and `categoryName = "Uncategorized"`. Internally the handler uses `Guid.Empty` as a sentinel for the dictionary key (non-nullable under nullable refs) and maps back to `null` on the way out.
- `GET /reports/balance-over-time?accountId=<guid>&from=YYYY-MM-DD&to=YYYY-MM-DD&interval=Daily|Weekly|Monthly` → `IReadOnlyList<BalancePointDto>` oldest-first; each point is `{ asOf, balance (account's native currency), balanceMdl (nullable), missingFxRate }`. `Monthly` walks month-ends within the range and clamps the final point to `to`; `Weekly` strides 7 days from `from` with the same `to` clamp; `Daily` is one point per day. Daily span > ~3 years fails with `ReportsErrors.IntervalTooFine`. **Does NOT exclude transfers/adjustments** — per-account balance arithmetic requires every non-deleted row that touches the account, same as `GetAccountsQueryHandler.GetCurrentBalance`. Archived account → `AccountErrors.NotFound`.
- `GET /reports/top-payees?from=YYYY-MM-DD&to=YYYY-MM-DD&direction=Expense|Income&limit=10` → `IReadOnlyList<TopPayeeDto>` sorted by `amountMdl` desc. `payee` is the normalized description (`trim().ToLowerInvariant()`); `originalDescription` carries the first-occurrence raw casing for display. `Transaction.Create` already trims descriptions at write time, so the normalizer only needs to handle casing. `limit` clamped to [1, 50].
- `GET /reports/transactions.csv?<same filters as /transactions>` → streaming `text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="transactions_YYYY-MM-DD.csv"`. Columns (snake_case so consumers can use the row as-is): `transaction_date,account,category,direction,amount,currency,amount_mdl,description,is_transfer,is_adjustment`. RFC 4180 quoting via `CsvWriter.EscapeField` — fields containing `,`, `"`, or `\n` are wrapped in `"` with internal `"` doubled. UTF-8 without BOM. Account/category names resolved via single batched join (no N+1). The endpoint writes header + rows directly to `Response.Body` through a `StreamWriter` — true streaming, no intermediate buffer. The endpoint does NOT auto-apply the transfer/adjustment filter; callers control inclusion via `isTransfer`/`isAdjustment` query params.

`ReportsErrors` lives in `Application/Features/Reports` (same convention as `DashboardErrors` — entity-less features colocate errors with the slice). Errors: `RangeOutOfBounds(detail)`, `IntervalTooFine(detail)`, `DirectionRequired`.

`TransactionDirection` and `BalanceInterval` are bound from query strings via ASP.NET's enum binder. `DateOnly` query params bind directly — no string parsing needed except for the YYYY-MM month form (which goes through `TryParseOptionalMonth` in `ReportsEndpoints`).

### Budget slice

Per-category monthly spending limit with running spend, updated reactively as transactions land. Two domain entities (`Budget`, `BudgetPeriod`), four endpoints, one domain-event handler.

- `Budget` is the user-facing entity (CategoryId + MonthlyLimit + IsArchived). MDL-only in v1; the factory rejects any other currency with `BudgetErrors.MdlOnly`. At most one active (`IsArchived = false`) budget per category — enforced both in `CreateBudgetCommandHandler` with a pre-check (`BudgetErrors.AlreadyExistsForCategory(id)`) and at the DB level via the filtered unique index `ix_budgets_category_id_active`. The HasQueryFilter on Budget hides archived rows from default queries; the archive handler uses `IgnoreQueryFilters()` so the same id can be re-archived idempotently.
- `BudgetPeriod` is the per-month running-spend row, created on demand by the domain-event handler the first time an expense in that month hits the budget's category. Composite uniqueness on `(BudgetId, Year, Month)` via `ix_budget_periods_budget_id_year_month`.

**Endpoints**

- `POST /budgets` → `CreateBudgetCommand(CategoryId, MonthlyLimit)` → `{ id }`. Validates the category exists, that no active budget already exists for it, and that the limit is positive.
- `GET /budgets?year=&month=` → `BudgetDto[]`. Year and month default to the current UTC month (via `IDateTimeProvider`). The DTO carries `MonthlyLimit`, `Spent` (0 when no `BudgetPeriod` row exists for the month), `Remaining` (can be negative), and a `Status` enum bucketed `OnTrack < 80% < Warning <= 100% < Over` — thresholds match the dashboard color story. Archived budgets are excluded via both the EF query filter and an explicit `!IsArchived` predicate in the handler (defense in depth — mirrors `GetSummaryQueryHandler`).
- `PUT /budgets/{id}` → `UpdateBudgetLimitCommand(Id, MonthlyLimit)`. Same validation as create.
- `DELETE /budgets/{id}` → `ArchiveBudgetCommand(Id)`. Idempotent: archiving an already-archived budget is a no-op success.
- `POST /budgets/{id}/rebuild-periods` and `POST /budgets/rebuild-all-periods` → `RebuildBudgetPeriodsCommand(BudgetId?)`. Returns `200 { budgetsRebuilt, periodsAffected }`. See "RebuildBudgetPeriods slice" below.

**Inverse updates (2026-05-27)** — soft-delete and recategorize now feed the rollup symmetrically. `Transaction.MarkDeleted(amountMdl?)` raises `TransactionDeletedDomainEvent`; `Transaction.SetCategory(categoryId, amountMdl?)` raises `TransactionCategoryChangedDomainEvent` (idempotent — old == new is a no-op and emits nothing). Two new handlers under `Application/Features/Budgets/EventHandlers` consume them:

- `UpdateBudgetPeriodOnTransactionDeletedHandler` — same `ShouldSkip` predicate as the create handler (uncategorized / transfer / adjustment / income / null mdl). When a budget and period both exist, calls `period.SubtractSpend(amountMdl)`; missing period or missing budget returns silently.
- `UpdateBudgetPeriodOnTransactionCategoryChangedHandler` — applies the OLD-side subtract and the NEW-side find-or-create-and-add in one pass (single `Budgets.Where(b => b.CategoryId == old || b.CategoryId == new)` query), with a single `SaveChangesAsync` at the end. Same shared skip rules (transfer / adjustment / income / null mdl).

The new domain method `BudgetPeriod.SubtractSpend(amountMdl)` rejects non-positive input with the existing `BudgetPeriodErrors.SpendMustBePositive`, and **clamps at zero** if the subtraction would go negative — FX drift between create-time and delete-time (the converter resolves the most recent rate ≤ asOf, and that rate can shift in either direction) would otherwise produce nonsensical negative spend. `RebuildBudgetPeriods` is the canonical correction path for any accumulated drift.

To make the inverse-update event payload meaningful, `DeleteTransactionCommandHandler` and `UpdateTransactionCategoryCommandHandler` now inject `IFxConverter` and FX-convert the row's `Amount`/`Currency` to MDL at the row's `TransactionDate` before calling `MarkDeleted` / `SetCategory`. Conversion is always attempted (single cached lookup); the nullable propagates so the handler simply skips when no usable rate exists — same contract as the create path.

**RebuildBudgetPeriods slice** — `POST /budgets/{id}/rebuild-periods` and `POST /budgets/rebuild-all-periods` are the escape hatch for any drift accumulated before the inverse handlers landed. `RebuildBudgetPeriodsCommand(Guid? BudgetId)` (null = every budget) → `RebuildBudgetPeriodsResult(BudgetsRebuilt, PeriodsAffected)`. Per budget: load every non-deleted Expense row with `CategoryId == budget.CategoryId && !IsTransfer && !IsAdjustment`, FX-convert each to MDL at its own date (rows with no usable rate are skipped), group by `(Year, Month)`, delete every existing `BudgetPeriod` for that budget, then insert fresh rows for each non-empty group. Both endpoints return `200 { budgetsRebuilt, periodsAffected }`; an unknown explicit `BudgetId` returns 404 via `BudgetErrors.NotFound`.

**Rough edges**:
- BudgetPeriod has no Year/Month range validation in the EF config — the domain factory rejects month ∉ [1, 12] and year ≤ 0, but only future writes go through the factory.

### SavingsGoal slice

User-defined target with two mutually exclusive tracking modes. Two domain entities (`SavingsGoal` + `SavingsGoalContribution`), six endpoints, no domain events.

- `SavingsGoal` is the user-facing entity (Name + TargetAmount + optional TargetDate + LinkedAccountId? + ManualSavedAmount? + IsArchived). MDL-only in v1; the factory rejects any other currency with `SavingsGoalErrors.MdlOnly`. The HasQueryFilter hides archived rows from default queries; the archive handler uses `IgnoreQueryFilters()` so the same id can be re-archived idempotently.
- **Linked-account mode** — `LinkedAccountId` points at an `Account`; `ManualSavedAmount` is `null`. The read-side handler computes `Saved` live: anchor + Σ income − Σ expense (same shape as `GetAccountsQueryHandler`), FX-converted to MDL at today's rate via `IFxConverter`. If no usable rate exists, `Saved = 0` and `MissingFxRate = true`.
- **Manual mode** — `LinkedAccountId` is `null`; `ManualSavedAmount` holds a writable MDL `Money?`, defaulted to `Money.Zero("MDL")` at creation. `PATCH /goals/{id}/manual-saved` rejects with `SavingsGoalErrors.NotInManualMode` when the goal is linked.
- **Contribution history (`SavingsGoalContribution`)** — time-series table written by `UpdateManualSavedCommandHandler` on every manual-saved PATCH: each non-zero delta becomes a row with `Amount = newSaved − previousSaved` (signed; positive = contribution, negative = withdrawal), `OccurredOn = today` from `IDateTimeProvider`, MDL-only, optional notes ≤500 chars. Zero deltas are skipped. Linked-mode goals do NOT write to this table — their contribution history is derived at read time from the linked account's transactions (signed by `Direction`: Income → +, Expense → −). Mixing the two would double-count, since the linked balance already integrates every transaction.
- **Mode switching** is one update away: `LinkAccount(id)` flips to linked mode and clears the manual amount; `Unlink()` flips back to manual mode and resets the manual amount to zero (clean-slate contract — the prior value isn't preserved). Both are idempotent on the same target. Re-linking to a different account is free.
- The nullable `ManualSavedAmount` is persisted as two paired scalar columns (`manual_saved_amount_value` + `manual_saved_amount_currency`) rather than a `ComplexProperty<Money?>` — EF Core 10 doesn't model nullable value-object instances cleanly. The domain entity reconstructs the `Money?` from the pair in a read-only getter; setters always write the pair together. The columns are both NULL in linked mode, both populated in manual mode.

**GoalStatus rules** (computed server-side, never user-supplied):

- `Saved >= TargetAmount` → `Achieved` (always, even past the deadline).
- Else if `TargetDate is null` → `OnTrack` (no pace to compare against).
- Else if `today > TargetDate` → `Behind`.
- Else linear-pace check: `expected = target * (monthsElapsed / monthsTotal)` against the goal's creation date; saved ≥ 90% of expected is `OnTrack`, less is `AtRisk`. `MonthsBetween(a, b)` uses 30.4375 days/month (long-run avg).

**RequiredMonthlyContribution** — null when `TargetDate is null` OR when the goal is achieved; otherwise `(target − saved) / max(1, ceil(monthsRemaining))`. The `max(1, …)` guard means past-the-deadline goals get "the shortfall this month" rather than divide-by-zero.

**Endpoints**

- `POST /goals` → `CreateGoalCommand(Name, TargetAmount, TargetDate?, LinkedAccountId?)` → `{ id }`. Validates name (non-blank, ≤100), target > 0, target date not in the past; if `LinkedAccountId` is set, verifies the account exists and isn't archived (the default `Account` query filter hides archived rows, so a switch to an archived account 404s with `AccountErrors.NotFound`).
- `GET /goals` → `GoalDto[]`. No filter params for v1. Excludes archived (both via the EF query filter and an explicit `!g.IsArchived` predicate — defense in depth). Computes `Saved` / `Remaining` / `ProgressPercent` / `Status` / `RequiredMonthlyContribution` / `MissingFxRate` per goal via the shared `GoalProjection` helper (same helper backs `GET /goals/{id}` so both stay in lock-step).
- `GET /goals/{id}` → `GoalDetailDto`. Includes the `GoalDto` surface plus `CreatedOn`, `IsArchived`, a `Contributions` list (manual rows for manual-mode goals, transaction-derived rows for linked-mode goals — signed by direction), a `SavedHistory` monthly series (running cumulative for manual mode; live MDL balance per month-end for linked mode, capped at the trailing 12 months), and `Pace` stats (trailing-90-day `AvgMonthlyContribution`, `ProjectedCompletionDate`, `MonthsToAchieveAtPace`). Archived goals stay reachable (`IgnoreQueryFilters`).
- `PUT /goals/{id}` → `UpdateGoalCommand(Id, Name, TargetAmount, TargetDate?, LinkedAccountId?)`. Applies rename, update-target, update-target-date, and link/unlink in that order; the mode switch comes last so a validation failure earlier leaves the existing link untouched.
- `PATCH /goals/{id}/manual-saved` → `UpdateManualSavedCommand(Id, Amount)`. Rejects in linked mode (`NotInManualMode`) and on negative amounts (`ManualSavedMustBeNonNegative`). On a non-zero delta, also writes a `SavingsGoalContribution` row (atomic with the goal update — single `SaveChangesAsync`).
- `DELETE /goals/{id}` → `ArchiveGoalCommand(Id)`. Idempotent: archiving an already-archived goal is a no-op success.

**Pace stats (GET /goals/{id} only)** — trailing 90-day window starting at `today.AddDays(-90)` (clamped at `CreatedOn` for new goals). `AvgMonthlyContribution`:
- Manual mode: `Σ contributions.Amount in window / monthsInWindow`. Null when `monthsInWindow < 1` (i.e. <30 days of history) or no contributions exist.
- Linked mode: `(savedToday − savedAtWindowStart) / monthsInWindow`. Null when the FX-at-window-start lookup fails or `monthsInWindow < 1`.
`ProjectedCompletionDate` is null when `avg is null OR avg ≤ 0 OR saved ≥ target`; otherwise `today + (monthsToAchieve × 30.4375)` days. `MonthsToAchieve` is clamped at 600 (50 years) so absurdly slow paces don't overflow the date. Achieved goals report `MonthsToAchieveAtPace = 0` and a null projection.

**Rough edges**:
- **`SavedHistory` is guaranteed strictly-increasing by `AsOf`** — the saved-over-time series (`GoalSavedPointDto[]`) must never emit two points sharing an `AsOf`, because the detail chart keys on it (a duplicate fired a React duplicate-key warning + drew two dots at the same x). A goal created *today* with a contribution dated *today* used to collide: the manual/empty builders prepended a `(createdOn, 0)` baseline that landed on the same date as `(today, saved)`. Fixed in `GetGoalDetailQueryHandler` — the baseline insert is now guarded on `createdOn < firstPoint.AsOf`, `BuildEmptySavedHistory` collapses to a single `(today, saved)` when `createdOn >= today`, and all three builders (manual, empty, linked) run a final `DedupeByAsOf` pass that keeps the latest value for any repeated date. Pinned by `GetGoalDetailQueryHandlerTests` (manual-created-today, manual-empty-today, linked-created-today).
- **Dangling LinkedAccountId is unreachable in production** — the FK on `savings_goals.linked_account_id` uses `ON DELETE RESTRICT`, so deleting an account that a goal points at fails at the DB. The user must unlink (or archive) the goal first. The read handler still defensively returns `Saved = 0` for an unfound linked account to keep the page renderable if a future code path ever drops the constraint.
- **Linked-mode contributions cascade on goal delete, but the goal is never hard-deleted in v1** — `savings_goal_contributions.goal_id` uses `ON DELETE CASCADE` for symmetry with the budget-period precedent, but the v1 endpoints only archive goals (soft-delete). The cascade only fires if a future migration or out-of-band query hard-deletes a goal row.
- **Switching mode loses the contribution series** — `LinkAccount` / `Unlink` rewrite the goal's mode but leave any existing `SavingsGoalContribution` rows in place. The read handler chooses the source by current mode (manual rows for manual mode, transaction-derived rows for linked mode), so the orphaned set is invisible to the API but still on disk. A future "reset history on mode switch" pass is deferred.
- **Multi-currency deferred** — same as Budget, `TargetAmount` and `ManualSavedAmount` are forced to MDL (the reporting currency) at the factory; non-MDL inputs fail `SavingsGoalErrors.MdlOnly`.
- **No auto-archive on Achieved** — hitting the target sets the status to `Achieved` but doesn't archive the goal. The user keeps seeing it on the dashboard until they explicitly archive (intentional: keeps the win visible). A future "celebrate then auto-archive after N days" flow can opt in if it's wanted.

### DataPortability slice

Full-database JSON backup + destructive restore. Entity-less slice (errors colocate with the feature, same convention as Dashboard/Reports). No Domain entity, no schema change, **no migration**.

- **`IBackupStore`** (`Application/Abstractions/Backup/`) implemented by **`EfBackupStore`** (Infrastructure) — same Application-defines / Infrastructure-implements split as `IFxConverter`/`EfFxConverter`. This is the one place that needs `DbContext.Database` (transactions, `ExecuteDeleteAsync`, raw SQL) and `IgnoreQueryFilters`, which `IApplicationDbContext` deliberately doesn't expose.
- **`BackupDocument`** — `SchemaVersion` (`int`, current = `4` in `BackupSchemaVersion.Current`) + `ExportedAtUtc` + one flat `*Backup` record array per entity (Accounts, Categories, **CategoryPatterns**, Transactions, ImportBatches, Budgets, BudgetPeriods, SavingsGoals, SavingsGoalContributions — 9 arrays). Each `*Backup` mirrors the **persisted columns exactly**: `Money` complex-property components as scalar pairs (`balanceAmount`/`balanceCurrency`, etc.), SavingsGoal's paired nullable `manualSavedAmountValue`/`manualSavedAmountCurrency` (read via `EF.Property<>` since the entity reconstructs them), enums (typed as the enum, serialized as their **name** by the API's shared `JsonStringEnumConverter`), audit timestamps, `isDeleted`/`isArchived` flags, and FKs. The export captures archived rows and soft-deleted transactions (every DbSet read with `IgnoreQueryFilters().AsNoTracking()`) so a restore round-trips with the **same IDs and audit fields**. **`fx_rates` is deliberately excluded** from the backup (since v2) — rates are re-fetchable from BNM, so they're treated as a local cache outside backup scope: never exported, and never touched on restore. *(v2 dropped the `FxRates` array; v3 added the transaction `notes` column to `TransactionBackup`; **v4 added `CategoryPatterns`** — see below. Each bump means older backups no longer import — the handler rejects the version mismatch.)*
  - **Why v4 added `CategoryPatterns` (2026-05-29 data-loss fix):** `category_patterns.category_id` is `ON DELETE CASCADE`, so the restore's `DELETE FROM categories` was silently cascade-wiping every learned/seeded keyword rule — and since patterns weren't in the backup, they were never reinserted. A round-trip provably dropped `category_patterns` 35 → 0. They are now exported, wiped explicitly (before `categories`), and reinserted (after `categories`, their FK parent). The seeded rules also reappear on the next startup via `CategoryPatternSeeder`, but *learned* rules were being lost permanently before this fix.
- **`GET /data/export`** → streams the `BackupDocument` straight to `Response.Body` via `JsonSerializer.SerializeAsync` using the app's configured `JsonOptions` (so enums round-trip as names), with `Content-Type: application/json` + `Content-Disposition: attachment; filename="moneymanagement-backup-{yyyy-MM-dd_HH-mm}.json"` (UTC; underscore + `HH-mm` because `:` is illegal in filenames, so multiple same-day exports don't collide). Same streaming-download shape as `/reports/transactions.csv`. The query handler (`ExportDataQueryHandler`) just delegates to `IBackupStore.ExportAsync`.
- **`POST /data/import`** → multipart `file` upload (`.DisableAntiforgery()`; 50MB cap → 400; missing/empty file → 400). The endpoint deserializes with the same `JsonOptions`; a `JsonException` or null result → 400 `DataErrors.MalformedBackup`. On success it dispatches `ImportDataCommand(BackupDocument)`, which validates `SchemaVersion == BackupSchemaVersion.Current` (else `DataErrors.UnsupportedSchemaVersion`) and that no entity array is null (else `MalformedBackup`), then calls `IBackupStore.RestoreAsync` and returns an `ImportDataResult` (per-table insert counts).
- **Restore is destructive + transactional.** `EfBackupStore.RestoreAsync` opens one `BeginTransactionAsync`, **wipes every backed-up table child-first** with `IgnoreQueryFilters().ExecuteDeleteAsync()` (so soft-deleted/archived rows go too), then **reinserts parent-first**; commit at the end, so any failure rolls back and leaves existing data untouched. Delete order: SavingsGoalContributions → SavingsGoals → BudgetPeriods → Budgets → Transactions → ImportBatches → **CategoryPatterns** → Categories → Accounts. Insert order: Accounts → Categories → **CategoryPatterns** → ImportBatches → Transactions → Budgets → BudgetPeriods → SavingsGoals → SavingsGoalContributions. **`fx_rates` is left untouched** — it is neither wiped nor reinserted, so a user's local FX rates survive a restore (nothing FKs to `fx_rates`, so this is safe).
- **Two ordering subtleties baked in:**
  - `categories.parent_id` is `ON DELETE RESTRICT`, which Postgres checks **immediately per-row** (non-deferrable, unlike `NO ACTION`'s end-of-statement check). A single `DELETE FROM categories` over a parent+child hierarchy would raise a FK violation, so the wipe runs `ExecuteUpdateAsync(SetProperty(c => c.ParentId, null))` over all categories **before** the delete. Inserts re-establish the hierarchy by inserting parents before children (`TopologicalByParent`).
  - Cross-table RESTRICT (`savings_goals.linked_account_id` → accounts) is safe because goals are fully deleted before accounts.
- **Why raw parameterized INSERTs, not domain factories:** the restore must preserve exact IDs/audit/flags so FKs and the snapshot round-trip. `Account.Create`/`Transaction.Create` mint fresh `Guid.CreateVersion7()` IDs and rewrite audit fields, and tracked EF inserts would fight `AuditableEntitySaveChangesInterceptor`. So `EfBackupStore` issues per-row `ExecuteSqlRawAsync` with bound `NpgsqlParameter`s. Table names come from `context.Model.FindEntityType(...)` (tracks the snake_case mapping); column names are passed per-table and match the EF config; enums are written via `.ToString()` (matches `HasConversion<string>()`); `DateTime` columns are typed `timestamptz` and coerced to `Kind=Utc`, `DateOnly` to `date`. Values are **always** bound parameters, never interpolated.
- **Testing**: `ImportDataCommandHandlerTests` + `ExportDataQueryHandlerTests` substitute `IBackupStore` and pin the orchestration/validation (schema-version + null-array rejection, delegation, count pass-through). The real EF wipe/reinsert in `EfBackupStore` is integration-level and **manually verified** — there is no Infrastructure test project in this repo (same documented deviation as `BnmRateProvider.Parse`).

### Domain event handlers

`IDomainEventHandler<T>` is defined in SharedKernel; `Application/DependencyInjection.cs` scans the Application assembly for handlers and registers them with scoped lifetime. They are dispatched by `DomainEventsDispatcher` (in Infrastructure) **after** `ApplicationDbContext.SaveChangesAsync` completes — see `ApplicationDbContext.SaveChangesAsync`: events are collected from the change tracker *before* the save, then dispatched *after* the save returns. The dispatcher resolves handlers from a freshly-created scope, so each handler has its own DI scope (a different `IApplicationDbContext` instance) and SaveChanges inside a handler does **not** retrigger the emitting event. Handlers that throw bubble up — there's no try/catch in the dispatcher — failures are loud during dev. No outbox is wired up yet; the save-then-dispatch contract is acceptable for single-user self-hosted v1.

Currently registered:

- **`UpdateBudgetPeriodOnTransactionCreatedHandler`** — first end-to-end use of the pattern. Subscribes to `TransactionCreatedDomainEvent` (raised inside `Transaction.Create`, with the calling handler — `CreateTransactionCommandHandler`, `CreateTransferCommandHandler`, `AdjustBalanceCommandHandler`, `CommitImportCommandHandler` — passing the FX-converted `AmountMdl` it already computes for the read side). Skip rules, evaluated before any DB work:
  - `CategoryId is null` → uncategorized, no budget can apply
  - `IsTransfer` → internal movement, not real P&L
  - `IsAdjustment` → balance true-up, not real spend
  - `Direction != Expense` → budgets cap spending, not income
  - `AmountMdl is null` (or ≤ 0) → no usable FX rate at the transaction date, can't quantify
  - No active budget for `CategoryId` → most categories don't have one, common case
  Otherwise: find-or-create the `BudgetPeriod` for `(Budget.Id, evt.TransactionDate.Year, evt.TransactionDate.Month)` and `AddSpend(evt.AmountMdl.Value)`. `IFxConverter` lives on the write side now (alongside `IApplicationDbContext`) for every `Transaction.Create` caller, mirroring the per-row-date conversion `GetTransactionsQueryHandler` does for `TransactionDto.AmountMdl`.
- **`UpdateBudgetPeriodOnTransactionDeletedHandler`** — subscribes to `TransactionDeletedDomainEvent` (raised by `Transaction.MarkDeleted`, idempotent on already-deleted rows). Same skip predicate as the create handler. When the budget and the period both exist, calls `period.SubtractSpend(evt.AmountMdl.Value)`; missing budget or missing period returns silently — there's nothing to subtract from. `DeleteTransactionCommandHandler` injects `IFxConverter` and FX-converts at the row's date before `MarkDeleted` so the event carries the MDL value.
- **`UpdateBudgetPeriodOnTransactionCategoryChangedHandler`** — subscribes to `TransactionCategoryChangedDomainEvent` (raised by `Transaction.SetCategory` whenever the category actually changes; the aggregate's idempotence filter means old == new never reaches the handler). Skips on the COMMON gates only (transfer / adjustment / income / null mdl). For the OLD side: if `OldCategoryId` has a budget and the matching period exists, subtract. For the NEW side: if `NewCategoryId` has a budget, find-or-create the period and add. One `SaveChangesAsync` covers both sides; if neither category is budgeted the handler returns silently. `UpdateTransactionCategoryCommandHandler` injects `IFxConverter` and passes the MDL value through `SetCategory`.

---

## Validation & Business Rules

Enforced in domain entities (`Result<T>` from `Create` factories) and FluentValidation (for input shape):

- `Account.name` non-empty, ≤ 100 chars
- `Account.balance.currency` is a 3-letter uppercase ISO code (regex `^[A-Z]{3}$`); enforced both in `Account.Create` (domain) and `CreateAccountCommandValidator` (FluentValidation) for defense in depth
- `Account.balance.amount` can be negative only for `CreditCard` type
- `Category.name` non-empty, ≤ 80 chars; `color`, if set, matches `#RRGGBB`
- `Transaction.amount.amount > 0` always (sign comes from `direction`)
- `Transaction.amount.currency` is a 3-letter uppercase ISO code (`CurrencyCodes.IsValidIso`). Cross-entity invariant enforced at every write boundary (`CreateTransactionCommandHandler`, `CreateTransferCommandHandler`, `CommitImportCommandHandler`, `AdjustBalanceCommandHandler`): `transaction.amount.currency == account.balance.currency`. **Each leg always matches its own account's currency** — which is precisely what makes cross-currency transfers legal (see below): the two legs simply differ.
- **Cross-currency transfers** (`CreateTransferCommandHandler` and the `CommitImport` counter-leg path): when source and destination currencies differ, the caller supplies the destination amount (`CreateTransferCommand.DestinationAmount` / `TransactionToImport.CounterAmount`; required & `> 0`, else `TransferErrors.DestinationAmountRequired` / `TransactionErrors.CounterAmountRequired`). The source leg is `Money(amount, sourceCcy)` and the destination/counter leg is `Money(destAmount, destCcy)` — so the destination account's native balance moves by the received amount. Both legs keep the **source-derived `amountMdl`** (value is conserved across the pair; transfers are excluded from income/expense aggregates anyway). The effective rate is not stored (= source ÷ destination); for traceability each leg's `OriginalAmount`/`OriginalCurrency` is stamped with the *other* leg's amount+currency. Same-currency transfers are unchanged (no destination amount, null `Original*`). The old same-currency rejection (`MismatchedCurrencies` / `TransferCurrencyMismatch`) is gone.
- `Transaction.IsTransfer` and `Transaction.IsAdjustment` are mutually exclusive.
- The account balance-change action (`POST /accounts/{id}/balance-changes` → `AdjustBalanceCommand(AccountId, Kind, Value, Date, Notes)`) is permitted only on `Brokerage`, `CryptoExchange`, `P2PLending`, `BankDeposit` accounts; rejected with `TransactionErrors.AdjustmentAccountTypeNotEligible` otherwise. Three `BalanceChangeKind`s: **Adjustment** (`Value` = new total balance → signed delta, category `Balance Adjustment`, `IsAdjustment=true`; a zero-delta adjustment is rejected with `AdjustmentDeltaZero`), **Investment** (`Value` = amount in → Income, category `Investment`, `IsTransfer=true`), **Withdrawal** (`Value` = amount out → Expense, category `Withdrawal`, `IsTransfer=true`). Investment/Withdrawal require `Value > 0` and use the transfer flag with **no counter account**, so they land in the account Performance card's contribution / withdrawal buckets and stay out of income/expense + budget reporting, while Adjustment feeds Net P&L. The optional `Notes` is stored as the synthetic transaction's **`Notes`** (≤500); its **`Description` is always the kind's fixed label** ("Investment" / "Withdrawal" / "Balance adjustment"). *(Until 2026-06-01 the handler mistakenly wrote `Notes` into the `Description` and left `Notes` null — fixed; the dialog's "Notes" field now behaves like notes everywhere else.)*
- `PUT /transactions/{id}/category` (`UpdateTransactionCategoryCommand`) recategorizes a single row (`null` clears it). A non-null category's `Flow` must be compatible with the row's `Direction` (Income → `Income`/`Both`, Expense → `Expense`/`Both`) else `TransactionErrors.CategoryFlowMismatch`. The handler FX-converts at the row's date and feeds the MDL value into `Transaction.SetCategory`, which raises `TransactionCategoryChangedDomainEvent` so the budget rollup moves the spend in one pass (see Budget slice > Inverse updates). `PUT /categories/{id}` (`UpdateCategoryCommand`) renames/edits a category (name · flow · colour, same validation as `Create`).
- `PUT /transactions/{id}/notes` (`UpdateTransactionNotesCommand`, body `{ notes: string|null }`, 204) sets/clears the optional free-text `notes` on any row (`Transaction.SetNotes`, max 500, blank→null). Unlike category, notes are descriptive only — **no** domain event, no FX, no budget/report/balance impact. Mirrors the recategorize endpoint's shape. `notes` is also settable at manual creation (`POST /transactions`).
- `FxRate.fromCurrency` and `FxRate.toCurrency` are valid ISO codes and **must differ**; `FxRate.rate > 0`; uniqueness enforced at DB level on `(from_currency, to_currency, as_of)`
- `Transaction.description` non-empty, ≤ 500 chars
- `Transaction.transactionDate <= today` (no future-dated transactions in v1), where **"today" is the UTC date** (`DateOnly.FromDateTime(DateTime.UtcNow)`); the frontend defaults/validates these dates in UTC too so the two agree in any timezone. Same rule for the balance-adjustment `Date` and the savings-goal contribution `occurred_on`.
- `Transaction.originalCurrency`, if set, is exactly 3 chars
- `ImportBatch.fileHash` is a 64-char hex sha256
- `Budget.monthly_limit.amount > 0` and `Budget.monthly_limit.currency == "MDL"` (MDL-only in v1); at most one active budget per `category_id` (enforced both in the handler pre-check and via the DB-side filtered unique index)
- `BudgetPeriod.year > 0`, `BudgetPeriod.month` in 1..12, `BudgetPeriod.AddSpend(amount > 0)`, `BudgetPeriod.SubtractSpend(amount > 0)` (rejects non-positive input with the same `SpendMustBePositive` error; clamps `Spent` at zero if the result would be negative)
- `SavingsGoal.name` non-empty, ≤ 100 chars; `target_amount > 0` and `target_amount_currency == "MDL"` (MDL-only in v1); `target_date` (when set) must be ≥ today at creation/update; manual mode allows `manual_saved_amount ≥ 0` and rejects non-MDL currencies; `SetManualSaved` rejects in linked mode
- `SavingsGoalContribution.amount != 0` (sign carries semantic: positive = contribution, negative = withdrawal); `amount.currency == "MDL"`; `occurred_on <= today`; `notes` ≤ 500 chars (trimmed; whitespace-only normalized to NULL). Callers are responsible for skipping zero-delta writes — the factory rejects with `ContributionAmountMustBeNonZero` rather than silently no-op'ing.

**Transfers (Phase 3)** — a transfer is two `Transaction` rows that both have `is_transfer = true`. The source leg has `Direction = Expense` and `CounterAccountId = destination.Id`; the destination leg has `Direction = Income` and `CounterAccountId = source.Id`. `POST /transfers` (handled by `CreateTransferCommand`) loads both accounts, asserts same currency + distinct ids, then persists both legs in a single `SaveChangesAsync`. An optional `notes` (≤`Transaction.NotesMaxLength`) is written to **both** legs, so the annotation shows from either account (same as the import path). Income/Expense reports MUST filter `IsTransfer = false` — there is no compiler check enforcing this. When importing a statement, `ITransferDetector` auto-suggests the `IsTransfer` flag per row (the import path doesn't know the counter account, so `CounterAccountId` stays null on imported transfer legs).

**Balance adjustments (Phase 4)** — investment/crypto/P2P/deposit accounts can be true-up'd monthly via `POST /accounts/{id}/balance-adjustments` with `{ newBalance, date, notes? }`. `AdjustBalanceCommandHandler` loads the account, validates the type is in the allowed set, computes the current balance (`opening + Σ signed transactions`, all in the account's native currency, summed over all non-deleted rows — same definition as `GetAccountsQueryHandler` so the delta is computed against what the UI shows), and persists a single `Transaction` for the delta: `Direction = delta > 0 ? Income : Expense`, `Amount = |delta|`, `IsAdjustment = true`, `CategoryId = SeededCategories.BalanceAdjustmentId`. Zero-delta is rejected. The `AmountMdl` field on `TransactionDto` is computed by `GetTransactionsQueryHandler` via `IFxConverter` against the transaction date — null when no usable rate exists.

---

## Migrations

```bash
# from solution root
dotnet ef migrations add <Name> -p src/MoneyManagement.Infrastructure -s src/MoneyManagement.Api -o Database/Migrations
dotnet ef database update -p src/MoneyManagement.Infrastructure -s src/MoneyManagement.Api
```

Migrations live under `src/MoneyManagement.Infrastructure/Database/Migrations/`. Commit them to source control; never edit a migration that has been applied to a shared environment. All migrations are generated via the EF Core CLI (`dotnet ef migrations add`, API stopped first), then the generated body is converted from block-scoped to file-scoped namespace to satisfy `IDE0161` (the auto-generated `.Designer.cs` is exempt); `dotnet ef migrations has-pending-model-changes` must report clean.

Current state — a **single baseline** migration:
- `20260528201519_InitialCreate` — creates the entire schema (all 10 tables: `accounts`, `categories`, `category_patterns`, `transactions`, `import_batches`, `fx_rates`, `budgets`, `budget_periods`, `savings_goals`, `savings_goal_contributions`) with their indexes, FKs, `Money` complex properties, enum-as-string columns, and the `created_at` / `updated_at` audit columns on every table. The earlier incremental migration history was collapsed into this one clean CLI-generated baseline on 2026-05-28 (the app had not been released, so there was no applied-migration history to preserve). Applied automatically on dev startup via `MigrationExtensions` (`db.Database.Migrate()`), which also creates the database if it does not exist.

---

## Local Postgres

Postgres runs on `localhost:5432`. Two supported setups: a **native install** (e.g. on Windows, the PostgreSQL 17 service `postgresql-x64-17` with `psql.exe` at the default `C:\Program Files\PostgreSQL\17\bin\`) or the portable **Docker** alternative via the root `docker-compose.yml`. Either way the app needs nothing but Postgres listening on `:5432`; the connection string is supplied via **.NET user-secrets** (see below) and is never committed.

### Two databases: real vs. test

The app is single-user and self-hosted, so the "real" data and the QA/smoke-test churn are separated by **database name**, switched by **launch profile** — not by environment or manual config edits:

| Purpose | Database | Selected by | Notes |
|---------|----------|-------------|-------|
| **Real** daily data | `money_management` | default profiles (`http` / `https`, bare Visual Studio Run) — reads `ConnectionStrings:Default` from user-secrets | Never run smoke tests here. Inspect read-only via `mcp__postgres-real__query`. |
| **Test** / QA | `money_management_test` | the **`qa`** launch profile, which sets `UseTestDatabase=true` (→ `ConnectionStrings:Test`) | Disposable; smoke-test assertions via `mcp__postgres-test__query`. |

- Both profiles run in the `Development` environment, so `ApplyMigrations()` (creates the DB if missing + applies the baseline migration) and the category seeders work identically for either DB. A fresh DB is created, migrated, and seeded on first run — no manual `createdb`.
- `ApplyMigrations()` logs `Using database '<name>' on host '<host>'` at startup. **Check this line** to confirm which DB you're about to write to before driving any flow.
- Connection strings live in **.NET user-secrets** (not committed). Set them once:
  ```bash
  cd src/MoneyManagement.Api
  dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=money_management;Username=postgres;Password=<your-postgres-password>"
  dotnet user-secrets set "ConnectionStrings:Test"    "Host=localhost;Database=money_management_test;Username=postgres;Password=<your-postgres-password>"
  ```
- The `qa` profile (in `src/MoneyManagement.Api/Properties/launchSettings.json`) sets `UseTestDatabase=true`; `AddInfrastructure` then resolves `ConnectionStrings:Test` instead of `:Default` (see `Infrastructure/DependencyInjection.cs`). No secret lives in any committed file.

### Docker alternative

- `docker compose up -d` from the repo root brings up `postgres:17` on `localhost:5432` (DB `money_management`, named volume `mm_pgdata`; `docker compose down -v` for a clean slate). The container password is read from the `POSTGRES_PASSWORD` env var (see `.env.example` → copy to the git-ignored `.env`). Use on machines without a native install. The `qa` profile's `money_management_test` is still auto-created by `ApplyMigrations()` inside that container.
- Talk to a container DB via `docker exec -it money-management-db psql -U postgres -d money_management` when no host `psql.exe` is present.

---

## MCPs (Development Tooling)

### Postgres MCP (recommended)

Direct read access to the local DBs — verify migrations, inspect data, debug without writing throwaway queries.

The project-scope **`.mcp.json`** at the repo root (git-ignored; copy from the committed **`.mcp.json.example`** and fill in your password) wires up **two** `@modelcontextprotocol/server-postgres` instances (official; every query runs inside a `READ ONLY` transaction, so neither can mutate data — the real DB is safe to attach):

| MCP server | Tool prefix | Database | Use for |
|------------|-------------|----------|---------|
| `postgres-test` | `mcp__postgres-test__*` | `money_management_test` | smoke-test assertions, throwaway poking |
| `postgres-real` | `mcp__postgres-real__*` | `money_management` | debugging / inspecting your real data |

The URLs URL-encode the creds (`&` → `%26`, `%` → `%25`), e.g.:

```
postgresql://postgres:YOUR_PASSWORD@localhost:5432/money_management_test
postgresql://postgres:YOUR_PASSWORD@localhost:5432/money_management
```

(URL-encode any special chars in the password, e.g. `&` → `%26`, `%` → `%25`.)

On first open in a fresh clone (or after editing `.mcp.json`), Claude Code prompts you to trust the project-scoped MCP config — approve it, then **restart the session** for the tools to surface. The local DB password is **not** committed: the API reads it from .NET user-secrets (`ConnectionStrings:Default` / `:Test`), `docker-compose` and the integration tests read it from the `POSTGRES_PASSWORD` env var (see `.env.example`), and `.mcp.json` is your local untracked copy of `.mcp.json.example`. If you change the password, update it in user-secrets, your `.env`, and your local `.mcp.json`.

Alternative: `postgres-mcp` by crystaldba — adds index tuning, query performance analysis. Heavier; only worth it if you need it.

### Claude Code agents

Per the project-root [CLAUDE.md](./CLAUDE.md):

- Backend changes (anything in this doc's scope — `src/MoneyManagement.*` and `tests/MoneyManagement.*.Tests`) go through the **`c-sharp-pro`** agent. It's tuned for idiomatic modern C#, EF Core, and ASP.NET Core patterns.
- Frontend changes (`web/`) go through the **`frontend-developer`** agent — see [FRONTEND.md](./FRONTEND.md).

When a change spans both stacks, dispatch both agents in parallel from the main thread, then collate their reports.
