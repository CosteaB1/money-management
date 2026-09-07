import { HttpResponse, http } from 'msw';
import type {
  AccountDetailDto,
  AccountDto,
  BudgetDto,
  CategoryDto,
  CategoryPatternDto,
  FxRateDto,
  GoalDetailDto,
  GoalDto,
  LoanDetailDto,
  LoanDto,
  NetWorthDto,
  PagedResult,
  PoolDetailDto,
  PoolDto,
  StatementPreviewDto,
  TransactionDto,
} from '@/src/types/api';

const accounts: AccountDto[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Cash Wallet',
    type: 'Cash',
    currency: 'MDL',
    openingDate: '2025-01-01',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: 500,
    balanceMdl: 500,
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    name: 'BRD Visa',
    type: 'CreditCard',
    currency: 'MDL',
    openingDate: '2024-06-15',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: -1200,
    balanceMdl: -1200,
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'ING Savings',
    type: 'BankDeposit',
    currency: 'MDL',
    openingDate: '2024-03-01',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: 22550,
    balanceMdl: 22550,
  },
  {
    id: '44444444-4444-4444-4444-444444444444',
    name: 'XTB',
    type: 'Brokerage',
    currency: 'USD',
    openingDate: '2024-09-10',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: 1500,
    // 1500 USD * 17.50 = 26250 MDL
    balanceMdl: 26250,
  },
  // Single archived account so component tests can exercise the
  // "Show archived" toggle + Unarchive action. Only surfaces from the
  // list handler when `includeArchived=true`.
  {
    id: '55555555-5555-5555-5555-555555555555',
    name: 'Old Revolut',
    type: 'BankCurrent',
    currency: 'MDL',
    openingDate: '2023-02-01',
    isArchived: true,
    isPooled: false,
    notes: null,
    balance: 0,
    balanceMdl: 0,
  },
];

const categories: CategoryDto[] = [
  {
    id: 'c0000001-0000-0000-0000-000000000001',
    name: 'Groceries',
    flow: 'Expense',
    isArchived: false,
    color: '#22c55e',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000002',
    name: 'Dining out',
    flow: 'Expense',
    isArchived: false,
    color: '#f97316',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000003',
    name: 'Transport',
    flow: 'Expense',
    isArchived: false,
    color: '#3b82f6',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000004',
    name: 'Utilities',
    flow: 'Expense',
    isArchived: false,
    color: '#8b5cf6',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000005',
    name: 'Entertainment',
    flow: 'Expense',
    isArchived: false,
    color: '#ec4899',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000006',
    name: 'Misc',
    flow: 'Both',
    isArchived: false,
    color: '#94a3b8',
  },
  {
    id: 'c0000001-0000-0000-0000-000000000007',
    name: 'Salary',
    flow: 'Income',
    isArchived: false,
    color: '#10b981',
  },
  // One archived category so the categories manager can exercise the
  // "Archived" badge + the hidden Archive action on archived rows. Only
  // surfaces from the list handler when `includeArchived=true`.
  {
    id: 'c0000001-0000-0000-0000-000000000008',
    name: 'Old subscriptions',
    flow: 'Expense',
    isArchived: true,
    color: '#64748b',
  },
];

// Auto-categorization keyword patterns. Seeds a mix of Seeded defaults and
// Learned rules so the merged manager can exercise both chip variants, the
// grouping-by-category join, and the inline add/delete flows. Groceries gets
// two keywords (so an expanded row shows multiple chips); Dining out gets one;
// the remaining categories stay empty (so the "No keywords yet" hint renders).
const categoryPatterns: CategoryPatternDto[] = [
  {
    id: 'cp000001-0000-0000-0000-000000000001',
    keyword: 'LINELLA',
    categoryId: 'c0000001-0000-0000-0000-000000000001',
    categoryName: 'Groceries',
    source: 'Seeded',
  },
  {
    id: 'cp000001-0000-0000-0000-000000000002',
    keyword: 'TUCANO',
    categoryId: 'c0000001-0000-0000-0000-000000000002',
    categoryName: 'Dining out',
    source: 'Learned',
  },
  {
    id: 'cp000001-0000-0000-0000-000000000003',
    keyword: 'KAUFLAND',
    categoryId: 'c0000001-0000-0000-0000-000000000001',
    categoryName: 'Groceries',
    source: 'Learned',
  },
];

const budgets: BudgetDto[] = [
  {
    id: 'b0000001-0000-0000-0000-000000000001',
    categoryId: 'c0000001-0000-0000-0000-000000000001',
    categoryName: 'Groceries',
    monthlyLimit: 3000,
    spent: 1200,
    remaining: 1800,
    status: 'OnTrack',
    year: 2026,
    month: 5,
  },
  {
    id: 'b0000001-0000-0000-0000-000000000002',
    categoryId: 'c0000001-0000-0000-0000-000000000002',
    categoryName: 'Dining out',
    monthlyLimit: 1500,
    spent: 1350,
    remaining: 150,
    status: 'Warning',
    year: 2026,
    month: 5,
  },
  {
    id: 'b0000001-0000-0000-0000-000000000003',
    categoryId: 'c0000001-0000-0000-0000-000000000003',
    categoryName: 'Transport',
    monthlyLimit: 1000,
    spent: 1450,
    remaining: -450,
    status: 'Over',
    year: 2026,
    month: 5,
  },
];

// Goals seed covers both linked and manual modes plus all four status
// buckets, so tests can assert mode/status rendering without redefining
// rows per test.
const goals: GoalDto[] = [
  {
    id: 'g0000001-0000-0000-0000-000000000001',
    name: 'Emergency fund',
    targetAmount: 50000,
    targetDate: '2026-12-31',
    linkedAccountId: '33333333-3333-3333-3333-333333333333',
    linkedAccountName: 'ING Savings',
    saved: 22550,
    remaining: 27450,
    progressPercent: 22550 / 50000,
    status: 'OnTrack',
    requiredMonthlyContribution: 3920,
    isLinkedMode: true,
    missingFxRate: false,
  },
  {
    id: 'g0000001-0000-0000-0000-000000000002',
    name: 'Vacation',
    targetAmount: 10000,
    targetDate: '2026-08-15',
    linkedAccountId: null,
    linkedAccountName: null,
    saved: 4500,
    remaining: 5500,
    progressPercent: 4500 / 10000,
    status: 'AtRisk',
    requiredMonthlyContribution: 1830,
    isLinkedMode: false,
    missingFxRate: false,
  },
  {
    id: 'g0000001-0000-0000-0000-000000000003',
    name: 'New laptop',
    targetAmount: 25000,
    targetDate: '2026-07-01',
    linkedAccountId: null,
    linkedAccountName: null,
    saved: 26000,
    remaining: 0,
    progressPercent: 26000 / 25000,
    status: 'Achieved',
    requiredMonthlyContribution: null,
    isLinkedMode: false,
    missingFxRate: false,
  },
  {
    id: 'g0000001-0000-0000-0000-000000000004',
    name: 'Down payment',
    targetAmount: 200000,
    targetDate: '2026-09-30',
    linkedAccountId: '44444444-4444-4444-4444-444444444444',
    linkedAccountName: 'XTB',
    saved: 26250,
    remaining: 173750,
    progressPercent: 26250 / 200000,
    status: 'Behind',
    requiredMonthlyContribution: 43500,
    isLinkedMode: true,
    missingFxRate: false,
  },
];

// Loans seed covers both directions plus both status buckets: a Received
// (borrowed) EUR loan with partial repayments, an Active Given (lent) MDL
// loan, and a Settled Given USD loan. MDL-eq figures follow the seeded FX
// rates (EUR→MDL 19.2, USD→MDL 17.5).
const loans: LoanDto[] = [
  {
    id: 'l0000001-0000-0000-0000-000000000001',
    direction: 'Received',
    counterparty: 'Parents',
    principal: 5000,
    currency: 'EUR',
    loanDate: '2026-01-10',
    totalRepaid: 2000,
    outstanding: 3000,
    // 3000 EUR * 19.2 = 57600 MDL
    outstandingMdl: 57600,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 2,
    isAccountLinked: false,
    notes: 'Borrowed for the car',
    isArchived: false,
  },
  {
    id: 'l0000001-0000-0000-0000-000000000002',
    direction: 'Given',
    counterparty: 'Ion',
    principal: 2500,
    currency: 'MDL',
    loanDate: '2026-03-05',
    totalRepaid: 500,
    outstanding: 2000,
    outstandingMdl: 2000,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 1,
    isAccountLinked: true,
    notes: null,
    isArchived: false,
  },
  {
    id: 'l0000001-0000-0000-0000-000000000003',
    direction: 'Given',
    counterparty: 'Maria',
    principal: 1000,
    currency: 'USD',
    loanDate: '2025-11-20',
    totalRepaid: 1000,
    outstanding: 0,
    outstandingMdl: 0,
    missingFxRate: false,
    status: 'Settled',
    paymentCount: 3,
    isAccountLinked: false,
    notes: null,
    isArchived: false,
  },
  // Single archived loan so component tests can exercise the "Show
  // archived" toggle + Unarchive action. Only surfaces from the list
  // handler when `includeArchived=true`. Outstanding is intentionally
  // non-zero: archiving hides a loan without settling it, and the summary
  // tiles must never count this row.
  {
    id: 'l0000001-0000-0000-0000-000000000004',
    direction: 'Given',
    counterparty: 'Old colleague',
    principal: 800,
    currency: 'MDL',
    loanDate: '2024-05-01',
    totalRepaid: 300,
    outstanding: 500,
    outstandingMdl: 500,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 1,
    isAccountLinked: false,
    notes: null,
    isArchived: true,
  },
];

// Detail-view seeds for loans. The Parents loan has untracked payments
// (no linked transactions/accounts); the Ion loan exercises the
// account-linked path — disbursement line + a payment whose deletion also
// deletes the linked transaction (the confirm-dialog warning path).
const loanDetails: Record<string, LoanDetailDto> = {
  'l0000001-0000-0000-0000-000000000001': {
    id: 'l0000001-0000-0000-0000-000000000001',
    direction: 'Received',
    counterparty: 'Parents',
    principal: 5000,
    currency: 'EUR',
    loanDate: '2026-01-10',
    totalRepaid: 2000,
    outstanding: 3000,
    outstandingMdl: 57600,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 2,
    notes: 'Borrowed for the car',
    isArchived: false,
    createdOn: '2026-01-10',
    disbursementTransactionId: null,
    disbursementAccountId: null,
    disbursementAccountName: null,
    payments: [
      {
        id: 'lp000001-0000-0000-0000-000000000002',
        amount: 1200,
        currency: 'EUR',
        occurredOn: '2026-06-15',
        transactionId: null,
        accountId: null,
        accountName: null,
        notes: 'June repayment',
      },
      {
        id: 'lp000001-0000-0000-0000-000000000001',
        amount: 800,
        currency: 'EUR',
        occurredOn: '2026-03-01',
        transactionId: null,
        accountId: null,
        accountName: null,
        notes: null,
      },
    ],
  },
  'l0000001-0000-0000-0000-000000000002': {
    id: 'l0000001-0000-0000-0000-000000000002',
    direction: 'Given',
    counterparty: 'Ion',
    principal: 2500,
    currency: 'MDL',
    loanDate: '2026-03-05',
    totalRepaid: 500,
    outstanding: 2000,
    outstandingMdl: 2000,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 1,
    notes: null,
    isArchived: false,
    createdOn: '2026-03-05',
    disbursementTransactionId: 'tx-loan-disburse',
    disbursementAccountId: '11111111-1111-1111-1111-111111111111',
    disbursementAccountName: 'Cash Wallet',
    payments: [
      {
        id: 'lp000001-0000-0000-0000-000000000003',
        amount: 500,
        currency: 'MDL',
        occurredOn: '2026-04-20',
        transactionId: 'tx-loan-payment',
        accountId: '11111111-1111-1111-1111-111111111111',
        accountName: 'Cash Wallet',
        notes: null,
      },
    ],
  },
};

const fxRates: FxRateDto[] = [
  {
    id: 'f0000001-0000-0000-0000-000000000001',
    fromCurrency: 'USD',
    toCurrency: 'MDL',
    rate: 17.5,
    asOf: '2024-09-10',
    createdAt: '2024-09-10T00:00:00Z',
    updatedAt: '2024-09-10T00:00:00Z',
    source: 'Manual',
  },
  {
    id: 'f0000001-0000-0000-0000-000000000002',
    fromCurrency: 'EUR',
    toCurrency: 'MDL',
    rate: 19.2,
    asOf: '2024-09-10',
    createdAt: '2024-09-10T00:00:00Z',
    updatedAt: '2024-09-10T00:00:00Z',
    source: 'BnmAuto',
  },
];

// A deterministic transfer pair sits across Cash Wallet and ING Savings so any
// test or dev-mode peek sees the new fields wired through. Both rows carry
// `isTransfer: true` and reference each other via `counterAccountId`. We also
// seed a USD adjustment on the XTB Brokerage so the new "Balance adjustment"
// badge has a row to render against.
const transactions: TransactionDto[] = [
  {
    id: 'tx-transfer-source',
    accountId: '11111111-1111-1111-1111-111111111111',
    transactionDate: '2025-05-10',
    direction: 'Expense',
    amount: 500,
    description: 'Move savings → cash',
    notes: null,
    source: 'Manual',
    isTransfer: true,
    counterAccountId: '33333333-3333-3333-3333-333333333333',
    currency: 'MDL',
    amountMdl: 500,
    isAdjustment: false,
  },
  {
    id: 'tx-transfer-dest',
    accountId: '33333333-3333-3333-3333-333333333333',
    transactionDate: '2025-05-10',
    direction: 'Income',
    amount: 500,
    description: 'Move savings → cash',
    notes: null,
    source: 'Manual',
    isTransfer: true,
    counterAccountId: '11111111-1111-1111-1111-111111111111',
    currency: 'MDL',
    amountMdl: 500,
    isAdjustment: false,
  },
  {
    id: 'tx-coffee',
    accountId: '11111111-1111-1111-1111-111111111111',
    transactionDate: '2025-05-12',
    direction: 'Expense',
    amount: 35,
    description: 'Coffee at Tucano',
    // One fixture carries a non-null note so the table test can assert the
    // muted note sub-line + seeded edit-dialog text.
    notes: 'Treat after the gym',
    source: 'Manual',
    isTransfer: false,
    counterAccountId: null,
    currency: 'MDL',
    amountMdl: 35,
    isAdjustment: false,
  },
  {
    id: 'tx-xtb-adjust',
    accountId: '44444444-4444-4444-4444-444444444444',
    transactionDate: '2025-05-15',
    direction: 'Income',
    amount: 250,
    description: 'Balance adjustment',
    notes: null,
    source: 'Manual',
    isTransfer: false,
    counterAccountId: null,
    currency: 'USD',
    // 250 USD * 17.50 = 4375 MDL (matches the seeded fx rate)
    amountMdl: 4375,
    isAdjustment: true,
  },
];

// Detail-view seeds — one for the XTB Brokerage USD account (exercises
// the dual-currency + missingFxRate=false code paths) and one for the
// MDL ING Savings (exercises the identity-FX path). Both include the
// activity totals shape the Performance card consumes.
const accountDetails: Record<string, AccountDetailDto> = {
  '44444444-4444-4444-4444-444444444444': {
    id: '44444444-4444-4444-4444-444444444444',
    name: 'XTB',
    type: 'Brokerage',
    currency: 'USD',
    openingDate: '2024-09-10',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: 1500,
    balanceMdl: 26250,
    initialCapital: 1000,
    allTime: {
      contributionsMdl: 12000,
      withdrawalsMdl: 2000,
      netPnLMdl: 4375,
      contributionCount: 3,
      withdrawalCount: 1,
      adjustmentCount: 2,
      missingFxRate: false,
    },
    yearToDate: {
      contributionsMdl: 5000,
      withdrawalsMdl: 0,
      netPnLMdl: 1750,
      contributionCount: 1,
      withdrawalCount: 0,
      adjustmentCount: 1,
      missingFxRate: false,
    },
    firstActivityDate: '2024-09-10',
    lastActivityDate: '2025-05-15',
    realActivityCount: 2,
  },
  '33333333-3333-3333-3333-333333333333': {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'ING Savings',
    type: 'BankDeposit',
    currency: 'MDL',
    openingDate: '2024-03-01',
    isArchived: false,
    isPooled: false,
    notes: null,
    balance: 22550,
    balanceMdl: 22550,
    initialCapital: 20000,
    allTime: {
      contributionsMdl: 5000,
      withdrawalsMdl: 2500,
      netPnLMdl: 50,
      contributionCount: 2,
      withdrawalCount: 1,
      adjustmentCount: 0,
      missingFxRate: false,
    },
    yearToDate: {
      contributionsMdl: 500,
      withdrawalsMdl: 0,
      netPnLMdl: 0,
      contributionCount: 1,
      withdrawalCount: 0,
      adjustmentCount: 0,
      missingFxRate: false,
    },
    firstActivityDate: '2024-03-01',
    lastActivityDate: '2025-05-10',
    realActivityCount: 1,
  },
};

// Detail-view seeds for goals — one linked-mode (Emergency fund → ING Savings)
// and one manual-mode (Vacation). The two combined exercise every code path
// the detail page renders:
//   - linked vs manual mode badge, hidden Update-saved on linked
//   - status pill across OnTrack and AtRisk buckets (Achieved + Behind are
//     already covered by the list-page tests; we re-use OnTrack here as the
//     "happy" path and AtRisk for the secondary status)
//   - non-null `pace` cells (linked: avg + projected; manual: also includes
//     a richer contributions array)
//   - `savedHistory` non-empty so the chart's sr-only enumeration is testable
const goalDetails: Record<string, GoalDetailDto> = {
  'g0000001-0000-0000-0000-000000000001': {
    id: 'g0000001-0000-0000-0000-000000000001',
    name: 'Emergency fund',
    targetAmount: 50000,
    targetDate: '2026-12-31',
    linkedAccountId: '33333333-3333-3333-3333-333333333333',
    linkedAccountName: 'ING Savings',
    saved: 22550,
    remaining: 27450,
    progressPercent: 22550 / 50000,
    status: 'OnTrack',
    requiredMonthlyContribution: 3920,
    isLinkedMode: true,
    missingFxRate: false,
    createdOn: '2025-08-01',
    isArchived: false,
    pace: {
      avgMonthlyContribution: 2500,
      projectedCompletionDate: '2027-04-01',
      monthsToAchieveAtPace: 11,
    },
    contributions: [
      {
        id: null,
        amount: 500,
        occurredOn: '2026-05-10',
        notes: 'Move savings → cash (transfer leg)',
        source: 'LinkedAccountTransaction',
      },
      {
        id: null,
        amount: 2000,
        occurredOn: '2026-04-12',
        notes: 'Salary deposit',
        source: 'LinkedAccountTransaction',
      },
      {
        id: null,
        amount: -300,
        occurredOn: '2026-03-22',
        notes: 'Quick withdrawal',
        source: 'LinkedAccountTransaction',
      },
    ],
    savedHistory: [
      { asOf: '2026-01-31', saved: 18000 },
      { asOf: '2026-02-28', saved: 19500 },
      { asOf: '2026-03-31', saved: 20850 },
      { asOf: '2026-04-30', saved: 22050 },
      { asOf: '2026-05-23', saved: 22550 },
    ],
  },
  'g0000001-0000-0000-0000-000000000002': {
    id: 'g0000001-0000-0000-0000-000000000002',
    name: 'Vacation',
    targetAmount: 10000,
    targetDate: '2026-08-15',
    linkedAccountId: null,
    linkedAccountName: null,
    saved: 4500,
    remaining: 5500,
    progressPercent: 4500 / 10000,
    status: 'AtRisk',
    requiredMonthlyContribution: 1830,
    isLinkedMode: false,
    missingFxRate: false,
    createdOn: '2026-01-15',
    isArchived: false,
    pace: {
      avgMonthlyContribution: 900,
      projectedCompletionDate: '2026-11-01',
      monthsToAchieveAtPace: 6,
    },
    contributions: [
      {
        id: 'gc-0001',
        amount: 1500,
        occurredOn: '2026-04-30',
        notes: 'Bonus stash',
        source: 'Manual',
      },
      {
        id: 'gc-0002',
        amount: 1500,
        occurredOn: '2026-03-31',
        notes: null,
        source: 'Manual',
      },
      {
        id: 'gc-0003',
        amount: 1500,
        occurredOn: '2026-02-28',
        notes: null,
        source: 'Manual',
      },
    ],
    savedHistory: [
      { asOf: '2026-02-28', saved: 1500 },
      { asOf: '2026-03-31', saved: 3000 },
      { asOf: '2026-04-30', saved: 4500 },
    ],
  },
};

// Net-worth seed for the three dashboard tiles. Round numbers rather than a
// re-derivation of loans[]: the endpoint applies rules the list fixtures
// don't model (archived loans still count — a hidden debt is still a debt —
// while loans with no linked disbursement never do), so the tiles and the
// /loans summary are allowed to disagree by design.
//
//   grossAssets          48100  (matches the accounts[] MDL balances)
//   externalLiabilities  57600  (the borrowed EUR, as a POSITIVE magnitude)
//   externalAssets        2000  (still owed to the user)
//   netWorth             48100 - 57600 + 2000 = -7500
//
// Negative on purpose: the borrowed money outweighs the accounts, which is
// exactly the case the old client-side account sum got wrong.
const netWorth: NetWorthDto = {
  grossAssetsMdl: 48100,
  externalLiabilitiesMdl: 57600,
  externalAssetsMdl: 2000,
  netWorthMdl: -7500,
  accountsMissingFxRate: 0,
  loansMissingFxRate: 0,
  // Zero by default so the "not all of this is yours" sub-line stays absent
  // for every suite that doesn't care about pools. The tests that do care
  // override this handler with a positive figure.
  outsideCapitalMdl: 0,
};

// Pools seed. Modelled on the worked example in POOLED-CAPITAL.md but with
// round numbers, so every derived figure below is exact and a test asserting
// on one can't be defeated by a rounding penny:
//
//   pool value 2,500 = account 2,600 − 100 closed-but-unpaid
//   2,000 shares at 1.25 → owner 400, Andrei 800, Bogdan 800
//   owner 500 + outside 2,000 = 2,500, so ownerFraction is exactly 0.20
//   USD→MDL 17.5 (the seeded rate) → 43,750 pool / 35,000 outside / 8,750 yours
//
// Two deliberate shapes carried by the roster:
//   - Bogdan sits exactly ON his capital base, so his `distributable` is 0 —
//     the "a green month can correctly pay nothing" case, which is the single
//     most disputable behaviour in the design.
//   - Andrei carries the closed-but-unpaid payout, so the unpaid badge, the
//     owed-cash block and the settle flow all have something to render.
const pools: PoolDto[] = [
  {
    id: 'aaaa0001-0000-4000-8000-000000000001',
    accountId: '66666666-6666-6666-6666-666666666666',
    accountName: 'Binanance',
    name: 'Binance pool',
    currency: 'USD',
    inceptionDate: '2026-08-01',
    notes: 'Two friends, monthly profit share.',
    isArchived: false,
    accountBalance: 2600,
    unpaidDistributionCash: 100,
    unpaidDistributionCount: 1,
    poolValue: 2500,
    poolValueMdl: 43750,
    totalUnits: 2000,
    navPerUnit: 1.25,
    participantCount: 3,
    ownerFraction: 0.2,
    outsideCapital: 2000,
    outsideCapitalMdl: 35000,
    missingFxRate: false,
  },
  // Single archived pool so component tests can exercise the "Show archived"
  // toggle. Only surfaces from the list handler when `includeArchived=true`.
  // Wound down: no shares left, so `navPerUnit` is null and the row has to
  // render an em dash rather than a placeholder price.
  {
    id: 'aaaa0001-0000-4000-8000-000000000002',
    accountId: '77777777-7777-7777-7777-777777777777',
    accountName: 'Old Kraken',
    name: 'Kraken pool',
    currency: 'EUR',
    inceptionDate: '2025-02-01',
    notes: null,
    isArchived: true,
    accountBalance: 0,
    unpaidDistributionCash: 0,
    unpaidDistributionCount: 0,
    poolValue: 0,
    poolValueMdl: 0,
    totalUnits: 0,
    navPerUnit: null,
    participantCount: 1,
    ownerFraction: 1,
    outsideCapital: 0,
    outsideCapitalMdl: 0,
    missingFxRate: false,
  },
];

const poolDetails: Record<string, PoolDetailDto> = {
  'aaaa0001-0000-4000-8000-000000000001': {
    id: 'aaaa0001-0000-4000-8000-000000000001',
    accountId: '66666666-6666-6666-6666-666666666666',
    accountName: 'Binanance',
    accountCurrency: 'USD',
    accountIsArchived: false,
    name: 'Binance pool',
    currency: 'USD',
    inceptionDate: '2026-08-01',
    notes: 'Two friends, monthly profit share.',
    isArchived: false,
    createdOn: '2026-08-01T09:00:00Z',
    asOf: '2026-09-03',
    accountBalance: 2600,
    unpaidDistributionCash: 100,
    unpaidDistributionCount: 1,
    poolValue: 2500,
    poolValueMdl: 43750,
    totalUnits: 2000,
    navPerUnit: 1.25,
    ownerParticipantId: 'bbbb0001-0000-4000-8000-000000000001',
    ownerFraction: 0.2,
    outsideCapital: 2000,
    outsideCapitalMdl: 35000,
    missingFxRate: false,
    // Three days old — inside the week-long grace window, so the staleness
    // warning stays silent on the default render. Tests that want it push
    // their own age through a handler override.
    lastMarkDate: '2026-08-31',
    markAgeDays: 3,
    participants: [
      {
        id: 'bbbb0001-0000-4000-8000-000000000001',
        name: 'Me',
        isOwner: true,
        isArchived: false,
        joinedOn: '2026-08-01',
        units: 400,
        ownershipPercent: 20,
        stake: 500,
        stakeMdl: 8750,
        capitalBase: 0,
        distributable: 500,
        unpaidDistributionCash: 0,
        unpaidDistributionCount: 0,
        missingFxRate: false,
      },
      {
        id: 'bbbb0001-0000-4000-8000-000000000002',
        name: 'Andrei',
        isOwner: false,
        isArchived: false,
        joinedOn: '2026-08-05',
        units: 800,
        ownershipPercent: 40,
        stake: 1000,
        stakeMdl: 17500,
        capitalBase: 900,
        distributable: 100,
        unpaidDistributionCash: 100,
        unpaidDistributionCount: 1,
        missingFxRate: false,
      },
      {
        id: 'bbbb0001-0000-4000-8000-000000000003',
        name: 'Bogdan',
        isOwner: false,
        isArchived: false,
        joinedOn: '2026-08-10',
        units: 800,
        ownershipPercent: 40,
        stake: 1000,
        stakeMdl: 17500,
        capitalBase: 1000,
        // Exactly at his basis: nothing payable, however green the month was.
        distributable: 0,
        unpaidDistributionCash: 0,
        unpaidDistributionCount: 0,
        missingFxRate: false,
      },
    ],
    events: [
      {
        id: 'cccc0001-0000-4000-8000-000000000004',
        participantId: 'bbbb0001-0000-4000-8000-000000000002',
        participantName: 'Andrei',
        kind: 'Distribution',
        occurredOn: '2026-08-31',
        units: 80,
        unitsDelta: -80,
        navPerUnit: 1.25,
        poolValuePreMoney: 2600,
        cash: 100,
        cashCurrency: 'USD',
        settledOn: null,
        isUnpaid: true,
        movementTransactionId: null,
        movementAccountId: null,
        movementAccountName: null,
        notes: 'August close',
      },
      {
        id: 'cccc0001-0000-4000-8000-000000000003',
        participantId: 'bbbb0001-0000-4000-8000-000000000001',
        participantName: 'Me',
        kind: 'Redemption',
        occurredOn: '2026-08-20',
        units: 100,
        unitsDelta: -100,
        navPerUnit: 1.2,
        poolValuePreMoney: 2520,
        cash: 120,
        cashCurrency: 'USD',
        settledOn: '2026-08-20',
        isUnpaid: false,
        movementTransactionId: 'tx-pool-redemption',
        movementAccountId: '66666666-6666-6666-6666-666666666666',
        movementAccountName: 'Binanance',
        notes: null,
      },
      {
        id: 'cccc0001-0000-4000-8000-000000000002',
        participantId: 'bbbb0001-0000-4000-8000-000000000003',
        participantName: 'Bogdan',
        kind: 'Subscription',
        occurredOn: '2026-08-10',
        units: 800,
        unitsDelta: 800,
        navPerUnit: 1.25,
        poolValuePreMoney: 1500,
        cash: 1000,
        cashCurrency: 'USD',
        settledOn: '2026-08-10',
        isUnpaid: false,
        movementTransactionId: 'tx-pool-subscription',
        movementAccountId: '66666666-6666-6666-6666-666666666666',
        movementAccountName: 'Binanance',
        notes: null,
      },
      {
        id: 'cccc0001-0000-4000-8000-000000000001',
        participantId: 'bbbb0001-0000-4000-8000-000000000001',
        participantName: 'Me',
        kind: 'Seed',
        occurredOn: '2026-08-01',
        units: 500,
        unitsDelta: 500,
        navPerUnit: 1,
        poolValuePreMoney: 0,
        // A seed moves no cash: the owner's existing balance simply becomes
        // shares, so there is no transaction and no cash leg.
        cash: null,
        cashCurrency: null,
        settledOn: '2026-08-01',
        isUnpaid: false,
        movementTransactionId: null,
        movementAccountId: null,
        movementAccountName: null,
        notes: null,
      },
    ],
    reconciliation: {
      isClean: true,
      unmatchedTransactions: [],
      participantUnits: 2000,
      ledgerUnits: 2000,
      unitsDrift: 0,
      unitsBalance: true,
      valueDrifts: [],
      unbackedCashClaims: [],
      // The ledger predicts the ACCOUNT balance (2,600), not the pool value —
      // the 100 of closed-but-unpaid payout cash has not left the account yet.
      predictedBalance: 2600,
      derivedBalance: 2600,
      balanceDrift: 0,
      balanceReconciles: true,
    },
  },
  'aaaa0001-0000-4000-8000-000000000002': {
    id: 'aaaa0001-0000-4000-8000-000000000002',
    accountId: '77777777-7777-7777-7777-777777777777',
    accountName: 'Old Kraken',
    accountCurrency: 'EUR',
    accountIsArchived: true,
    name: 'Kraken pool',
    currency: 'EUR',
    inceptionDate: '2025-02-01',
    notes: null,
    isArchived: true,
    createdOn: '2025-02-01T09:00:00Z',
    asOf: '2026-09-03',
    accountBalance: 0,
    unpaidDistributionCash: 0,
    unpaidDistributionCount: 0,
    poolValue: 0,
    poolValueMdl: 0,
    totalUnits: 0,
    navPerUnit: null,
    ownerParticipantId: 'bbbb0002-0000-4000-8000-000000000001',
    ownerFraction: 1,
    outsideCapital: 0,
    outsideCapitalMdl: 0,
    missingFxRate: false,
    lastMarkDate: '2026-01-15',
    markAgeDays: 231,
    participants: [
      {
        id: 'bbbb0002-0000-4000-8000-000000000001',
        name: 'Me',
        isOwner: true,
        isArchived: false,
        joinedOn: '2025-02-01',
        units: 0,
        ownershipPercent: 0,
        // Wound down: no shares outstanding means no price, so nothing can be
        // valued — null, never a zero dressed up as a valuation.
        stake: null,
        stakeMdl: null,
        capitalBase: 0,
        distributable: null,
        unpaidDistributionCash: 0,
        unpaidDistributionCount: 0,
        missingFxRate: false,
      },
    ],
    events: [],
    reconciliation: {
      isClean: true,
      unmatchedTransactions: [],
      participantUnits: 0,
      ledgerUnits: 0,
      unitsDrift: 0,
      unitsBalance: true,
      valueDrifts: [],
      unbackedCashClaims: [],
      predictedBalance: 0,
      derivedBalance: 0,
      balanceDrift: 0,
      balanceReconciles: true,
    },
  },
};

export const handlers = [
  http.get('*/accounts', ({ request }) => {
    const url = new URL(request.url);
    const includeArchived = url.searchParams.get('includeArchived') === 'true';
    const rows = includeArchived ? accounts : accounts.filter((a) => !a.isArchived);
    return HttpResponse.json(rows);
  }),
  http.post('*/accounts', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    if (!body.type) {
      return HttpResponse.json({ error: 'Type is required' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-account-id' }, { status: 201 });
  }),
  http.get('*/accounts/:id', ({ params }) => {
    const id = String(params.id);
    const detail = accountDetails[id];
    if (!detail) {
      return HttpResponse.json(
        { error: 'Account not found', code: 'accounts.not_found' },
        { status: 404 },
      );
    }
    return HttpResponse.json(detail);
  }),
  http.get('*/categories', ({ request }) => {
    const url = new URL(request.url);
    const includeArchived = url.searchParams.get('includeArchived') === 'true';
    const rows = includeArchived ? categories : categories.filter((c) => !c.isArchived);
    return HttpResponse.json(rows);
  }),
  http.post('*/categories', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    if (!body.flow) {
      return HttpResponse.json({ error: 'Flow is required' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-category-id' }, { status: 201 });
  }),
  http.put('*/categories/:id', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    if (!body.flow) {
      return HttpResponse.json({ error: 'Flow is required' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/categories/:id', () => new HttpResponse(null, { status: 204 })),
  http.get('*/category-patterns', () => HttpResponse.json(categoryPatterns)),
  http.post('*/category-patterns', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    const keyword = String(body.keyword ?? '').trim();
    if (keyword.length === 0) {
      return HttpResponse.json({ error: 'Keyword is required' }, { status: 400 });
    }
    if (!body.categoryId) {
      return HttpResponse.json({ error: 'Category is required' }, { status: 400 });
    }
    // Mirror the backend's 409 on a duplicate keyword (case-insensitive,
    // since the server upper-cases). RFC-7807 ProblemDetails shape so the
    // detail surfaces through ApiError.message.
    const exists = categoryPatterns.some((p) => p.keyword.toUpperCase() === keyword.toUpperCase());
    if (exists) {
      return HttpResponse.json(
        {
          type: 'category_pattern.duplicate_keyword',
          title: 'Conflict',
          status: 409,
          detail: `A pattern for keyword '${keyword.toUpperCase()}' already exists.`,
          errorCode: 'category_pattern.duplicate_keyword',
          errorType: 'Conflict',
        },
        { status: 409 },
      );
    }
    return HttpResponse.json({ id: 'created-pattern-id' }, { status: 201 });
  }),
  http.put('*/category-patterns/:id', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    const keyword = String(body.keyword ?? '').trim();
    if (keyword.length === 0) {
      return HttpResponse.json({ error: 'Keyword is required' }, { status: 400 });
    }
    if (!body.categoryId) {
      return HttpResponse.json({ error: 'Category is required' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/category-patterns/:id', () => new HttpResponse(null, { status: 204 })),
  http.get('*/transactions', ({ request }) => {
    const url = new URL(request.url);
    const isTransferParam = url.searchParams.get('isTransfer');
    const isAdjustmentParam = url.searchParams.get('isAdjustment');
    const accountIdParam = url.searchParams.get('accountId');
    const page = Number(url.searchParams.get('page') ?? '1');
    const pageSize = Number(url.searchParams.get('pageSize') ?? '25');
    let rows = transactions;
    if (accountIdParam) {
      rows = rows.filter((t) => t.accountId === accountIdParam);
    }
    if (isTransferParam === 'true') {
      rows = rows.filter((t) => t.isTransfer);
    } else if (isTransferParam === 'false') {
      rows = rows.filter((t) => !t.isTransfer);
    }
    if (isAdjustmentParam === 'true') {
      rows = rows.filter((t) => t.isAdjustment);
    } else if (isAdjustmentParam === 'false') {
      rows = rows.filter((t) => !t.isAdjustment);
    }
    const totalCount = rows.length;
    const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
    const start = (page - 1) * pageSize;
    const items = rows.slice(start, start + pageSize);
    const result: PagedResult<TransactionDto> = {
      items,
      totalCount,
      pageNumber: page,
      pageSize,
      totalPages,
    };
    return HttpResponse.json(result);
  }),
  http.post('*/transactions', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.accountId) {
      return HttpResponse.json({ error: 'Account is required' }, { status: 400 });
    }
    if (!body.description || String(body.description).trim().length === 0) {
      return HttpResponse.json({ error: 'Description is required' }, { status: 400 });
    }
    if (body.amount === undefined || (body.amount as number) <= 0) {
      return HttpResponse.json({ error: 'Amount must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-tx-id' }, { status: 201 });
  }),
  http.put('*/transactions/:id/category', async ({ request }) => {
    // Body is `{ categoryId: string | null }`. A null clears the category.
    // The real backend returns 400 when the chosen category's flow is
    // incompatible with the row's direction; tests opt into that via
    // `server.use(...)` overrides, so the default handler just acks 204.
    await request.json();
    return new HttpResponse(null, { status: 204 });
  }),
  http.put('*/transactions/:id/notes', async ({ request }) => {
    // Body is `{ notes: string | null }`. Null/blank clears the note. Tests
    // that need to inspect the body or id override this via `server.use(...)`;
    // the default handler just acks 204 No Content.
    await request.json();
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/transactions/:id', () => new HttpResponse(null, { status: 204 })),
  http.post('*/transfers', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.sourceAccountId || !body.destinationAccountId) {
      return HttpResponse.json(
        { error: 'Source and destination accounts are required' },
        { status: 400 },
      );
    }
    if (body.sourceAccountId === body.destinationAccountId) {
      return HttpResponse.json({ error: 'Source and destination must differ' }, { status: 400 });
    }
    if (body.amount === undefined || (body.amount as number) <= 0) {
      return HttpResponse.json({ error: 'Amount must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json(
      {
        sourceTransactionId: 'mock-transfer-source-id',
        destinationTransactionId: 'mock-transfer-dest-id',
      },
      { status: 201 },
    );
  }),
  http.put('*/accounts/:id', async ({ request }) => {
    // Body is `{ name: string, notes: string | null }`. Mirror the backend's
    // 400 on a blank name; otherwise ack 204 No Content. Tests that need to
    // inspect the body or force an error override this via `server.use(...)`.
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/accounts/:id', () => new HttpResponse(null, { status: 204 })),
  http.post('*/accounts/:id/unarchive', () => new HttpResponse(null, { status: 204 })),
  http.delete('*/accounts/:id/permanent', ({ params }) => {
    const id = String(params.id);
    // Cash Wallet has seeded transactions/transfers, so the backend would
    // refuse a permanent delete with a 409 — mirror that here so the error
    // path is testable. This matches the real API's RFC-7807 ProblemDetails
    // shape (message in `detail`), which `ApiError` parses into `.message`.
    // Every other account id deletes cleanly (204).
    if (id === '11111111-1111-1111-1111-111111111111') {
      return HttpResponse.json(
        {
          type: 'account.has_linked_records',
          title: 'Conflict',
          status: 409,
          detail:
            "Account with id '11111111-1111-1111-1111-111111111111' has linked transactions, imports, or goals and can't be permanently deleted. Archive it instead.",
          errorCode: 'account.has_linked_records',
          errorType: 'Conflict',
        },
        { status: 409 },
      );
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.post('*/accounts/:id/balance-changes', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    const kind = body.kind as string | undefined;
    const value = body.value as number | undefined;
    if (kind === undefined || value === undefined) {
      return HttpResponse.json({ error: 'Kind and value are required' }, { status: 400 });
    }
    if (!body.date) {
      return HttpResponse.json({ error: 'Date is required' }, { status: 400 });
    }
    if ((kind === 'Investment' || kind === 'Withdrawal') && value <= 0) {
      return HttpResponse.json({ error: 'Amount must be greater than 0' }, { status: 400 });
    }
    // Echo a sensible delta: Withdrawals write an expense leg (negative),
    // Investments an income leg (positive of the amount), Adjustments a fixed
    // sample P&L delta the dialog turns into an Increased/Decreased toast.
    const delta = kind === 'Withdrawal' ? -value : kind === 'Investment' ? value : 100;
    return HttpResponse.json({ transactionId: 'mock-tx', delta }, { status: 201 });
  }),
  http.get('*/dashboard/summary', ({ request }) => {
    const url = new URL(request.url);
    const month = url.searchParams.get('month') ?? '2026-05';
    return HttpResponse.json({
      month,
      income: 12000,
      expense: 8500,
      net: 3500,
      savingsRate: 3500 / 12000,
      transactionCount: 24,
      missingFxRate: false,
    });
  }),
  http.get('*/dashboard/net-worth-trend', ({ request }) => {
    const url = new URL(request.url);
    const months = Number(url.searchParams.get('months') ?? '6');
    const series = [
      { month: '2025-12', netWorthMdl: 40000 },
      { month: '2026-01', netWorthMdl: 42500 },
      { month: '2026-02', netWorthMdl: 44100 },
      { month: '2026-03', netWorthMdl: 45000 },
      { month: '2026-04', netWorthMdl: 46500 },
      { month: '2026-05', netWorthMdl: 48100 },
    ].slice(-months);
    return HttpResponse.json(series.map((p) => ({ ...p, missingFxRate: false })));
  }),
  http.get('*/dashboard/net-worth', () => HttpResponse.json(netWorth)),
  http.post('*/budgets/rebuild-all-periods', () =>
    HttpResponse.json({ budgetsRebuilt: 3, periodsAffected: 5 }),
  ),
  http.get('*/budgets', () => HttpResponse.json(budgets)),
  http.post('*/budgets', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.categoryId) {
      return HttpResponse.json({ error: 'Category is required' }, { status: 400 });
    }
    if (body.monthlyLimit === undefined || (body.monthlyLimit as number) <= 0) {
      return HttpResponse.json({ error: 'Monthly limit must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-budget-id' }, { status: 201 });
  }),
  http.put('*/budgets/:id', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (body.monthlyLimit === undefined || (body.monthlyLimit as number) <= 0) {
      return HttpResponse.json({ error: 'Monthly limit must be greater than 0' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/budgets/:id', () => new HttpResponse(null, { status: 204 })),
  http.get('*/goals', () => HttpResponse.json(goals)),
  http.get('*/goals/:id', ({ params }) => {
    const id = String(params.id);
    const detail = goalDetails[id];
    if (!detail) {
      return HttpResponse.json(
        { error: 'Goal not found', code: 'savings_goal.not_found' },
        { status: 404 },
      );
    }
    return HttpResponse.json(detail);
  }),
  http.post('*/goals', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    if (body.targetAmount === undefined || (body.targetAmount as number) <= 0) {
      return HttpResponse.json({ error: 'Target amount must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-goal-id' }, { status: 201 });
  }),
  http.put('*/goals/:id', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Name is required' }, { status: 400 });
    }
    if (body.targetAmount === undefined || (body.targetAmount as number) <= 0) {
      return HttpResponse.json({ error: 'Target amount must be greater than 0' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.patch('*/goals/:id/manual-saved', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (body.amount === undefined || (body.amount as number) < 0) {
      return HttpResponse.json({ error: 'Amount must be 0 or greater' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete('*/goals/:id', () => new HttpResponse(null, { status: 204 })),
  http.get('*/loans', ({ request }) => {
    const url = new URL(request.url);
    const includeArchived = url.searchParams.get('includeArchived') === 'true';
    const rows = includeArchived ? loans : loans.filter((l) => !l.isArchived);
    return HttpResponse.json(rows);
  }),
  http.get('*/loans/:id', ({ params }) => {
    const id = String(params.id);
    const detail = loanDetails[id];
    if (!detail) {
      return HttpResponse.json(
        { error: 'Loan not found', code: 'loan.not_found' },
        { status: 404 },
      );
    }
    return HttpResponse.json(detail);
  }),
  http.post('*/loans', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.counterparty || String(body.counterparty).trim().length === 0) {
      return HttpResponse.json({ error: 'Counterparty is required' }, { status: 400 });
    }
    if (body.principal === undefined || (body.principal as number) <= 0) {
      return HttpResponse.json({ error: 'Principal must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-loan-id' }, { status: 201 });
  }),
  http.put('*/loans/:id', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.counterparty || String(body.counterparty).trim().length === 0) {
      return HttpResponse.json({ error: 'Counterparty is required' }, { status: 400 });
    }
    return new HttpResponse(null, { status: 204 });
  }),
  http.post('*/loans/:id/payments', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (body.amount === undefined || (body.amount as number) <= 0) {
      return HttpResponse.json({ error: 'Amount must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-loan-payment-id' }, { status: 201 });
  }),
  http.delete('*/loans/:id/payments/:paymentId', () => new HttpResponse(null, { status: 204 })),
  http.delete('*/loans/:id', () => new HttpResponse(null, { status: 204 })),
  http.post('*/loans/:id/unarchive', () => new HttpResponse(null, { status: 204 })),
  http.get('*/pools', ({ request }) => {
    const url = new URL(request.url);
    const includeArchived = url.searchParams.get('includeArchived') === 'true';
    const rows = includeArchived ? pools : pools.filter((p) => !p.isArchived);
    return HttpResponse.json(rows);
  }),
  http.get('*/pools/:id', ({ params }) => {
    const id = String(params.id);
    const detail = poolDetails[id];
    if (!detail) {
      return HttpResponse.json(
        { error: 'Pool not found', code: 'pools.not_found' },
        { status: 404 },
      );
    }
    return HttpResponse.json(detail);
  }),
  http.post('*/pools', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Pool name is required' }, { status: 400 });
    }
    if (!body.accountId) {
      return HttpResponse.json({ error: 'Account is required' }, { status: 400 });
    }
    return HttpResponse.json(
      {
        id: 'created-pool-id',
        ownerParticipantId: 'created-owner-participant-id',
        seedUnits: 1000,
        markDelta: 0,
        markTransactionId: null,
        participants: [
          {
            id: 'created-owner-participant-id',
            name: String(body.ownerName),
            isOwner: true,
            units: 1000,
          },
        ],
      },
      { status: 201 },
    );
  }),
  http.post('*/pools/:id/participants', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.name || String(body.name).trim().length === 0) {
      return HttpResponse.json({ error: 'Participant name is required' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-participant-id' }, { status: 201 });
  }),
  http.post('*/pools/:id/subscriptions', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    // Mirrors the server-side rule the UI enforces client-side: no valuation,
    // no movement.
    if (!body.poolValueNow || (body.poolValueNow as number) <= 0) {
      return HttpResponse.json({ error: 'The exchange total is required' }, { status: 400 });
    }
    return HttpResponse.json(
      {
        eventId: 'created-subscription-event-id',
        units: 800,
        navPerUnit: 1.25,
        poolValuePreMoney: 2500,
        markDelta: 0,
        markTransactionId: null,
        movementTransactionId: 'created-subscription-tx-id',
      },
      { status: 201 },
    );
  }),
  http.post('*/pools/:id/redemptions', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.poolValueNow || (body.poolValueNow as number) <= 0) {
      return HttpResponse.json({ error: 'The exchange total is required' }, { status: 400 });
    }
    return HttpResponse.json(
      {
        eventId: 'created-redemption-event-id',
        units: 80,
        navPerUnit: 1.25,
        poolValuePreMoney: 2500,
        markDelta: 0,
        markTransactionId: null,
        movementTransactionId: 'created-redemption-tx-id',
        counterTransactionId: null,
      },
      { status: 201 },
    );
  }),
  http.post('*/pools/:id/distributions', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.poolValueNow || (body.poolValueNow as number) <= 0) {
      return HttpResponse.json({ error: 'The exchange total is required' }, { status: 400 });
    }
    return HttpResponse.json(
      {
        navPerUnit: 1.25,
        poolValuePreMoney: 2500,
        markDelta: 0,
        markTransactionId: null,
        totalCash: 100,
        lines: [
          {
            eventId: 'created-distribution-event-id',
            participantId: 'bbbb0001-0000-4000-8000-000000000002',
            participantName: 'Andrei',
            units: 80,
            cash: 100,
          },
        ],
      },
      { status: 201 },
    );
  }),
  http.post('*/pools/:id/distributions/:eventId/settle', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({
      transactionId: 'created-settlement-tx-id',
      settledOn: String(body.settledOn ?? '2026-09-03'),
      cash: 100,
    });
  }),
  http.post('*/pools/:id/cost-reimbursements', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (body.amount === undefined || (body.amount as number) <= 0) {
      return HttpResponse.json({ error: 'Amount must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({
      navPerUnit: 1.25,
      totalUnitsTransferred: 16,
      totalAmountRecovered: 20,
      markDelta: 0,
      markTransactionId: null,
      ownerEventId: 'created-cost-recovery-event-id',
      lines: [
        {
          eventId: 'created-cost-share-event-id',
          participantId: 'bbbb0001-0000-4000-8000-000000000002',
          participantName: 'Andrei',
          units: 8,
          amount: 10,
        },
      ],
    });
  }),
  http.delete('*/pools/:id/events/:eventId', () => new HttpResponse(null, { status: 204 })),
  http.post('*/pools/:id/archive', () => new HttpResponse(null, { status: 204 })),
  http.post('*/pools/:id/unarchive', () => new HttpResponse(null, { status: 204 })),
  // Hard delete. The happy default is the "created by mistake" pool; suites
  // that need the 409 (`pools.delete_has_movements`) or a 404 override it.
  http.delete('*/pools/:id', () => new HttpResponse(null, { status: 204 })),
  http.get('*/fx-rates', ({ request }) => {
    const url = new URL(request.url);
    const page = Number(url.searchParams.get('page') ?? '1');
    const pageSize = Number(url.searchParams.get('pageSize') ?? '25');
    const start = (page - 1) * pageSize;
    const items = fxRates.slice(start, start + pageSize);
    return HttpResponse.json({
      items,
      totalCount: fxRates.length,
      pageNumber: page,
      pageSize,
      totalPages: Math.ceil(fxRates.length / pageSize),
    });
  }),
  http.post('*/fx-rates/refresh', async () =>
    HttpResponse.json({ fetched: 3, inserted: 2, updated: 0, skipped: 1 }),
  ),
  http.post('*/fx-rates/backfill', async () =>
    HttpResponse.json({ daysProcessed: 30, fetched: 90, inserted: 12, updated: 3, skipped: 75 }),
  ),
  http.post('*/fx-rates', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    if (!body.fromCurrency || !body.toCurrency) {
      return HttpResponse.json({ error: 'Currencies are required' }, { status: 400 });
    }
    if (body.rate === undefined || (body.rate as number) <= 0) {
      return HttpResponse.json({ error: 'Rate must be greater than 0' }, { status: 400 });
    }
    return HttpResponse.json({ id: 'created-fx-rate-id' }, { status: 201 });
  }),
  http.delete('*/fx-rates/:id', () => new HttpResponse(null, { status: 204 })),
  // GET /fx-rates/convert — used by the transfer dialog + import preview to
  // pre-fill the destination/received amount. Default returns a hasRate=true
  // payload so tests that don't care about FX still pass; tests that assert a
  // specific pre-fill or the no-rate path override this via `server.use(...)`.
  http.get('*/fx-rates/convert', () =>
    HttpResponse.json({ convertedAmount: 1000, rate: 0.0582, hasRate: true }),
  ),

  // ----- Reports slice (mirrors the contract documented in FRONTEND.md) -----
  http.get('*/reports/monthly-summary', () =>
    HttpResponse.json([
      {
        month: '2026-03',
        income: 11000,
        expense: 8000,
        net: 3000,
        savingsRate: 3000 / 11000,
        transactionCount: 22,
        missingFxRate: false,
      },
      {
        month: '2026-04',
        income: 11500,
        expense: 8200,
        net: 3300,
        savingsRate: 3300 / 11500,
        transactionCount: 21,
        missingFxRate: false,
      },
      {
        month: '2026-05',
        income: 12000,
        expense: 8500,
        net: 3500,
        savingsRate: 3500 / 12000,
        transactionCount: 24,
        missingFxRate: false,
      },
    ]),
  ),
  http.get('*/reports/category-breakdown', ({ request }) => {
    const url = new URL(request.url);
    const direction = url.searchParams.get('direction') ?? 'Expense';
    return HttpResponse.json({
      from: url.searchParams.get('from') ?? '2026-05-01',
      to: url.searchParams.get('to') ?? '2026-05-23',
      direction,
      totalMdl: 4500,
      missingFxRate: false,
      items: [
        {
          categoryId: 'c0000001-0000-0000-0000-000000000001',
          categoryName: 'Groceries',
          amountMdl: 2000,
          percentage: 2000 / 4500,
          transactionCount: 12,
        },
        {
          categoryId: 'c0000001-0000-0000-0000-000000000002',
          categoryName: 'Dining out',
          amountMdl: 1500,
          percentage: 1500 / 4500,
          transactionCount: 8,
        },
        {
          categoryId: null,
          categoryName: 'Uncategorized',
          amountMdl: 1000,
          percentage: 1000 / 4500,
          transactionCount: 5,
        },
      ],
    });
  }),
  http.get('*/reports/top-payees', ({ request }) => {
    const url = new URL(request.url);
    const limit = Number(url.searchParams.get('limit') ?? '10');
    const rows = [
      {
        payee: 'Linella',
        originalDescription: 'LINELLA SRL CHISINAU',
        amountMdl: 1800,
        transactionCount: 11,
      },
      {
        payee: 'Tucano',
        originalDescription: 'TUCANO COFFEE',
        amountMdl: 950,
        transactionCount: 14,
      },
      {
        payee: 'Andy',
        originalDescription: 'ANDYS PIZZA',
        amountMdl: 720,
        transactionCount: 6,
      },
    ];
    return HttpResponse.json(rows.slice(0, limit));
  }),
  http.get('*/reports/balance-over-time', ({ request }) => {
    const url = new URL(request.url);
    const accountId = url.searchParams.get('accountId') ?? '';
    // Mirror the seeded XTB account (USD) to exercise the dual-line code path.
    // For MDL accounts the MDL-eq equals the native balance.
    const isUsd = accountId === '44444444-4444-4444-4444-444444444444';
    return HttpResponse.json([
      {
        asOf: '2026-03-31',
        balance: isUsd ? 1400 : 21000,
        balanceMdl: isUsd ? 24500 : 21000,
        missingFxRate: false,
      },
      {
        asOf: '2026-04-30',
        balance: isUsd ? 1450 : 21800,
        balanceMdl: isUsd ? 25375 : 21800,
        missingFxRate: false,
      },
      {
        asOf: '2026-05-23',
        balance: isUsd ? 1500 : 22550,
        balanceMdl: isUsd ? 26250 : 22550,
        missingFxRate: false,
      },
    ]);
  }),
  // POST /imports/parse — returns a StatementPreviewDto. maib books commissions
  // INSIDE `totalOut`, and `closingBalance` is maib's "Sold Disponibil" = the
  // true balance = opening + in − out. So the summary ties WITHOUT subtracting
  // fees again: opening (1000) + in (500) − out (300) = closing (1200). The 125
  // of `totalFees` is a subset of Out (the "Comision:" row below).
  http.post('*/imports/parse', () =>
    HttpResponse.json({
      fileHash: 'mock-parse-hash',
      statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
      bankSource: 'Maib',
      summary: {
        openingBalance: 1000,
        closingBalance: 1200,
        totalIn: 500,
        totalOut: 300,
        totalFees: 125,
      },
      transactions: [
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 175,
          description: 'LINELLA SRL CHISINAU',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 125,
          description: 'Comision: card maintenance',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          transactionDate: '2025-05-20',
          direction: 'Income',
          amount: 500,
          description: 'SALARY DEPOSIT',
          isDuplicate: false,
          isTransfer: false,
        },
      ],
    } satisfies StatementPreviewDto),
  ),
  http.post('*/data/import', () =>
    HttpResponse.json({
      schemaVersion: 2,
      accounts: 4,
      categories: 9,
      transactions: 128,
      importBatches: 3,
      budgets: 6,
      budgetPeriods: 6,
      savingsGoals: 2,
      savingsGoalContributions: 7,
    }),
  ),
];
