import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { PoolDetailView } from '@/src/components/pools/detail/pool-detail-view';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { PoolDetailDto } from '@/src/types/api';

// The header pushes back to /pools after a delete. jsdom has no App-Router
// context, so `useRouter` needs a stub — same treatment the account and loan
// detail suites use.
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

const POOL_ID = 'aaaa0001-0000-4000-8000-000000000001';
const ARCHIVED_POOL_ID = 'aaaa0001-0000-4000-8000-000000000002';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      {ui}
      <Toaster />
    </QueryClientProvider>,
  );
}

/** Fetches the seeded detail and hands back a mutated copy, so overrides stay minimal. */
async function seededDetail(): Promise<PoolDetailDto> {
  const res = await fetch('http://x/pools/aaaa0001-0000-4000-8000-000000000001');
  return (await res.json()) as PoolDetailDto;
}

function serveDetail(detail: PoolDetailDto) {
  server.use(http.get('*/pools/:id', () => HttpResponse.json(detail)));
}

describe('PoolDetailView — layout and states', () => {
  it('renders the header, value card, roster and ledger', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await waitFor(() => expect(screen.getByTestId('pool-detail-view')).toBeInTheDocument());
    expect(screen.getByTestId('pool-detail-name')).toHaveTextContent('Binance pool');
    expect(screen.getByTestId('pool-detail-account-link')).toHaveTextContent('Binanance');
    expect(screen.getByTestId('pool-value-card')).toBeInTheDocument();
    expect(screen.getByTestId('pool-participants-table')).toBeInTheDocument();
    expect(screen.getByTestId('pool-events-table')).toBeInTheDocument();
  });

  it('renders the loading skeleton first', () => {
    server.use(
      http.get('*/pools/:id', () => new Promise(() => {}) as unknown as Promise<Response>),
    );
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    expect(screen.getByTestId('pool-detail-skeleton')).toBeInTheDocument();
  });

  it('renders a distinct not-found state on 404', async () => {
    renderWithClient(<PoolDetailView id="00000000-0000-0000-0000-000000000000" />);
    const error = await screen.findByTestId('pool-detail-error');
    expect(error).toHaveAttribute('data-not-found', 'true');
    expect(screen.getByText('Pool not found.')).toBeInTheDocument();
  });

  it('falls back to the generic error state on any other failure', async () => {
    server.use(http.get('*/pools/:id', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    const error = await screen.findByTestId('pool-detail-error');
    expect(error).toHaveAttribute('data-not-found', 'false');
    expect(screen.getByText('Failed to load pool.')).toBeInTheDocument();
  });

  it('stays drillable when archived but drops every money-moving action', async () => {
    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);
    await waitFor(() => expect(screen.getByTestId('pool-detail-archived')).toBeInTheDocument());
    expect(screen.queryByTestId('pool-detail-close-month')).not.toBeInTheDocument();
    expect(screen.queryByTestId('pool-detail-record-subscription')).not.toBeInTheDocument();
    expect(screen.queryByTestId('pool-detail-archive')).not.toBeInTheDocument();
    // What replaces them: the two lifecycle actions.
    expect(screen.getByTestId('pool-detail-unarchive')).toBeInTheDocument();
    expect(screen.getByTestId('pool-detail-delete')).toBeInTheDocument();
  });
});

describe('PoolDetailView — value card', () => {
  it('shows the pool value, the split and the price per share', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await waitFor(() => expect(screen.getByTestId('pool-detail-value')).toBeInTheDocument());
    expect(screen.getByTestId('pool-detail-value').textContent).toBe(formatMoney(2500, 'USD'));
    // 2,500 pool − 2,000 theirs = 500 yours; the two halves add to the whole.
    expect(screen.getByTestId('pool-detail-your-share-amount').textContent).toBe(
      formatMoney(500, 'USD'),
    );
    expect(screen.getByTestId('pool-detail-outside-capital-amount').textContent).toBe(
      formatMoney(2000, 'USD'),
    );
    expect(screen.getByTestId('pool-detail-your-share-percent')).toHaveTextContent('20.00%');
    expect(screen.getByTestId('pool-detail-nav-amount').textContent).toBe(formatMoney(1.25, 'USD'));
  });

  it('separates the account balance from the pool value while a payout is owed', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const owed = await screen.findByTestId('pool-detail-owed-cash');
    expect(within(owed).getByTestId('pool-detail-account-balance').textContent).toBe(
      formatMoney(2600, 'USD'),
    );
    expect(within(owed).getByTestId('pool-detail-unpaid-cash').textContent).toBe(
      `−${formatMoney(100, 'USD')}`,
    );
    expect(owed).toHaveTextContent(/already owed/i);
  });

  it('hides the owed-cash block once nothing is outstanding', async () => {
    const detail = await seededDetail();
    serveDetail({
      ...detail,
      accountBalance: 2500,
      unpaidDistributionCash: 0,
      unpaidDistributionCount: 0,
    });
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await screen.findByTestId('pool-detail-value');
    expect(screen.queryByTestId('pool-detail-owed-cash')).not.toBeInTheDocument();
  });

  it('renders an unconvertible MDL equivalent as an em dash, never as 0', async () => {
    const detail = await seededDetail();
    serveDetail({ ...detail, currency: 'GBP', poolValueMdl: null, missingFxRate: true });
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const line = await screen.findByTestId('pool-detail-value-mdl');
    expect(within(line).getByTestId('pool-detail-missing-fx')).toBeInTheDocument();
    expect(line.textContent).not.toMatch(/0[.,]00\s*MDL/);
  });
});

describe('PoolDetailView — mark staleness', () => {
  it('says nothing while the valuation is fresh', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await screen.findByTestId('pool-detail-value');
    expect(screen.queryByTestId('pool-mark-staleness')).not.toBeInTheDocument();
  });

  it('warns in amber from a week old', async () => {
    const detail = await seededDetail();
    serveDetail({ ...detail, lastMarkDate: '2026-08-25', markAgeDays: 9 });
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const warning = await screen.findByTestId('pool-mark-staleness');
    expect(warning).toHaveAttribute('data-level', 'stale');
    expect(warning).toHaveTextContent('Value last confirmed 9 days ago');
  });

  it('escalates to red past a month', async () => {
    const detail = await seededDetail();
    serveDetail({ ...detail, lastMarkDate: '2026-07-01', markAgeDays: 64 });
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const warning = await screen.findByTestId('pool-mark-staleness');
    expect(warning).toHaveAttribute('data-level', 'critical');
    expect(warning.className).toMatch(/destructive/);
  });

  it('treats a pool that has never been valued as the worst case', async () => {
    const detail = await seededDetail();
    serveDetail({ ...detail, lastMarkDate: null, markAgeDays: null });
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const warning = await screen.findByTestId('pool-mark-staleness');
    expect(warning).toHaveAttribute('data-level', 'never');
    expect(warning).toHaveTextContent('This pool has never been valued.');
  });
});

describe('PoolDetailView — participants', () => {
  it('speaks in shares and percentages, and marks the owner', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await waitFor(() => expect(screen.getAllByTestId('pool-participant-row').length).toBe(3));
    const rows = screen.getAllByTestId('pool-participant-row');
    const owner = rows.find((r) => r.getAttribute('data-owner') === 'true') as HTMLElement;
    expect(within(owner).getByTestId('pool-participant-owner-badge')).toBeInTheDocument();
    expect(within(owner).getByTestId('pool-participant-share').textContent).toBe('20.00%');
  });

  it('explains a zero payable rather than printing a bare 0.00', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    // Bogdan sits exactly on his capital base: nothing payable, however green
    // the month was. This is the most disputable behaviour in the design, so
    // it must never render as an unexplained zero.
    const roster = await screen.findByTestId('pool-participants-table');
    const bogdan = within(roster).getByText('Bogdan').closest('tr') as HTMLElement;
    const cell = within(bogdan).getByTestId('pool-participant-distributable-zero');
    expect(cell).toHaveTextContent('Nothing payable — still at or below what they put in');
    expect(within(bogdan).queryByTestId('pool-participant-distributable')).not.toBeInTheDocument();
  });

  it('shows a real payable amount for someone above their basis', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    const roster = await screen.findByTestId('pool-participants-table');
    const andrei = within(roster).getByText('Andrei').closest('tr') as HTMLElement;
    expect(within(andrei).getByTestId('pool-participant-distributable').textContent).toBe(
      formatMoney(100, 'USD'),
    );
  });

  it("never offers the owner's own stake as a payout", async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-participant-row').length).toBe(3));
    expect(screen.getByTestId('pool-participant-distributable-owner')).toHaveTextContent(
      'Stays in the pool',
    );
  });

  it('renders the MDL equivalent of a stake and the amber flag when there is no rate', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-participant-row').length).toBe(3));
    expect(screen.getAllByTestId('pool-participant-stake-mdl')[0]?.textContent).toBe(
      formatMoney(8750, 'MDL'),
    );

    const detail = await seededDetail();
    const [owner, ...rest] = detail.participants;
    serveDetail({
      ...detail,
      participants: [
        { ...(owner as NonNullable<typeof owner>), stakeMdl: null, missingFxRate: true },
        ...rest,
      ],
    });
  });

  it('values nothing when the pool has no shares outstanding', async () => {
    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-participant-row').length).toBe(1));
    expect(screen.getByTestId('pool-participant-stake').textContent).toBe('—');
  });
});

describe('PoolDetailView — reconciliation tripwire', () => {
  it('stays out of the way while everything reconciles', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await screen.findByTestId('pool-detail-value');
    expect(screen.queryByTestId('pool-reconciliation-panel')).not.toBeInTheDocument();
  });

  it('surfaces every finding class and promises no repair', async () => {
    const detail = await seededDetail();
    serveDetail({
      ...detail,
      reconciliation: {
        isClean: false,
        unmatchedTransactions: [
          {
            transactionId: 'tx-1',
            transactionDate: '2026-08-25',
            description: 'P2P withdrawal',
            direction: 'Expense',
            amount: 300,
            currency: 'USD',
            isTransfer: false,
          },
        ],
        participantUnits: 2000,
        ledgerUnits: 1990,
        unitsDrift: 10,
        unitsBalance: false,
        valueDrifts: [
          {
            eventId: 'cccc0001-0000-4000-8000-000000000002',
            occurredOn: '2026-08-10',
            kind: 'Subscription',
            recordedPreMoney: 1500,
            derivedPreMoney: 1200,
            drift: 300,
          },
        ],
        unbackedCashClaims: [
          {
            eventId: 'cccc0001-0000-4000-8000-000000000003',
            settledOn: '2026-08-20',
            direction: 'Expense',
            amount: 120,
          },
        ],
        // Deliberately NOT the sum of the two cash findings (-120 + 300 = 180),
        // so the cross-reference has to take its cautious branch.
        predictedBalance: 2300,
        derivedBalance: 2100,
        balanceDrift: 200,
        balanceReconciles: false,
      },
    });

    renderWithClient(<PoolDetailView id={POOL_ID} />);

    const panel = await screen.findByTestId('pool-reconciliation-panel');
    expect(within(panel).getByTestId('reconciliation-unmatched')).toHaveTextContent(
      'P2P withdrawal',
    );
    expect(within(panel).getByTestId('reconciliation-units-drift-value').textContent).toBe(
      '10.0000',
    );
    expect(within(panel).getByTestId('reconciliation-value-drift-row')).toHaveTextContent(
      'Money in',
    );
    expect(within(panel).getByTestId('reconciliation-unbacked-row')).toHaveTextContent('Money out');
    expect(within(panel).getByTestId('reconciliation-balance-drift').textContent).toBe(
      formatMoney(200, 'USD'),
    );
    // The gap and the cash findings don't tie out, so it points rather than promises.
    expect(within(panel).getByTestId('reconciliation-balance-related')).toHaveTextContent(
      /Read this together with the other findings listed below/i,
    );
    // It reports; it never corrects. The copy has to say so.
    expect(panel).toHaveTextContent(/this is a report/i);
    expect(panel).toHaveTextContent(/Nothing below has been changed or repaired/i);
  });

  it('shows only the classes that actually have findings', async () => {
    const detail = await seededDetail();
    serveDetail({
      ...detail,
      reconciliation: {
        isClean: false,
        unmatchedTransactions: [],
        participantUnits: 2000,
        ledgerUnits: 1999,
        unitsDrift: 1,
        unitsBalance: false,
        valueDrifts: [],
        unbackedCashClaims: [],
        predictedBalance: 2600,
        derivedBalance: 2600,
        balanceDrift: 0,
        balanceReconciles: true,
      },
    });
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await screen.findByTestId('reconciliation-units-drift');
    expect(screen.queryByTestId('reconciliation-unmatched')).not.toBeInTheDocument();
    expect(screen.queryByTestId('reconciliation-value-drift')).not.toBeInTheDocument();
    expect(screen.queryByTestId('reconciliation-unbacked')).not.toBeInTheDocument();
    // The panel is open on another finding, and the balances agree. Saying
    // anything about them here would be inventing a second problem.
    expect(screen.queryByTestId('reconciliation-balance')).not.toBeInTheDocument();
  });
});

/**
 * The fifth check: what the ledger says the account should be holding, against
 * what it holds. This is the one that would have caught the live incident, so
 * the tests are written around the shape that got past the other four.
 */
describe('PoolDetailView — the balance the ledger predicts', () => {
  type Reconciliation = PoolDetailDto['reconciliation'];

  /** Every other check passing, so each assertion is about this finding alone. */
  function balanceFinding(over: Partial<Reconciliation>): Reconciliation {
    return {
      isClean: false,
      unmatchedTransactions: [],
      participantUnits: 3000,
      ledgerUnits: 3000,
      unitsDrift: 0,
      unitsBalance: true,
      valueDrifts: [],
      unbackedCashClaims: [],
      predictedBalance: 0,
      derivedBalance: 0,
      balanceDrift: 0,
      balanceReconciles: false,
      ...over,
    };
  }

  /** The phantom arrival from the live incident: 1,000 in, no transaction. */
  const PHANTOM_ARRIVAL = {
    eventId: 'cccc0001-0000-4000-8000-000000000009',
    settledOn: '2026-06-01',
    direction: 'Income',
    amount: 1000,
  } as const;

  async function serveBalance(over: Partial<Reconciliation>) {
    const detail = await seededDetail();
    serveDetail({ ...detail, reconciliation: balanceFinding(over) });
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    return screen.findByTestId('reconciliation-balance');
  }

  it('names the live incident in the numbers the user is looking at', async () => {
    const finding = await serveBalance({
      predictedBalance: 3000,
      derivedBalance: 2000,
      balanceDrift: 1000,
      unbackedCashClaims: [PHANTOM_ARRIVAL],
    });

    expect(finding).toHaveAttribute('data-direction', 'ledger-claims-more');
    expect(finding).toHaveTextContent('This pool has issued shares for money that never arrived');

    expect(within(finding).getByTestId('reconciliation-balance-predicted').textContent).toBe(
      formatMoney(3000, 'USD'),
    );
    expect(within(finding).getByTestId('reconciliation-balance-derived').textContent).toBe(
      formatMoney(2000, 'USD'),
    );
    expect(within(finding).getByTestId('reconciliation-balance-drift').textContent).toBe(
      formatMoney(1000, 'USD'),
    );
    // Pinned as the user reads it, not as the API sends it: app-wide ro-MD
    // grouping with the ISO code, and no bare minus sign in front of the gap.
    expect(within(finding).getByTestId('reconciliation-balance-drift')).toHaveTextContent(
      '1.000,00 USD',
    );

    // What it means and what to do about it, not just that it happened.
    expect(finding).toHaveTextContent(/should be holding/i);
    expect(finding).toHaveTextContent(/an arrival recorded while the pool was being set up/i);
    expect(finding).toHaveTextContent(/add it, dated the day the money actually arrived/i);
  });

  it('relates the gap to the entry behind it rather than reporting two problems', async () => {
    const finding = await serveBalance({
      predictedBalance: 3000,
      derivedBalance: 2000,
      balanceDrift: 1000,
      unbackedCashClaims: [PHANTOM_ARRIVAL],
    });

    const note = within(finding).getByTestId('reconciliation-balance-related');
    expect(note).toHaveTextContent(/One problem, not two/i);
    expect(note).toHaveTextContent(/claim money the account never saw/i);
    expect(note).toHaveTextContent(/this gap is accounted for exactly by/i);
  });

  it('reads the other way round when the account holds the unexplained money', async () => {
    const finding = await serveBalance({
      predictedBalance: 1800,
      derivedBalance: 2000,
      balanceDrift: -200,
      unmatchedTransactions: [
        {
          transactionId: 'tx-9',
          transactionDate: '2026-08-30',
          description: 'Manual deposit',
          direction: 'Income',
          amount: 200,
          currency: 'USD',
          isTransfer: false,
        },
      ],
    });

    expect(finding).toHaveAttribute('data-direction', 'account-holds-more');
    expect(finding).toHaveTextContent("The account holds money this pool can't account for");
    // The opposite reading must not leak into this one.
    expect(finding).not.toHaveTextContent(/issued shares for money that never arrived/i);
    expect(finding).not.toHaveTextContent(/not owed to anyone/i);

    expect(within(finding).getByTestId('reconciliation-balance-predicted').textContent).toBe(
      formatMoney(1800, 'USD'),
    );
    expect(within(finding).getByTestId('reconciliation-balance-derived').textContent).toBe(
      formatMoney(2000, 'USD'),
    );
    // Unsigned, with the direction carried by the label instead.
    expect(within(finding).getByTestId('reconciliation-balance-drift').textContent).toBe(
      formatMoney(200, 'USD'),
    );
    expect(finding).toHaveTextContent(/Unaccounted for on the account/i);

    expect(finding).toHaveTextContent(/split pro-rata across everyone in the pool/i);
    expect(finding).toHaveTextContent(/count the same cash twice/i);

    const note = within(finding).getByTestId('reconciliation-balance-related');
    expect(note).toHaveTextContent(/One problem, not two/i);
    expect(note).toHaveTextContent(/no ledger entry behind them/i);
  });

  it('will not claim the gap is explained when it cannot do the arithmetic', async () => {
    const finding = await serveBalance({
      predictedBalance: 1800,
      derivedBalance: 2000,
      balanceDrift: -200,
      // The same 200, denominated in something else. The totals would tie if we
      // summed them blindly, which is exactly why we don't.
      unmatchedTransactions: [
        {
          transactionId: 'tx-9',
          transactionDate: '2026-08-30',
          description: 'Manual deposit',
          direction: 'Income',
          amount: 200,
          currency: 'EUR',
          isTransfer: false,
        },
      ],
    });

    const note = within(finding).getByTestId('reconciliation-balance-related');
    expect(note).toHaveTextContent(/Read this together with/i);
    expect(note).not.toHaveTextContent(/exactly/i);
  });

  it('says nothing about other findings when there are none to relate it to', async () => {
    const finding = await serveBalance({
      predictedBalance: 3000,
      derivedBalance: 2000,
      balanceDrift: 1000,
    });

    expect(within(finding).queryByTestId('reconciliation-balance-related')).not.toBeInTheDocument();
  });
});

describe('PoolDetailView — ledger', () => {
  it('lists every entry newest first with its human label', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await waitFor(() => expect(screen.getAllByTestId('pool-event-row').length).toBe(4));
    const kinds = screen.getAllByTestId('pool-event-kind').map((n) => n.textContent);
    expect(kinds).toEqual(['Profit payout', 'Money out', 'Money in', 'Opening stake']);
  });

  it('is the one place share counts and prices per share appear', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-row').length).toBe(4));

    expect(screen.getAllByTestId('pool-event-units')[0]?.textContent).toBe('−80.0000');
    expect(screen.getAllByTestId('pool-event-nav')[0]?.textContent).toBe('1.250000');
    // The roster above speaks percentages only.
    expect(screen.getByTestId('pool-participants-table').textContent).not.toMatch(/2,000\.0000/);
  });

  it('shows no cash for an entry that only moved shares', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-row').length).toBe(4));
    // The seed carries no cash leg — the owner's existing balance became shares.
    const seedRow = screen
      .getAllByTestId('pool-event-row')
      .find((r) => r.getAttribute('data-kind') === 'Seed') as HTMLElement;
    expect(within(seedRow).getByTestId('pool-event-cash').textContent).toBe('—');
  });

  it('badges an unpaid payout and offers Settle only there', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);

    await waitFor(() => expect(screen.getAllByTestId('pool-event-row').length).toBe(4));
    expect(screen.getAllByTestId('pool-event-unpaid-badge').length).toBe(1);
    expect(screen.getAllByTestId('pool-event-settle').length).toBe(1);
  });

  it('opens the Settle dialog and records the day the transfer actually left', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions/:eventId/settle', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          transactionId: 'tx',
          settledOn: '2026-09-02',
          cash: 100,
        });
      }),
    );

    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-settle').length).toBe(1));
    await user.click(screen.getByTestId('pool-event-settle'));

    const dialog = await screen.findByTestId('settle-payout-dialog');
    expect(within(dialog).getByTestId('settle-payout-context')).toHaveTextContent('Andrei');

    const date = within(dialog).getByTestId('settle-date-input');
    fireEvent.change(date, { target: { value: '2026-09-02' } });
    await user.click(within(dialog).getByTestId('settle-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ settledOn: '2026-09-02' });
  });

  it('refuses a settlement dated before the month was closed', async () => {
    const user = userEvent.setup();
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-settle').length).toBe(1));
    await user.click(screen.getByTestId('pool-event-settle'));

    const dialog = await screen.findByTestId('settle-payout-dialog');
    const date = within(dialog).getByTestId('settle-date-input') as HTMLInputElement;
    // The input carries a native `min` of the close date, which jsdom enforces
    // by refusing to submit at all. Strip it so the Zod refine behind it is the
    // thing under test — same convention as the record-payment date tests.
    expect(date.min).toBe('2026-08-31');
    date.removeAttribute('min');
    fireEvent.change(date, { target: { value: '2026-08-01' } });
    await user.click(within(dialog).getByTestId('settle-submit-button'));

    expect(await within(dialog).findByTestId('settle-date-error')).toHaveTextContent(
      'The transfer cannot have left before the month was closed',
    );
  });

  it('never offers to delete the opening stake', async () => {
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-row').length).toBe(4));
    // Four rows, three delete buttons — the seed has none.
    expect(screen.getAllByTestId('pool-event-delete').length).toBe(3);
  });

  it('warns that deleting an entry re-prices history and cannot be repaired', async () => {
    const user = userEvent.setup();
    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await waitFor(() => expect(screen.getAllByTestId('pool-event-delete').length).toBe(3));

    // The redemption row carries a linked transaction, so the copy must also
    // say the account row goes with it.
    await user.click(screen.getAllByTestId('pool-event-delete')[1] as HTMLElement);
    const dialog = await screen.findByTestId('delete-pool-event-dialog');
    expect(within(dialog).getByTestId('delete-pool-event-warning')).toHaveTextContent(
      /re-prices history/i,
    );
    expect(dialog).toHaveTextContent('Binanance');

    await user.click(within(dialog).getByTestId('delete-pool-event-confirm'));
    await waitFor(() =>
      expect(screen.queryByTestId('delete-pool-event-dialog')).not.toBeInTheDocument(),
    );
  });

  it('renders an empty ledger without breaking', async () => {
    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);
    expect(await screen.findByTestId('pool-events-empty')).toBeInTheDocument();
  });
});
