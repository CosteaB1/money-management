/** Generic paginated response wrapper — mirrors MoneyManagement.Application.Common.PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

/** Mirrors MoneyManagement.Domain.Accounts.AccountType enum. */
export type AccountType =
  | 'Cash'
  | 'CreditCard'
  | 'BankDeposit'
  | 'BankCurrent'
  | 'Brokerage'
  | 'CryptoExchange'
  | 'P2PLending';

/** Mirrors MoneyManagement.Application.Features.Accounts.AccountDto. */
export interface AccountDto {
  id: string;
  name: string;
  type: AccountType;
  /** ISO 4217 currency code (e.g. "MDL", "USD", "EUR"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  openingDate: string;
  isArchived: boolean;
  notes: string | null;
  /**
   * Live computed balance in the account's native currency:
   * `balance = anchor + Σ income − Σ expense` over all non-deleted
   * transactions on this account (the anchor is the starting amount
   * the user supplied at creation time). Always present.
   */
  balance: number;
  /**
   * `balance` expressed in MDL using the FX rate available for the
   * account's currency on the latest applicable date. `null` if no
   * rate exists. For MDL accounts this is the identity case and equals
   * `balance`.
   */
  balanceMdl: number | null;
}

/** POST /accounts request body — mirrors AccountEndpoints.CreateAccountRequest. */
export interface CreateAccountRequest {
  name: string;
  type: AccountType;
  /**
   * Starting balance the account is anchored to at creation time. After
   * creation, subsequent ledger movements (transactions, transfers,
   * balance adjustments) drift `AccountDto.balance` from this anchor.
   */
  balance: number;
  /** ISO 4217 currency code (e.g. "MDL", "USD", "EUR"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  openingDate: string;
  notes?: string | null;
}

export interface CreateAccountResponse {
  id: string;
}

/**
 * PUT /accounts/{id} request body. Edits only the user-mutable metadata —
 * `name` (required) and `notes` (`null` to clear). Currency and type are
 * fixed after creation and are NOT part of this contract. Returns 204 No
 * Content, 404 if the account doesn't exist, or 400 ProblemDetails on
 * validation error.
 */
export interface UpdateAccountRequest {
  name: string;
  notes: string | null;
}

export type CategoryFlow = 'Expense' | 'Income' | 'Both';
export type TransactionDirection = 'Income' | 'Expense';
export type TransactionSource = 'Manual' | 'Imported';
export type BankSource = 'Maib';

export interface CategoryDto {
  id: string;
  name: string;
  parentId?: string;
  color?: string;
  icon?: string;
  flow: CategoryFlow;
  isArchived: boolean;
}

/**
 * Origin of an auto-categorization keyword pattern.
 *  - `Seeded`  → shipped with the app's default rule set.
 *  - `Learned` → derived from the user's own categorization history.
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type CategoryPatternSource = 'Seeded' | 'Learned';

/**
 * Mirrors the backend CategoryPatternDto. A pattern maps an (upper-cased)
 * keyword found in an imported transaction's memo to a category, so future
 * imports can auto-suggest a category. Patterns only affect FUTURE imports —
 * mutating them never rewrites existing transactions.
 */
export interface CategoryPatternDto {
  id: string;
  /** Upper-cased keyword the importer matches against transaction memos. */
  keyword: string;
  categoryId: string;
  categoryName: string;
  source: CategoryPatternSource;
}

/**
 * POST /category-patterns request body. The backend upper-cases `keyword`
 * server-side; a duplicate keyword comes back as a 409 ProblemDetails whose
 * `detail` we surface verbatim.
 */
export interface CreateCategoryPatternRequest {
  keyword: string;
  categoryId: string;
}

export interface CreateCategoryPatternResponse {
  id: string;
}

/** PUT /category-patterns/{id} — same shape as create; fully replaces it. */
export type UpdateCategoryPatternRequest = CreateCategoryPatternRequest;

/**
 * PUT /categories/{id} request body. Fully replaces the editable category
 * fields — `color` is optional (omit to clear). The backend returns 204.
 */
export interface UpdateCategoryRequest {
  name: string;
  flow: CategoryFlow;
  color?: string;
}

export interface TransactionDto {
  id: string;
  accountId: string;
  categoryId?: string;
  categoryName?: string;
  transactionDate: string;
  direction: TransactionDirection;
  amount: number;
  description: string;
  /**
   * User-authored free-text annotation, distinct from `description` (the bank
   * memo / label). `null` when the user has not added a note. Capped at 500
   * characters server-side.
   */
  notes: string | null;
  originalAmount?: number;
  originalCurrency?: string;
  source: TransactionSource;
  importBatchId?: string;
  /** True if this row is one half of an internal transfer pair. */
  isTransfer: boolean;
  /** The opposing account's id when `isTransfer` is true, otherwise null. */
  counterAccountId: string | null;
  /** Native currency of the transaction; always equals the account's currency. */
  currency: string;
  /**
   * Amount expressed in MDL using the FX rate available for `currency` on
   * `transactionDate`. `null` if no rate exists.
   * For MDL accounts this is the identity case and equals `amount`.
   */
  amountMdl: number | null;
  /**
   * True if this row is a balance adjustment (an internal correction that
   * reconciles the account to a known balance). Mutually exclusive with
   * `isTransfer`.
   */
  isAdjustment: boolean;
}

export interface CreateTransactionRequest {
  accountId: string;
  categoryId?: string;
  transactionDate: string;
  direction: TransactionDirection;
  amount: number;
  description: string;
  /**
   * Optional user-authored note (≤500 chars). Omit, or send null/blank, to
   * create the transaction without a note.
   */
  notes?: string | null;
  originalAmount?: number;
  originalCurrency?: string;
  /** When true, marks this transaction as one leg of an internal transfer. */
  isTransfer?: boolean;
  /** Optional opposing account for transfers (counter-side). */
  counterAccountId?: string | null;
  /**
   * When true, marks this transaction as a balance adjustment. In practice
   * the regular create endpoint does NOT take this flag — balance changes go
   * through `POST /accounts/{id}/balance-changes`. Kept here for
   * contract completeness.
   */
  isAdjustment?: boolean;
}

export interface CreateTransactionResponse {
  id: string;
}

/**
 * PUT /transactions/{id}/category request body. `categoryId: null` clears the
 * category (Uncategorized). The backend returns 204, or 400 when the chosen
 * category's flow is incompatible with the transaction's direction (e.g.
 * assigning an Income-only category to an Expense row).
 */
export interface UpdateTransactionCategoryRequest {
  categoryId: string | null;
}

/**
 * PUT /transactions/{id}/notes request body. `notes: null` (or an empty/blank
 * string) clears the user-authored note; otherwise it replaces it. The backend
 * returns 204 No Content. Mirrors the per-row inline note editor in the
 * transactions table.
 */
export interface UpdateTransactionNotesRequest {
  notes: string | null;
}

export interface ParsedTransactionPreview {
  transactionDate: string;
  direction: TransactionDirection;
  amount: number;
  description: string;
  suggestedCategoryId?: string;
  suggestedCategoryName?: string;
  isDuplicate: boolean;
  originalAmount?: number;
  originalCurrency?: string;
  /** Backend's auto-suggested transfer flag — user can toggle in the preview UI. */
  isTransfer: boolean;
}

export interface StatementPreviewDto {
  fileHash: string;
  statementPeriod: { from: string; to: string };
  bankSource: BankSource;
  summary: {
    openingBalance: number;
    closingBalance: number;
    totalIn: number;
    totalOut: number;
    /**
     * Σ of statement commissions/fees (maib's "Total comision"). maib reports
     * this SEPARATELY from `totalOut`, so the reconciliation identity is
     * `openingBalance + totalIn − totalOut − totalFees === closingBalance`.
     */
    totalFees: number;
  };
  transactions: ParsedTransactionPreview[];
}

export interface CommitImportRequest {
  accountId: string;
  fileName: string;
  fileHash: string;
  bankSource: BankSource;
  transactions: Array<{
    transactionDate: string;
    direction: TransactionDirection;
    amount: number;
    description: string;
    categoryId?: string;
    originalAmount?: number;
    originalCurrency?: string;
    /**
     * Marks this row as an internal movement. Always excludes the row from
     * income/expense aggregates. When `counterAccountId` is also supplied,
     * the backend additionally writes a matching leg on the counter account
     * (useful for ATM withdrawals → Cash, Salary → Brokerage/Fagura where the
     * counter side has no PDF to import). When `counterAccountId` is null or
     * omitted, the backend only inserts this row as-is — the canonical case
     * for A2A between two MAIB accounts where each PDF provides its own side.
     */
    isTransfer?: boolean;
    /**
     * Optional counter account for transfer rows. When set, the backend
     * creates the opposing leg on that account. Leave `null`/omit when the
     * other side already has its own statement (e.g. MAIB → MAIB).
     */
    counterAccountId?: string | null;
    /**
     * Amount received on the counter account, expressed in the counter
     * account's native currency. Required (> 0) ONLY when the counter
     * account's currency differs from the import account's currency; omit
     * for same-currency transfers (the backend defaults it to `amount`).
     */
    counterAmount?: number;
    /** Optional free-text note the user attached to this row during import. Omitted when blank. */
    notes?: string;
  }>;
  /**
   * Optional "learn-with-confirm" rules harvested from the preview: when the
   * user categorized a row the suggester missed (or overrode), the UI proposes
   * an editable keyword. Confirmed rules ride along here so the import can seed
   * category patterns that auto-suggest the SAME memo on FUTURE imports.
   *
   * The backend upserts these best-effort inside the import transaction —
   * blank keywords and duplicates are skipped silently, so a failed rule never
   * fails the import. Omitted entirely when the user confirmed no rules.
   */
  learnedPatterns?: { keyword: string; categoryId: string }[];
}

/** POST /transfers request body. Source/destination may be in different currencies. */
export interface CreateTransferRequest {
  sourceAccountId: string;
  destinationAccountId: string;
  /** Positive amount in the SOURCE account's currency. */
  amount: number;
  /** ISO date string (yyyy-MM-dd) */
  date: string;
  description: string;
  categoryId?: string;
  /** Optional free-text note; the backend stores it on both transfer legs. */
  notes?: string;
  /**
   * Amount credited to the destination account, expressed in the
   * destination account's native currency. Required (> 0) ONLY when source
   * and destination currencies differ; omit for same-currency transfers
   * (the backend defaults it to `amount`).
   */
  destinationAmount?: number;
}

export interface CreateTransferResponse {
  sourceTransactionId: string;
  destinationTransactionId: string;
}

export interface CommitResultDto {
  importBatchId: string;
  importedCount: number;
  skippedDuplicates: number;
}

/**
 * Origin of an FX rate row.
 *  - `Manual`  → entered by the user via the create dialog.
 *  - `BnmAuto` → fetched from BNM (Banca Națională a Moldovei) via the
 *    `POST /fx-rates/refresh` endpoint. Subsequent refreshes overwrite
 *    rows with the same (from, to, asOf) key.
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type FxRateSource = 'Manual' | 'BnmAuto';

/** Mirrors MoneyManagement.Application.Features.FxRates.FxRateDto. */
export interface FxRateDto {
  id: string;
  /** 3-letter ISO currency code */
  fromCurrency: string;
  /** 3-letter ISO currency code */
  toCurrency: string;
  rate: number;
  /** ISO date string (yyyy-MM-dd) */
  asOf: string;
  createdAt: string;
  updatedAt: string;
  /** Where this rate came from — `Manual` user entry or `BnmAuto` refresh. */
  source: FxRateSource;
}

/** POST /fx-rates request body. */
export interface CreateFxRateRequest {
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  /** ISO date string (yyyy-MM-dd) */
  asOf: string;
}

export interface CreateFxRateResponse {
  id: string;
}

/**
 * GET /fx-rates/convert?from={ISO}&to={ISO}&date={yyyy-MM-dd}&amount={number}
 * response. `convertedAmount` is `amount` converted from→to using the rate
 * available on `date`. When no rate exists for that pair/date, all fields
 * come back null/false.
 */
export interface ConvertFxResponse {
  convertedAmount: number | null;
  rate: number | null;
  hasRate: boolean;
}

/**
 * POST /fx-rates/refresh request body. Both fields are optional:
 *  - `date` defaults to today (UTC) on the server.
 *  - `currencyFilter` defaults to all currencies the user holds.
 *
 * The call is synchronous and can take up to ~10s while BNM responds.
 */
export interface RefreshBnmRatesRequest {
  /** ISO date string (yyyy-MM-dd) */
  date?: string;
  /** 3-letter ISO currency codes to refresh; omit for all held currencies. */
  currencyFilter?: string[];
}

/** POST /fx-rates/refresh response body. */
export interface RefreshBnmRatesResponse {
  fetched: number;
  inserted: number;
  updated: number;
  skipped: number;
}

/**
 * POST /fx-rates/backfill request body. Pulls official BNM rates for every
 * business day in `[from, to]`:
 *  - `from` (required) — yyyy-MM-dd inclusive start of the range.
 *  - `to` (optional)   — yyyy-MM-dd inclusive end; defaults to today (UTC)
 *    server-side when null/omitted.
 *
 * The backend rejects (400 ProblemDetails) a future start, an end before the
 * start, or a range wider than ~2 years. The call loops many days
 * server-side and can take up to a minute.
 */
export interface BackfillBnmRatesRequest {
  /** ISO date string (yyyy-MM-dd) */
  from: string;
  /** ISO date string (yyyy-MM-dd); omit/null to default to today. */
  to?: string | null;
}

/** POST /fx-rates/backfill response body. */
export interface BackfillBnmRatesResponse {
  /** Number of business days the backend iterated over. */
  daysProcessed: number;
  fetched: number;
  inserted: number;
  updated: number;
  skipped: number;
}

/**
 * Discriminator for `POST /accounts/{id}/balance-changes`.
 *
 *   - `Adjustment` → `value` is the NEW TOTAL balance; the backend writes a
 *     synthetic income/expense leg for `delta = value - currentBalance`
 *     (i.e. realized profit/loss). Rejected when the delta is 0.
 *   - `Investment` → `value` is a positive AMOUNT moved INTO the account
 *     (capital contribution). Writes an income leg of `+value`.
 *   - `Withdrawal` → `value` is a positive AMOUNT moved OUT of the account.
 *     Writes an expense leg of `-value`.
 */
export type BalanceChangeKind = 'Adjustment' | 'Investment' | 'Withdrawal';

/**
 * POST /accounts/{id}/balance-changes request body. `value` is implicitly in
 * the account's native currency; its meaning depends on `kind` (see
 * `BalanceChangeKind`).
 *
 * Backend rejects (400 ApiError) when:
 *   - account.type is not in {Brokerage, CryptoExchange, P2PLending, BankDeposit}
 *   - the resulting Adjustment delta is 0 (no change)
 *   - `value <= 0` for Investment/Withdrawal
 */
export interface BalanceChangeRequest {
  kind: BalanceChangeKind;
  /** New total balance (Adjustment) or amount moved (Investment/Withdrawal). */
  value: number;
  /** ISO date string (yyyy-MM-dd) */
  date: string;
  notes?: string;
}

export interface BalanceChangeResponse {
  transactionId: string;
  /** Positive = an income leg was written; negative = an expense leg. */
  delta: number;
}

/**
 * GET /dashboard/summary?month=YYYY-MM response.
 *
 * Income/expense totals are MDL-equivalents and exclude both transfers
 * (`isTransfer === true`) and balance adjustments (`isAdjustment === true`).
 * `missingFxRate` is true when any transaction in the window was omitted
 * from the aggregate because no convertible rate exists.
 */
export interface DashboardSummaryDto {
  /** "YYYY-MM" — defaults to the current UTC month on the backend. */
  month: string;
  income: number;
  expense: number;
  /** income - expense */
  net: number;
  /** 0..1 (or negative when net < 0); 0 when income == 0. */
  savingsRate: number;
  transactionCount: number;
  missingFxRate: boolean;
}

/**
 * Single point in the GET /dashboard/net-worth-trend?months=N response.
 *
 * Past months use end-of-month as their as-of date; the current month uses
 * "today" — so the last point in a 6-month series is a live "today" reading.
 */
export interface NetWorthTrendPointDto {
  /** "YYYY-MM" */
  month: string;
  netWorthMdl: number;
  missingFxRate: boolean;
}

/**
 * Status of a budget for a given month. Backend pre-computes the bucket
 * from `spent / monthlyLimit`:
 *   - `OnTrack` → < 80% spent
 *   - `Warning` → 80% – 100% spent
 *   - `Over`    → > 100% spent
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter` is
 * registered globally in `Program.cs`).
 */
export type BudgetStatus = 'OnTrack' | 'Warning' | 'Over';

/**
 * Mirrors MoneyManagement.Application.Features.Budgets.BudgetDto.
 * All money amounts are MDL (reporting currency) and `spent` aggregates
 * non-transfer/non-adjustment expense rows on the budget's category for
 * the (year, month) window. `remaining = monthlyLimit - spent` and may
 * be negative when overspent.
 */
export interface BudgetDto {
  id: string;
  categoryId: string;
  categoryName: string;
  monthlyLimit: number;
  spent: number;
  remaining: number;
  status: BudgetStatus;
  year: number;
  month: number;
}

export interface CreateBudgetRequest {
  categoryId: string;
  monthlyLimit: number;
}

export interface CreateBudgetResponse {
  id: string;
}

export interface UpdateBudgetLimitRequest {
  monthlyLimit: number;
}

/**
 * Status bucket of a savings goal — pre-computed server-side.
 *
 *   - `Achieved` → `saved >= targetAmount`
 *   - `OnTrack`  → pacing is fine for the (optional) target date
 *   - `AtRisk`   → pacing is borderline given the target date
 *   - `Behind`   → pacing is insufficient for the target date
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type GoalStatus = 'OnTrack' | 'AtRisk' | 'Achieved' | 'Behind';

/**
 * Mirrors MoneyManagement.Application.Features.Goals.GoalDto.
 *
 * A goal is either **linked** to an account (`linkedAccountId` set —
 * `saved` tracks the account's MDL-equivalent balance live) or **manual**
 * (`linkedAccountId` null — `saved` is set explicitly by the user via
 * the `manual-saved` endpoint).
 *
 * All money amounts are MDL (reporting currency). `progressPercent` is
 * the raw `saved / targetAmount` ratio and can exceed 1.0; consumers
 * cap it on display.
 */
export interface GoalDto {
  id: string;
  name: string;
  targetAmount: number;
  /** ISO date string (yyyy-MM-dd) or null when no target date is set. */
  targetDate: string | null;
  linkedAccountId: string | null;
  linkedAccountName: string | null;
  saved: number;
  /** `max(0, targetAmount - saved)`. */
  remaining: number;
  /** Raw `saved / targetAmount`; can be > 1.0. */
  progressPercent: number;
  status: GoalStatus;
  /**
   * Amount the user should contribute monthly between now and the target
   * date to reach the goal. `null` when no target date is set, or when the
   * goal is already achieved.
   */
  requiredMonthlyContribution: number | null;
  isLinkedMode: boolean;
  /**
   * True when the linked account's balance could not be FX-converted to
   * MDL for the latest applicable date — the `saved` figure is best-effort
   * in that case.
   */
  missingFxRate: boolean;
}

export interface CreateGoalRequest {
  name: string;
  targetAmount: number;
  /** ISO date string (yyyy-MM-dd); omitted/absent means no target date. */
  targetDate?: string;
  /** When set, switches the goal into linked mode; omit for manual mode. */
  linkedAccountId?: string;
}

export interface CreateGoalResponse {
  id: string;
}

/** Same shape as create; PUT /goals/{id} fully replaces the goal. */
export type UpdateGoalRequest = CreateGoalRequest;

/** PATCH /goals/{id}/manual-saved body. Rejected if the goal is linked. */
export interface UpdateManualSavedRequest {
  amount: number;
}

// TODO: regenerate via `npm run gen:api` once GET /goals/{id} lands in OpenAPI.

/**
 * Origin of a contribution row inside a goal's history.
 *  - `Manual` — a hand-entered contribution against a manual-mode goal.
 *  - `LinkedAccountTransaction` — a derived row materialized from the linked
 *    account's transaction ledger (no stable id; the dto exposes `id: null`).
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type GoalContributionSource = 'Manual' | 'LinkedAccountTransaction';

/**
 * Single entry of a goal's contribution history. `amount` is signed —
 * positive for a contribution, negative for a withdrawal. Linked-mode
 * rows have `id === null` because they're projected on the fly from the
 * underlying account transactions.
 */
export interface GoalContributionDto {
  id: string | null;
  amount: number;
  /** ISO date string (yyyy-MM-dd) */
  occurredOn: string;
  notes: string | null;
  source: GoalContributionSource;
}

/**
 * Cumulative-MDL-saved point in a goal's saved-over-time series.
 * Series is monthly cadence, ascending by `asOf`.
 */
export interface GoalSavedPointDto {
  /** ISO date string (yyyy-MM-dd) */
  asOf: string;
  /** Running cumulative MDL saved at `asOf`. */
  saved: number;
}

/**
 * Pace statistics derived from the last 90 days of contributions.
 *
 * Any of the three fields may be `null`:
 *   - `avgMonthlyContribution` is null when the history window is too short.
 *   - `projectedCompletionDate` is null when the avg is null/≤ 0 OR the goal
 *     is already achieved.
 *   - `monthsToAchieveAtPace` mirrors the projection — null whenever a
 *     date can't be computed.
 */
export interface GoalPaceStatsDto {
  avgMonthlyContribution: number | null;
  /** ISO date string (yyyy-MM-dd) or null. */
  projectedCompletionDate: string | null;
  monthsToAchieveAtPace: number | null;
}

/**
 * GET /goals/{id} — per-goal detail view used by the /goals/{id} page.
 *
 * Extends the list-row shape (`GoalDto` fields) with two extra arrays —
 * a saved-over-time series and a contribution history — plus the pace
 * roll-up and `createdOn`/`isArchived` metadata. All money fields are
 * MDL (v1 goals are MDL-only).
 */
export interface GoalDetailDto {
  id: string;
  name: string;
  targetAmount: number;
  /** ISO date string (yyyy-MM-dd) or null when no target date is set. */
  targetDate: string | null;
  linkedAccountId: string | null;
  linkedAccountName: string | null;
  saved: number;
  /** `max(0, targetAmount - saved)`. */
  remaining: number;
  /** Raw `saved / targetAmount`; can be > 1.0. */
  progressPercent: number;
  status: GoalStatus;
  /** MDL/month to hit `targetDate`; null when no target date OR achieved. */
  requiredMonthlyContribution: number | null;
  isLinkedMode: boolean;
  /**
   * True when the linked account's balance could not be FX-converted to
   * MDL for some reading — same flag the list endpoint surfaces.
   */
  missingFxRate: boolean;
  /** ISO date string (yyyy-MM-dd) — when the goal was created. */
  createdOn: string;
  isArchived: boolean;
  pace: GoalPaceStatsDto;
  /** Descending by `occurredOn`. */
  contributions: GoalContributionDto[];
  /** Ascending by `asOf`, monthly cadence. */
  savedHistory: GoalSavedPointDto[];
}

// TODO: regenerate via `npm run gen:api` once /reports endpoints land in OpenAPI.

/**
 * GET /reports/monthly-summary?from=YYYY-MM&to=YYYY-MM response row.
 *
 * Defaults to a trailing-12-month window when both `from` and `to` are
 * omitted. Income/expense are MDL-equivalents excluding transfers and
 * balance adjustments. Series is oldest-first.
 */
export interface MonthlySummaryReportRow {
  /** "YYYY-MM" */
  month: string;
  income: number;
  expense: number;
  /** `income - expense` */
  net: number;
  /** 0..1 (or negative when net < 0); 0 when income == 0. */
  savingsRate: number;
  transactionCount: number;
  missingFxRate: boolean;
}

export type ReportDirection = 'Expense' | 'Income';

/**
 * Single bucket inside a `/reports/category-breakdown` response. The
 * `categoryId` is `null` for the synthetic Uncategorized bucket that
 * groups rows the user never categorized.
 */
export interface CategoryBreakdownItem {
  categoryId: string | null;
  categoryName: string;
  amountMdl: number;
  /** 0..1 — share of `totalMdl`. */
  percentage: number;
  transactionCount: number;
}

/**
 * GET /reports/category-breakdown?from=YYYY-MM-DD&to=YYYY-MM-DD
 *      &direction=Expense|Income
 *
 * `items` is pre-sorted descending by `amountMdl`. `missingFxRate` is
 * true if any in-window row was excluded because no convertible rate
 * exists.
 */
export interface CategoryBreakdownDto {
  /** ISO date string (yyyy-MM-dd) — echoes the request */
  from: string;
  /** ISO date string (yyyy-MM-dd) — echoes the request */
  to: string;
  direction: ReportDirection;
  totalMdl: number;
  missingFxRate: boolean;
  items: CategoryBreakdownItem[];
}

/**
 * Single row of `GET /reports/top-payees`. `payee` is the cleaned-up
 * display name (e.g. "Linella"), `originalDescription` is the raw
 * statement memo we matched on — surface the latter to keep the user
 * confident in the grouping.
 */
export interface TopPayeeReportRow {
  payee: string;
  originalDescription: string;
  amountMdl: number;
  transactionCount: number;
}

/**
 * Single point in `GET /reports/balance-over-time`. `balance` is the
 * account's native-currency balance at `asOf`; `balanceMdl` is the MDL
 * conversion or `null` when no FX rate exists for that date.
 */
export interface BalanceOverTimePoint {
  /** ISO date string (yyyy-MM-dd) */
  asOf: string;
  balance: number;
  balanceMdl: number | null;
  missingFxRate: boolean;
}

export type BalanceOverTimeInterval = 'Daily' | 'Weekly' | 'Monthly';

// TODO: regenerate via `npm run gen:api` once /accounts/{id} detail endpoint
// lands in OpenAPI.

/**
 * Income / withdrawal / P&L roll-up for one observation window
 * (year-to-date OR all-time) of an account, used by the per-account
 * detail page's Performance card.
 *
 * All money fields are MDL-equivalents — the DTO is intentionally
 * multi-currency-aware. For an MDL account the totals match the native
 * ledger by construction (FX identity short-circuit).
 *
 *   - `contributionsMdl` — Σ of incoming transfer legs in MDL.
 *   - `withdrawalsMdl`   — Σ of outgoing transfer legs in MDL.
 *   - `netPnLMdl`        — Σ of balance-adjustment legs in MDL
 *                          (positive = gain, negative = loss).
 *   - `*Count` fields    — row counts behind each total.
 *   - `missingFxRate`    — true when at least one row was excluded
 *                          from the aggregate because no convertible
 *                          rate exists for its date.
 */
export interface AccountActivityTotalsDto {
  contributionsMdl: number;
  withdrawalsMdl: number;
  netPnLMdl: number;
  contributionCount: number;
  withdrawalCount: number;
  adjustmentCount: number;
  missingFxRate: boolean;
}

/**
 * GET /accounts/{id} — per-account detail view. Extends `AccountDto` with
 * an `initialCapital` anchor + `allTime`/`yearToDate` activity totals +
 * a couple of meta fields used by the Activity section.
 *
 * `initialCapital` is the account's native-currency anchor at the opening
 * date — not window-scoped. `firstActivityDate`/`lastActivityDate` reflect
 * the earliest/latest non-transfer non-adjustment transaction (or null
 * when the account has no real activity yet).
 */
export interface AccountDetailDto {
  id: string;
  name: string;
  type: AccountType;
  /** ISO 4217 currency code. */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  openingDate: string;
  isArchived: boolean;
  notes: string | null;
  /** Live native-currency balance — see AccountDto.balance for semantics. */
  balance: number;
  /** Live MDL-equivalent balance, or null when no FX rate exists. */
  balanceMdl: number | null;
  /** Native-currency anchor at opening date. */
  initialCapital: number;
  allTime: AccountActivityTotalsDto;
  yearToDate: AccountActivityTotalsDto;
  /** ISO date string (yyyy-MM-dd) or null when no real activity. */
  firstActivityDate: string | null;
  /** ISO date string (yyyy-MM-dd) or null when no real activity. */
  lastActivityDate: string | null;
  /** Count of non-transfer non-adjustment transactions on this account. */
  realActivityCount: number;
}

/**
 * Per-table row counts returned by POST /data/import after a successful
 * restore. Mirrors the backend's import-result DTO.
 *
 * The UI renders these defensively — it iterates the numeric entries of the
 * response object rather than hard-coding each field — so minor naming
 * differences from the backend won't break the summary. `schemaVersion`
 * identifies the backup format and is intentionally separated from the
 * row counts in the UI.
 */
export interface ImportDataResponse {
  /** Backup schema version the file was written with. */
  schemaVersion: number;
  accounts: number;
  categories: number;
  transactions: number;
  importBatches: number;
  budgets: number;
  budgetPeriods: number;
  savingsGoals: number;
  savingsGoalContributions: number;
}
