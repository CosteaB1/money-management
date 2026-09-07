import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { AddParticipantDialog } from '@/src/components/pools/add-participant-dialog';
import { CloseMonthDialog } from '@/src/components/pools/close-month-dialog';
import { PoolDetailView } from '@/src/components/pools/detail/pool-detail-view';
import { RecordCostReimbursementDialog } from '@/src/components/pools/record-cost-reimbursement-dialog';
import { RecordRedemptionDialog } from '@/src/components/pools/record-redemption-dialog';
import { RecordSubscriptionDialog } from '@/src/components/pools/record-subscription-dialog';
import { SettlePayoutDialog } from '@/src/components/pools/settle-payout-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { PoolDetailDto, PoolParticipantDto, PoolUnitEventDto } from '@/src/types/api';

/**
 * Every write route on the pool slice is guarded server-side — the guard table
 * is the design's precondition, not a nicety — so a 409 is an ordinary
 * outcome, not an exceptional one. Each dialog has to show that message where
 * the user is looking and keep the form open with their input intact, rather
 * than closing on a failure or burying it in a toast that disappears.
 */

const POOL_ID = 'aaaa0001-0000-4000-8000-000000000001';
const OWNER_ID = 'bbbb0001-0000-4000-8000-000000000001';
const ANDREI_ID = 'bbbb0001-0000-4000-8000-000000000002';

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

function participant(overrides: Partial<PoolParticipantDto>): PoolParticipantDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    name: 'Somebody',
    isOwner: false,
    isArchived: false,
    joinedOn: '2026-08-05',
    units: 800,
    ownershipPercent: 40,
    stake: 1000,
    stakeMdl: 17500,
    capitalBase: 900,
    distributable: 100,
    unpaidDistributionCash: 0,
    unpaidDistributionCount: 0,
    missingFxRate: false,
    ...overrides,
  };
}

function pool(overrides: Partial<PoolDetailDto> = {}): PoolDetailDto {
  return {
    id: POOL_ID,
    accountId: '66666666-6666-6666-6666-666666666666',
    accountName: 'Binanance',
    accountCurrency: 'USD',
    accountIsArchived: false,
    name: 'Binance pool',
    currency: 'USD',
    inceptionDate: '2026-08-01',
    notes: null,
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
    ownerParticipantId: OWNER_ID,
    ownerFraction: 0.2,
    outsideCapital: 2000,
    outsideCapitalMdl: 35000,
    missingFxRate: false,
    lastMarkDate: '2026-08-31',
    markAgeDays: 3,
    participants: [
      participant({ id: OWNER_ID, name: 'Me', isOwner: true, capitalBase: 0, distributable: 500 }),
      participant({ id: ANDREI_ID, name: 'Andrei', distributable: 100 }),
    ],
    events: [],
    reconciliation: {
      isClean: true,
      unmatchedTransactions: [],
      participantUnits: 2000,
      ledgerUnits: 2000,
      unitsDrift: 0,
      unitsBalance: true,
      valueDrifts: [],
      unbackedCashClaims: [],
    },
    ...overrides,
  };
}

const unpaidPayout: PoolUnitEventDto = {
  id: 'cccc0001-0000-4000-8000-000000000004',
  participantId: ANDREI_ID,
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
  notes: null,
};

const noop = () => {};

describe('a guard rejection lands inline, next to the form', () => {
  it('redemption', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/redemptions', () =>
        HttpResponse.json({ detail: 'That would overdraw their share.' }, { status: 409 }),
      ),
    );
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '9999' } });
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(await screen.findByTestId('record-redemption-error')).toHaveTextContent(
      'That would overdraw their share.',
    );
  });

  it('close month', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/distributions', () =>
        HttpResponse.json({ detail: 'Nothing is payable.' }, { status: 409 }),
      ),
    );
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    expect(await screen.findByTestId('close-month-error')).toHaveTextContent('Nothing is payable.');
  });

  it('cost reimbursement', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/cost-reimbursements', () =>
        HttpResponse.json({ detail: 'The pool holds no shares.' }, { status: 409 }),
      ),
    );
    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    await user.click(screen.getByTestId('cost-submit-button'));

    expect(await screen.findByTestId('cost-reimbursement-error')).toHaveTextContent(
      'The pool holds no shares.',
    );
  });

  it('add participant', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/participants', () =>
        HttpResponse.json({ detail: 'That name is already taken.' }, { status: 409 }),
      ),
    );
    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="P" open onOpenChange={noop} />,
    );

    await user.type(screen.getByTestId('participant-name-input'), 'Andrei');
    await user.click(screen.getByTestId('participant-submit-button'));

    expect(await screen.findByTestId('add-participant-error')).toHaveTextContent(
      'That name is already taken.',
    );
  });

  it('settle payout', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/distributions/:eventId/settle', () =>
        HttpResponse.json({ detail: 'That payout is already settled.' }, { status: 409 }),
      ),
    );
    renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unpaidPayout}
        open
        onOpenChange={noop}
      />,
    );

    await user.click(screen.getByTestId('settle-submit-button'));
    expect(await screen.findByTestId('settle-payout-error')).toHaveTextContent(
      'That payout is already settled.',
    );
  });
});

describe('amount validation', () => {
  it('rejects a zero subscription', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByTestId('subscription-cash-error')).toHaveTextContent(
      'Amount must be greater than 0',
    );
  });

  it('rejects a zero redemption', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(await screen.findByTestId('redemption-cash-error')).toHaveTextContent(
      'Amount must be greater than 0',
    );
  });
});

describe('notes ride along when they are filled in', () => {
  it('sends the note on a subscription and shows the pending label while in flight', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/subscriptions', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        await new Promise((r) => setTimeout(r, 60));
        return HttpResponse.json(
          {
            eventId: 'e',
            units: 8,
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            movementTransactionId: 'tx',
          },
          { status: 201 },
        );
      }),
    );

    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '10' } });
    fireEvent.change(screen.getByTestId('subscription-notes-input'), {
      target: { value: 'Top-up' },
    });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByText('Recording...')).toBeInTheDocument();
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ notes: 'Top-up' });
  });

  it('sends the note on a redemption, with the destination account', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/redemptions', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            eventId: 'e',
            units: 8,
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            movementTransactionId: 'tx',
            counterTransactionId: 'tx2',
          },
          { status: 201 },
        );
      }),
    );

    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Me (you)' }));
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '400' } });
    await user.click(screen.getByTestId('redemption-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));
    fireEvent.change(screen.getByTestId('redemption-notes-input'), {
      target: { value: 'Out to Bybit' },
    });
    await user.click(screen.getByTestId('redemption-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      notes: 'Out to Bybit',
      destinationAccountId: '44444444-4444-4444-4444-444444444444',
    });
  });

  it('sends the note on a close and on a settlement', async () => {
    const user = userEvent.setup();
    let closeBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions', async ({ request }) => {
        closeBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            totalCash: 100,
            lines: [
              {
                eventId: 'e',
                participantId: ANDREI_ID,
                participantName: 'Andrei',
                units: 80,
                cash: 100,
              },
            ],
          },
          { status: 201 },
        );
      }),
    );

    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    fireEvent.change(screen.getByTestId('close-notes-input'), { target: { value: 'August' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    await waitFor(() => expect(closeBody).not.toBeNull());
    expect(closeBody).toMatchObject({ notes: 'August' });
  });

  it('sends the note on a settlement and on a cost', async () => {
    const user = userEvent.setup();
    let settleBody: Record<string, unknown> | null = null;
    let costBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions/:eventId/settle', async ({ request }) => {
        settleBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          transactionId: 'tx',
          settledOn: '2026-09-03',
          cash: 100,
        });
      }),
      http.post('*/pools/:id/cost-reimbursements', async ({ request }) => {
        costBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          navPerUnit: 1.25,
          totalUnitsTransferred: 16,
          totalAmountRecovered: 20,
          markDelta: 0,
          markTransactionId: null,
          ownerEventId: 'e',
          lines: [],
        });
      }),
    );

    const { unmount } = renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unpaidPayout}
        open
        onOpenChange={noop}
      />,
    );
    fireEvent.change(screen.getByTestId('settle-notes-input'), { target: { value: 'Sent' } });
    await user.click(screen.getByTestId('settle-submit-button'));
    await waitFor(() => expect(settleBody).not.toBeNull());
    expect(settleBody).toMatchObject({ notes: 'Sent' });
    unmount();

    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    fireEvent.change(screen.getByTestId('cost-notes-input'), { target: { value: 'VPS' } });
    await user.click(screen.getByTestId('cost-submit-button'));
    await waitFor(() => expect(costBody).not.toBeNull());
    expect(costBody).toMatchObject({ notes: 'VPS' });
  });
});

describe('PoolDetailView defensive path', () => {
  it('renders the generic error when the query is disabled by an empty id', () => {
    // No loading, no error, no data — the branch that stops the page silently
    // rendering nothing. Mirrors the loan-detail-view test of the same shape.
    renderWithClient(<PoolDetailView id="" />);
    const error = screen.getByTestId('pool-detail-error');
    expect(error).toHaveAttribute('data-not-found', 'false');
  });
});

describe('RecordRedemptionDialog destination picker', () => {
  it('says so when there is nowhere else in the same currency to send it', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <RecordRedemptionDialog pool={pool({ currency: 'GBP' })} open onOpenChange={noop} />,
    );

    await user.click(screen.getByTestId('redemption-destination-select'));
    expect(await screen.findByText('No other GBP accounts available.')).toBeInTheDocument();
  });

  it('quotes the selected holder against the pool’s last known value before anything is typed', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));

    // 800 shares at the API's 1.25 mark = 1000. Fine to show — but it is a
    // statement about the LAST mark, and saying "currently holds" under a
    // field demanding this minute's exchange total claims a freshness the
    // figure does not have.
    const hint = screen.getByTestId('redemption-stake-hint');
    expect(hint).toHaveTextContent('Andrei held');
    expect(hint).toHaveTextContent(/1[.,]000/);
    expect(hint).toHaveTextContent(/last known value/i);
    expect(hint).not.toHaveTextContent(/currently holds/i);
  });

  it('reprices the quoted share against the total being typed', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));

    // 2000 typed − 100 closed but unpaid = 1900 over 2000 shares → NAV 0.95,
    // so Andrei's 800 shares are worth 760, not the 1000 the API priced at the
    // previous mark. Size a withdrawal off the stale figure and the server
    // answers "that would overdraw their share".
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2000' },
    });

    const hint = screen.getByTestId('redemption-stake-hint');
    expect(hint).toHaveTextContent(/760/);
    expect(hint).not.toHaveTextContent(/1[.,]000/);
  });

  it('quotes no figure at all when the pool has no price', async () => {
    const user = userEvent.setup();
    const unpriced = pool({
      accountBalance: 0,
      unpaidDistributionCash: 0,
      unpaidDistributionCount: 0,
      poolValue: 0,
      totalUnits: 0,
      navPerUnit: null,
      participants: [
        participant({
          id: OWNER_ID,
          name: 'Me',
          isOwner: true,
          units: 0,
          capitalBase: 0,
          stake: null,
          distributable: null,
        }),
        participant({
          id: ANDREI_ID,
          name: 'Andrei',
          units: 0,
          capitalBase: 0,
          stake: null,
          distributable: null,
        }),
      ],
    });

    renderWithClient(<RecordRedemptionDialog pool={unpriced} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));

    // No units, no NAV — and a placeholder here would be an invented valuation
    // of somebody else's money. Say so, quote nothing, before AND after a
    // total is typed.
    expect(screen.getByTestId('redemption-stake-hint')).not.toHaveTextContent(/\d/);

    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2300' },
    });
    expect(screen.getByTestId('redemption-stake-hint')).not.toHaveTextContent(/\d/);
  });
});

describe('remaining field guards', () => {
  it('requires a participant on a redemption', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '10' } });
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(await screen.findByTestId('redemption-participant-error')).toHaveTextContent(
      'Pick whose money is leaving',
    );
  });

  it('lets an excluded participant be put back into the payout set', async () => {
    const user = userEvent.setup();
    const twoPayable = pool({
      participants: [
        participant({
          id: OWNER_ID,
          name: 'Me',
          isOwner: true,
          capitalBase: 0,
          distributable: 500,
        }),
        participant({ id: ANDREI_ID, name: 'Andrei', distributable: 100 }),
      ],
    });

    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            totalCash: 100,
            lines: [],
          },
          { status: 201 },
        );
      }),
    );

    renderWithClient(<CloseMonthDialog pool={twoPayable} open onOpenChange={noop} />);

    // Out, then back in — the second click must re-include rather than
    // exclude twice.
    await user.click(screen.getByTestId('close-month-payout-toggle-Andrei'));
    expect(screen.getByTestId('close-month-submit-button')).toBeDisabled();
    await user.click(screen.getByTestId('close-month-payout-toggle-Andrei'));

    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).payouts).toBeNull();
  });
});
