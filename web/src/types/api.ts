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
  /**
   * True when a NON-archived pool sits on this account — outside investors
   * hold a share of the money in it.
   *
   * `balance` / `balanceMdl` stay the account's FULL value regardless: the
   * account really does hold the participants' money, and that read-side
   * divergence was decided explicitly. Only the net-worth card, the net-worth
   * trend and the account-detail Performance card apply the owner's share.
   * This flag is what lets the UI say which of the two a given number is.
   */
  isPooled: boolean;
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
  /**
   * Opening-balance reconciliation (Phase 1). Compares the statement's printed
   * "Sold inițial" against the app's own computed balance for the account as of
   * the day before the statement period. Surfaced as a non-blocking warning when
   * `openingMatches` is false (e.g. a month-boundary gap where a last-day purchase
   * settled into the next month leaves the user missing transactions before this
   * period). May be absent on older previews / banks that don't expose an opening
   * balance, so always guard `preview.reconciliation` before reading it.
   */
  reconciliation?: {
    /** Statement's printed opening balance ("Sold inițial"). */
    statementOpeningBalance: number;
    /** App's computed balance just before the statement period. */
    appBalanceBeforeStatement: number;
    /** statementOpeningBalance − appBalanceBeforeStatement (can be negative). */
    openingDelta: number;
    /** true when |openingDelta| <= 0.01. */
    openingMatches: boolean;
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
 * GET /dashboard/net-worth — the headline figures behind the three
 * dashboard tiles (Total assets / I owe / Net worth).
 *
 * Computed server-side because a client-side sum of account balances
 * silently ignores personal loans: money the user borrowed still sits in
 * an account (inflating gross assets) even though it is owed back.
 *
 *   netWorthMdl = grossAssetsMdl - externalLiabilitiesMdl + externalAssetsMdl
 *
 * Both loan legs are reported as POSITIVE magnitudes — the sign lives in
 * the formula above, not in the values. Amounts that could not be
 * FX-converted contribute 0 and are counted in `accountsMissingFxRate` /
 * `loansMissingFxRate`, so the UI can flag *which* total is incomplete.
 */
export interface NetWorthDto {
  /** Sum of non-archived account balances, in MDL. */
  grossAssetsMdl: number;
  /** Sum of outstanding on loans the user borrowed ("I owe"), positive, in MDL. */
  externalLiabilitiesMdl: number;
  /** Sum of outstanding on loans the user lent out ("owed to me"), positive, in MDL. */
  externalAssetsMdl: number;
  /** `grossAssetsMdl - externalLiabilitiesMdl + externalAssetsMdl`. May be negative. */
  netWorthMdl: number;
  /** Count of accounts whose balance could not be converted to MDL. */
  accountsMissingFxRate: number;
  /** Count of loans whose outstanding could not be converted to MDL. */
  loansMissingFxRate: number;
  /**
   * Other people's money sitting inside the user's non-archived accounts, as
   * a POSITIVE magnitude in MDL — pooled capital held on someone else's
   * behalf.
   *
   * Deliberately OUTSIDE the net-worth identity: `netWorthMdl` stays exactly
   * `grossAssetsMdl - externalLiabilitiesMdl + externalAssetsMdl`, and this
   * figure is ALREADY excluded from `grossAssetsMdl`. Adding or subtracting it
   * anywhere in that equation double-counts. It exists so the UI can explain
   * the gap between an account's displayed balance and its contribution to
   * net worth.
   *
   * Optional on the client because pre-pool backends (and a few older test
   * fixtures) omit it; treat a missing value as 0.
   */
  outsideCapitalMdl?: number;
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
  /**
   * True when a NON-archived pool sits on this account. See
   * `AccountDto.isPooled` — `balance`/`balanceMdl` remain gross, while
   * `allTime`/`yearToDate` below are already the OWNER's share only.
   */
  isPooled: boolean;
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

// TODO: regenerate via `npm run gen:api` once the /loans endpoints land in OpenAPI.

/**
 * Direction of a personal loan, from the user's point of view.
 *  - `Received` → money the user borrowed (they owe it back). UI label: "Borrowed".
 *  - `Given`    → money the user lent out (it's owed back to them). UI label: "Lent".
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type LoanDirection = 'Given' | 'Received';

/**
 * Lifecycle of a loan — pre-computed server-side.
 *  - `Active`  → `outstanding > 0`.
 *  - `Settled` → fully repaid (`outstanding === 0`).
 *
 * Serialized as a string by the backend (`JsonStringEnumConverter`).
 */
export type LoanStatus = 'Active' | 'Settled';

/**
 * Mirrors the backend LoanDto (list endpoint row).
 *
 * v1 loans are interest-free and same-currency only: `principal`,
 * `totalRepaid`, and `outstanding` are all in `currency`.
 * `outstanding = principal - totalRepaid`; the loan flips to `Settled`
 * when it reaches 0. Archived loans are excluded from the list unless it
 * is fetched with `includeArchived=true`, and stay reachable via
 * GET /loans/{id}.
 */
export interface LoanDto {
  id: string;
  direction: LoanDirection;
  /** Who the money moved to/from (e.g. "Parents"). */
  counterparty: string;
  /** Original amount lent/borrowed, in `currency`. */
  principal: number;
  /** ISO 4217 currency code (e.g. "MDL", "EUR"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) — when the money changed hands. */
  loanDate: string;
  /** Σ of recorded payments, in `currency`. */
  totalRepaid: number;
  /** `principal - totalRepaid`, in `currency`. */
  outstanding: number;
  /**
   * `outstanding` expressed in MDL using the FX rate available for
   * `currency` on the latest applicable date. `null` if no rate exists.
   * For MDL loans this is the identity case and equals `outstanding`.
   */
  outstandingMdl: number | null;
  /** True when `outstandingMdl` could not be computed for lack of an FX rate. */
  missingFxRate: boolean;
  status: LoanStatus;
  paymentCount: number;
  /**
   * True when the loan's disbursement was recorded against a real account,
   * i.e. the principal actually moved through a tracked balance.
   *
   * Load-bearing for net worth: `GET /dashboard/net-worth` only counts
   * account-linked loans. An unlinked loan's cash never entered gross
   * assets, so subtracting the obligation would push the total wrong in
   * the opposite direction.
   */
  isAccountLinked: boolean;
  notes: string | null;
  /**
   * Soft-delete flag. Archived rows only appear in the list when it is
   * fetched with `includeArchived=true`; unarchiving (POST
   * /loans/{id}/unarchive) flips this back without moving any money.
   */
  isArchived: boolean;
}

/**
 * Single repayment against a loan. When the payment was recorded against
 * an account, the backend also wrote a real (transfer-flagged) transaction
 * on it — `transactionId`/`accountId`/`accountName` are non-null in that
 * case, and deleting the payment also deletes that transaction.
 */
export interface LoanPaymentDto {
  id: string;
  /** Positive amount, in the loan's currency. */
  amount: number;
  /** ISO 4217 currency code — always equals the loan's currency in v1. */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  occurredOn: string;
  /** Linked transaction id, or null when the payment isn't tracked in an account. */
  transactionId: string | null;
  accountId: string | null;
  accountName: string | null;
  notes: string | null;
}

/**
 * GET /loans/{id} — per-loan detail view used by the /loans/{id} page.
 *
 * Extends the list-row shape (`LoanDto` fields) with archive/creation
 * metadata, the optional disbursement transaction link (set when the
 * original loan amount was recorded against an account), and the payment
 * history (descending by `occurredOn`).
 */
export interface LoanDetailDto {
  id: string;
  direction: LoanDirection;
  counterparty: string;
  principal: number;
  /** ISO 4217 currency code (e.g. "MDL", "EUR"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  loanDate: string;
  totalRepaid: number;
  outstanding: number;
  /** MDL-equivalent of `outstanding`, or null when no FX rate exists. */
  outstandingMdl: number | null;
  missingFxRate: boolean;
  status: LoanStatus;
  paymentCount: number;
  notes: string | null;
  isArchived: boolean;
  /** ISO date string (yyyy-MM-dd) — when the loan was created in the app. */
  createdOn: string;
  /** Transaction written for the original disbursement, or null when untracked. */
  disbursementTransactionId: string | null;
  disbursementAccountId: string | null;
  disbursementAccountName: string | null;
  /** Descending by `occurredOn`. */
  payments: LoanPaymentDto[];
}

/**
 * POST /loans request body. `accountId` links the disbursement to an
 * account — the backend then writes a real transfer-flagged transaction
 * on it (so the movement never pollutes income/expense stats). Omit or
 * send null when the money isn't tracked in any account. The account's
 * currency must equal `currency` (v1 is same-currency only).
 */
export interface CreateLoanRequest {
  direction: LoanDirection;
  counterparty: string;
  principal: number;
  /** ISO 4217 currency code (e.g. "MDL", "EUR"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  loanDate: string;
  accountId?: string | null;
  notes?: string | null;
}

export interface CreateLoanResponse {
  id: string;
}

/**
 * PUT /loans/{id} request body. Edits only the user-mutable metadata —
 * `counterparty` and `notes` (`null` to clear). Principal, currency,
 * direction, and loan date are fixed at creation. Returns 204.
 */
export interface UpdateLoanRequest {
  counterparty: string;
  notes?: string | null;
}

/**
 * POST /loans/{id}/payments request body. `accountId` mirrors the create
 * contract: when set, the backend also writes a transfer-flagged
 * transaction on that account (same-currency only); null/omitted means
 * the repayment isn't tracked in any account. The server re-validates
 * that `amount` doesn't exceed the loan's outstanding balance.
 */
export interface RecordLoanPaymentRequest {
  amount: number;
  /** ISO date string (yyyy-MM-dd) */
  occurredOn: string;
  accountId?: string | null;
  notes?: string | null;
}

export interface RecordLoanPaymentResponse {
  id: string;
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

// TODO: regenerate via `npm run gen:api` once the /pools endpoints land in OpenAPI.

/**
 * What a pool unit event did to a participant's unit balance, and whether real
 * cash moved alongside it. Serialized as a string by the backend
 * (`JsonStringEnumConverter`).
 *
 *  - `Seed`         → bootstrap. The owner's EXISTING account balance becomes
 *                     units at a NAV of 1. No cash, no transaction, one per pool.
 *  - `Subscription` → cash in; mints units at the prevailing NAV.
 *  - `Redemption`   → cash out; burns units at the prevailing NAV.
 *  - `Distribution` → profit payout. TWO-PHASE: the month closes (units burn)
 *                     on `occurredOn`, the transfer physically leaves later.
 *                     The only kind allowed to carry cash with a null `settledOn`.
 *  - `CostShare`    → a participant's share of a pool cost the owner paid out of
 *                     pocket. Pure unit transfer, no cash leg.
 *  - `CostRecovery` → the owner side of that same transfer.
 *
 * Subscriptions and redemptions are NAV-invariant by construction: struck at
 * NAV, they leave the per-unit value unchanged for everyone else.
 */
export type PoolUnitEventKind =
  | 'Seed'
  | 'Subscription'
  | 'Redemption'
  | 'Distribution'
  | 'CostShare'
  | 'CostRecovery';

/**
 * Mirrors the backend PoolDto (the `/pools` list row).
 *
 * Every native monetary field is in the pool's own `currency`, which always
 * equals the account's — the pool never does FX. `poolValueMdl` and
 * `outsideCapitalMdl` are the reporting-currency conversion at TODAY's rate,
 * `null` with `missingFxRate` flipped when no usable rate exists. Same contract
 * as `AccountDto.balanceMdl`: never a silent zero, never an implicit 1:1.
 */
export interface PoolDto {
  id: string;
  /** The account the capital physically sits in. */
  accountId: string;
  /** That account's name; an archived account still labels its pool. */
  accountName: string;
  name: string;
  /** ISO 4217 currency code (e.g. "USD"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) — the day the pool started; the seed is dated here. */
  inceptionDate: string;
  notes: string | null;
  isArchived: boolean;
  /** The account's DERIVED balance today, gross of anything owed out. */
  accountBalance: number;
  /**
   * Closed-but-unpaid distributions still sitting in the account. Subtracted
   * from `accountBalance` to get `poolValue` — skip that and the money counts
   * as pool value a second time and the friends are paid twice on it.
   */
  unpaidDistributionCash: number;
  unpaidDistributionCount: number;
  /** `accountBalance - unpaidDistributionCash`. What units are priced against. */
  poolValue: number;
  /** MDL-equivalent of `poolValue`, or null when no FX rate exists. */
  poolValueMdl: number | null;
  /** Units outstanding across every participant, archived included. */
  totalUnits: number;
  /** `poolValue / totalUnits`, or null when no units are outstanding — never a placeholder. */
  navPerUnit: number | null;
  /** Non-archived participants, the owner included. */
  participantCount: number;
  /** The user's share of the pool, in [0, 1]. */
  ownerFraction: number;
  /** Sum of the non-owner stakes: other people's money, POSITIVE, in `currency`. */
  outsideCapital: number;
  /** MDL-equivalent of `outsideCapital`, or null when no FX rate exists. */
  outsideCapitalMdl: number | null;
  /** True when any MDL field above could not be valued. */
  missingFxRate: boolean;
}

/**
 * One holder's position in the pool, valued at `PoolDetailDto.asOf`.
 *
 * The UI speaks "share" and percentages — `units` is audit vocabulary and
 * belongs in the event ledger, not here.
 */
export interface PoolParticipantDto {
  id: string;
  name: string;
  isOwner: boolean;
  isArchived: boolean;
  /** ISO date string (yyyy-MM-dd) */
  joinedOn: string;
  /** Sum of their signed unit deltas. */
  units: number;
  /** `units / totalUnits * 100`, to 6dp. Zero when the pool holds no units. */
  ownershipPercent: number;
  /** `units * navPerUnit`, to the cent — null when NAV is undefined. */
  stake: number | null;
  /** MDL-equivalent of `stake`, or null when no FX rate exists. */
  stakeMdl: number | null;
  /**
   * `sum(subscription cash) - sum(redemption cash)`. Distributions never move
   * it, which IS the high-water mark — no stored field, no reset logic.
   */
  capitalBase: number;
  /**
   * `max(0, stake - max(0, capitalBase))`. **Zero is a real answer**: a green
   * month after a drawdown correctly pays nothing while the holder is still at
   * or below their capital base.
   *
   * Read the OWNER's value with care — a seed carries no cash, so the owner's
   * capital base is 0 and their "distributable" is their whole stake. That is
   * the formula being honest, not an amount to pay out; the owner's profit
   * stays in and grows their share.
   */
  distributable: number | null;
  /** Closed-but-unpaid payouts owed to this participant. */
  unpaidDistributionCash: number;
  unpaidDistributionCount: number;
  /** True when `stakeMdl` could not be valued. */
  missingFxRate: boolean;
}

/** One row of the pool's ledger. */
export interface PoolUnitEventDto {
  id: string;
  participantId: string;
  participantName: string;
  kind: PoolUnitEventKind;
  /** ISO date string (yyyy-MM-dd) */
  occurredOn: string;
  /** Positive magnitude; `kind` carries the direction. */
  units: number;
  /** The signed effect on the participant's balance. */
  unitsDelta: number;
  /** The price the units were struck at. AUDIT ONLY — never enters the ownership fraction. */
  navPerUnit: number;
  /** The pool's total value immediately before this event. Zero for a seed. */
  poolValuePreMoney: number;
  /** Money that actually moved; null for Seed, CostShare and CostRecovery. */
  cash: number | null;
  cashCurrency: string | null;
  /** ISO date string (yyyy-MM-dd) — null on an unpaid distribution, and only there. */
  settledOn: string | null;
  /** A distribution that has closed but not yet been paid out. */
  isUnpaid: boolean;
  movementTransactionId: string | null;
  movementAccountId: string | null;
  movementAccountName: string | null;
  notes: string | null;
}

/** A transaction on the pool account with no unit event behind it. */
export interface UnmatchedPoolTransactionDto {
  transactionId: string;
  /** ISO date string (yyyy-MM-dd) */
  transactionDate: string;
  description: string;
  direction: TransactionDirection;
  amount: number;
  currency: string;
  isTransfer: boolean;
}

/** An event priced against a pool value the ledger can no longer reproduce. */
export interface PoolValueDriftDto {
  eventId: string;
  /** ISO date string (yyyy-MM-dd) */
  occurredOn: string;
  kind: PoolUnitEventKind;
  recordedPreMoney: number;
  derivedPreMoney: number;
  /** `recordedPreMoney - derivedPreMoney`. */
  drift: number;
}

/** A unit event claiming cash the account never saw. */
export interface UnbackedPoolCashClaimDto {
  eventId: string;
  /** ISO date string (yyyy-MM-dd) */
  settledOn: string;
  direction: TransactionDirection;
  amount: number;
}

/**
 * The pool's ledger replayed against reality. **Reports; never corrects.**
 * Everything here is recoverable by hand and unrecoverable if silently
 * "fixed" — a value drift means somebody edited history after units were
 * priced, and the right answer depends on which of the two records is wrong.
 */
export interface PoolReconciliationDto {
  /** True when all four checks pass. The only field a badge needs. */
  isClean: boolean;
  /** Money that moved on the pool account with no unit event to account for it. */
  unmatchedTransactions: UnmatchedPoolTransactionDto[];
  /** Sum of units across the roster. */
  participantUnits: number;
  /** Sum of unit deltas across the ledger. */
  ledgerUnits: number;
  /** `participantUnits - ledgerUnits`. */
  unitsDrift: number;
  /** Whether the two agree to within dust. */
  unitsBalance: boolean;
  valueDrifts: PoolValueDriftDto[];
  unbackedCashClaims: UnbackedPoolCashClaimDto[];
}

/**
 * GET /pools/{id} — the drill-down projection. Carries the same valuation
 * surface as `PoolDto` plus the roster, the full ledger newest-first, the
 * mark-staleness warning and the reconciliation tripwire.
 *
 * Reachable for ARCHIVED pools, the same rule the loan and goal detail pages
 * follow.
 */
export interface PoolDetailDto {
  id: string;
  accountId: string;
  accountName: string;
  accountCurrency: string;
  accountIsArchived: boolean;
  name: string;
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  inceptionDate: string;
  notes: string | null;
  isArchived: boolean;
  /** ISO 8601 timestamp — when the pool was created in the app. */
  createdOn: string;
  /** ISO date string (yyyy-MM-dd) — the date every figure below is evaluated at. */
  asOf: string;
  accountBalance: number;
  unpaidDistributionCash: number;
  unpaidDistributionCount: number;
  poolValue: number;
  poolValueMdl: number | null;
  totalUnits: number;
  navPerUnit: number | null;
  /** The user's own participant row. Null only on a corrupt pool. */
  ownerParticipantId: string | null;
  ownerFraction: number;
  outsideCapital: number;
  outsideCapitalMdl: number | null;
  missingFxRate: boolean;
  /**
   * ISO date string (yyyy-MM-dd) — **the last time the pool's value was
   * CONFIRMED**, or null when it never has been.
   *
   * A NAV struck against a stale mark is the main way this model goes quietly
   * wrong: every subscription, redemption and distribution is priced off the
   * account's derived balance, and if that balance is weeks old in an account
   * that moves, units are minted or burned at the wrong price and the error is
   * permanent.
   */
  lastMarkDate: string | null;
  /** Days between `lastMarkDate` and `asOf`; null when there is no mark. */
  markAgeDays: number | null;
  /** The roster, owner first. Archived participants included — they are part of the unit total. */
  participants: PoolParticipantDto[];
  /** The full ledger, NEWEST first. */
  events: PoolUnitEventDto[];
  reconciliation: PoolReconciliationDto;
}

/**
 * POST /pools request body.
 *
 * `currency` must equal the account's, and the account's type must be one that
 * can take a balance adjustment (Brokerage / CryptoExchange / P2PLending /
 * BankDeposit) — an account that can never be re-priced would freeze the pool's
 * NAV at inception.
 *
 * `poolValueAtInception` is the exchange's REAL total on the inception date.
 * Supplying it writes a catch-up mark before the owner is seeded, so the seed
 * is struck against the truth rather than against a stale derived balance.
 */
export interface CreatePoolRequest {
  accountId: string;
  name: string;
  /** ISO 4217 currency code (e.g. "USD"). */
  currency: string;
  /** ISO date string (yyyy-MM-dd) */
  inceptionDate: string;
  /** Display name for the user's own participant row (e.g. "Me"). */
  ownerName: string;
  poolValueAtInception?: number | null;
  notes?: string | null;
  /** Subscriptions that already happened, replayed at the NAV of their own day. */
  backdatedSubscriptions?: BackdatedSubscriptionRequest[] | null;
}

/**
 * One participant's already-completed subscription, replayed into the ledger at
 * the NAV that applied on the day their money actually arrived.
 *
 * `poolValuePreMoney` is REQUIRED and deliberately not defaulted: the account's
 * derived balance on that date already contains their cash, so guessing would
 * price them at their own money.
 */
export interface BackdatedSubscriptionRequest {
  participantName: string;
  /** ISO date string (yyyy-MM-dd) — between inception and today. */
  occurredOn: string;
  cash: number;
  poolValuePreMoney: number;
  /** True when the arrival was never recorded on the account and needs a row written. */
  writeMovementTransaction?: boolean;
  notes?: string | null;
}

export interface CreatedPoolParticipant {
  id: string;
  name: string;
  isOwner: boolean;
  units: number;
}

export interface CreatePoolResponse {
  id: string;
  ownerParticipantId: string;
  seedUnits: number;
  /** Signed catch-up applied to the account, or 0 when none was needed. */
  markDelta: number;
  markTransactionId: string | null;
  participants: CreatedPoolParticipant[];
}

/** POST /pools/{id}/participants request body. */
export interface AddPoolParticipantRequest {
  name: string;
  /** ISO date string (yyyy-MM-dd) */
  joinedOn: string;
}

export interface AddPoolParticipantResponse {
  id: string;
}

/**
 * POST /pools/{id}/subscriptions request body.
 *
 * `poolValueNow` is the exchange's REAL total across every sub-wallet,
 * **including BNB**, read immediately before the money crosses the boundary.
 * It is a hard requirement, not a convenience: the handler marks the account to
 * it BEFORE striking the NAV, and a subscription priced off a stale balance
 * silently transfers value between the owner and the participants — permanently.
 */
export interface RecordSubscriptionRequest {
  participantId: string;
  poolValueNow: number;
  cash: number;
  notes?: string | null;
}

export interface RecordSubscriptionResponse {
  eventId: string;
  units: number;
  navPerUnit: number;
  poolValuePreMoney: number;
  markDelta: number;
  markTransactionId: string | null;
  movementTransactionId: string;
}

/**
 * POST /pools/{id}/redemptions request body. `destinationAccountId` writes the
 * receiving leg of a two-leg transfer (e.g. Binance to Bybit); omit it when the
 * money leaves for somewhere the app does not track.
 */
export interface RecordRedemptionRequest {
  participantId: string;
  poolValueNow: number;
  cash: number;
  destinationAccountId?: string | null;
  notes?: string | null;
  /**
   * "Yes, this really is the whole pool." Opt-in acknowledgement that relaxes
   * the guard which normally rejects cash equal to the account's balance —
   * that guard exists because the demonstrated slip is a BALANCE typed into an
   * AMOUNT field, but the last redemption of a pool genuinely *is* the whole
   * pool value, so a wind-down has no other path through the API.
   *
   * **Not a bypass switch.** The backend refutes the claim: if any shares would
   * remain outstanding afterwards the command fails. Defaults to false.
   */
  isFullWindDown?: boolean;
}

export interface RecordRedemptionResponse {
  eventId: string;
  units: number;
  navPerUnit: number;
  poolValuePreMoney: number;
  markDelta: number;
  markTransactionId: string | null;
  movementTransactionId: string;
  counterTransactionId: string | null;
}

/** One line of a monthly close. Omit `cash` to pay the participant's full distributable. */
export interface DistributionPayoutRequest {
  participantId: string;
  cash?: number | null;
}

/**
 * POST /pools/{id}/distributions request body — **closes the month**.
 *
 * Closing marks the pool, retires units at that NAV and records what is owed.
 * **No money moves.** The transfer is a separate, later step
 * (`SettleDistributionRequest`) dated the day the cash actually leaves — date
 * the payout at the close and the next snapshot still contains that cash, so
 * the app re-attributes it as fresh profit and pays the participants twice on
 * it.
 *
 * Omit `payouts` entirely to pay every non-owner their full distributable.
 */
export interface CloseDistributionRequest {
  poolValueNow: number;
  payouts?: DistributionPayoutRequest[] | null;
  notes?: string | null;
}

export interface DistributionLine {
  eventId: string;
  participantId: string;
  participantName: string;
  units: number;
  cash: number;
}

export interface CloseDistributionResponse {
  navPerUnit: number;
  poolValuePreMoney: number;
  markDelta: number;
  markTransactionId: string | null;
  /** Sum of every line — owed, not yet paid. */
  totalCash: number;
  lines: DistributionLine[];
}

/**
 * POST /pools/{id}/distributions/{eventId}/settle request body. Writes the real
 * transaction on the day the transfer physically settled; `settledOn` defaults
 * to today when omitted.
 */
export interface SettleDistributionRequest {
  /** ISO date string (yyyy-MM-dd) */
  settledOn?: string | null;
  notes?: string | null;
}

export interface SettleDistributionResponse {
  transactionId: string;
  /** ISO date string (yyyy-MM-dd) */
  settledOn: string;
  cash: number;
}

/**
 * POST /pools/{id}/cost-reimbursements request body.
 *
 * A cost the OWNER paid out of pocket (a server bill, say) that the
 * participants reimburse pro-rata. The money never enters the account, so this
 * mints no value: it is a PURE UNIT TRANSFER from the participants to the
 * owner at the prevailing NAV. Total units, pool value and NAV are all
 * unchanged.
 *
 * `poolValueNow` is optional here — and only here — because no cash crosses the
 * boundary. Supplying it re-marks the account first so the transfer is struck
 * at a fresh NAV, which is still the better answer.
 */
export interface RecordCostReimbursementRequest {
  /** The full cost, in the pool's currency. Participants bear their share of it. */
  amount: number;
  poolValueNow?: number | null;
  notes?: string | null;
}

export interface CostShareLine {
  eventId: string;
  participantId: string;
  participantName: string;
  units: number;
  amount: number;
}

export interface RecordCostReimbursementResponse {
  navPerUnit: number;
  totalUnitsTransferred: number;
  totalAmountRecovered: number;
  markDelta: number;
  markTransactionId: string | null;
  ownerEventId: string;
  lines: CostShareLine[];
}
