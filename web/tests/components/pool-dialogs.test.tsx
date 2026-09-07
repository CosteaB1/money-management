import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AddParticipantDialog } from '@/src/components/pools/add-participant-dialog';
import { ArchivePoolDialog } from '@/src/components/pools/archive-pool-dialog';
import { CloseMonthDialog } from '@/src/components/pools/close-month-dialog';
import { RecordCostReimbursementDialog } from '@/src/components/pools/record-cost-reimbursement-dialog';
import { RecordRedemptionDialog } from '@/src/components/pools/record-redemption-dialog';
import { RecordSubscriptionDialog } from '@/src/components/pools/record-subscription-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { PoolDetailDto, PoolParticipantDto } from '@/src/types/api';

const POOL_ID = 'aaaa0001-0000-4000-8000-000000000001';
const OWNER_ID = 'bbbb0001-0000-4000-8000-000000000001';
const ANDREI_ID = 'bbbb0001-0000-4000-8000-000000000002';
const BOGDAN_ID = 'bbbb0001-0000-4000-8000-000000000003';

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
      participant({ id: BOGDAN_ID, name: 'Bogdan', capitalBase: 1000, distributable: 0 }),
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

const noop = () => {};

describe('RecordSubscriptionDialog', () => {
  it('refuses to move money without the exchange total', async () => {
    const user = userEvent.setup();
    const posted = vi.fn();
    server.use(
      http.post('*/pools/:id/subscriptions', () => {
        posted();
        return HttpResponse.json({}, { status: 201 });
      }),
    );

    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(
      await screen.findByText('Type the exchange total before recording money in'),
    ).toBeInTheDocument();
    // The hard block: nothing reached the API.
    expect(posted).not.toHaveBeenCalled();
  });

  it('spells out that the total must include everything, BNB included', async () => {
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);
    const dialog = screen.getByTestId('record-subscription-dialog');
    expect(dialog).toHaveTextContent(/including BNB/i);
    expect(dialog).toHaveTextContent(/spot, futures, earn and fiat/i);
  });

  it('requires a participant', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '100' } });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByTestId('subscription-participant-error')).toHaveTextContent(
      'Pick who the money is from',
    );
  });

  it('submits the participant, the total and the cash', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/subscriptions', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            eventId: 'e',
            units: 800,
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

    const onOpenChange = vi.fn();
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('subscription-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ participantId: ANDREI_ID, poolValueNow: 2500, cash: 1000 });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('surfaces a server rejection inline instead of silently closing', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/subscriptions', () =>
        HttpResponse.json({ detail: 'That account is not pooled.' }, { status: 409 }),
      ),
    );
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByTestId('record-subscription-error')).toHaveTextContent(
      'That account is not pooled.',
    );
  });
});

describe('RecordRedemptionDialog', () => {
  it('refuses to move money without the exchange total', async () => {
    const user = userEvent.setup();
    const posted = vi.fn();
    server.use(
      http.post('*/pools/:id/redemptions', () => {
        posted();
        return HttpResponse.json({}, { status: 201 });
      }),
    );

    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '120' } });
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(
      await screen.findByText('Type the exchange total before recording money out'),
    ).toBeInTheDocument();
    expect(posted).not.toHaveBeenCalled();
  });

  it('offers only same-currency, non-pooled destination accounts', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-destination-select'));
    // XTB is the only other USD account in the seed.
    expect(await screen.findByRole('option', { name: 'XTB' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Cash Wallet' })).not.toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Not tracked in this app' })).toBeInTheDocument();
  });

  it('sends the wind-down acknowledgement only when it is ticked', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/redemptions', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            eventId: 'e',
            units: 1,
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            movementTransactionId: 'tx',
            counterTransactionId: null,
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
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '2500' } });
    await user.click(screen.getByTestId('redemption-wind-down-checkbox'));
    await user.click(screen.getByTestId('redemption-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ isFullWindDown: true, cash: 2500 });
  });
});

describe('CloseMonthDialog', () => {
  it('says in as many words that no money moves yet', () => {
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);
    const notice = screen.getByTestId('close-month-no-money-notice');
    expect(notice).toHaveTextContent('No money moves now.');
    expect(notice).toHaveTextContent(/settle each payout separately/i);
  });

  it('refuses to close without the exchange total', async () => {
    const user = userEvent.setup();
    const posted = vi.fn();
    server.use(
      http.post('*/pools/:id/distributions', () => {
        posted();
        return HttpResponse.json({}, { status: 201 });
      }),
    );

    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('close-month-submit-button'));

    expect(
      await screen.findByText('Type the exchange total before closing the month'),
    ).toBeInTheDocument();
    expect(posted).not.toHaveBeenCalled();
  });

  it('lists only outside participants and explains a zero payable', () => {
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);

    // The owner is never in the payout set — their profit stays in.
    expect(screen.getAllByTestId('close-month-payout-row').length).toBe(2);
    expect(screen.getByTestId('close-month-payout-none-Bogdan')).toHaveTextContent(
      'Nothing payable — still at or below what they put in',
    );
    expect(screen.getByTestId('close-month-payout-toggle-Bogdan')).toBeDisabled();
  });

  it('omits the payout list entirely when nobody is excluded', async () => {
    const user = userEvent.setup();
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

    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    // Amounts are never sent — the server recomputes them against the total
    // just typed, which is the only figure that is not already stale.
    expect((body as unknown as Record<string, unknown>).payouts).toBeNull();
  });

  it('sends an explicit list once somebody is left out to compound', async () => {
    const user = userEvent.setup();
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
            totalCash: 60,
            lines: [],
          },
          { status: 201 },
        );
      }),
    );

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
        participant({ id: BOGDAN_ID, name: 'Bogdan', distributable: 60 }),
      ],
    });

    renderWithClient(<CloseMonthDialog pool={twoPayable} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('close-month-payout-toggle-Andrei'));
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).payouts).toEqual([
      { participantId: BOGDAN_ID },
    ]);
  });

  it('blocks the close and points at Update balance when nothing is payable', () => {
    const nothing = pool({
      participants: [
        participant({
          id: OWNER_ID,
          name: 'Me',
          isOwner: true,
          capitalBase: 0,
          distributable: 500,
        }),
        participant({ id: ANDREI_ID, name: 'Andrei', capitalBase: 1000, distributable: 0 }),
      ],
    });
    renderWithClient(<CloseMonthDialog pool={nothing} open onOpenChange={noop} />);

    const note = screen.getByTestId('close-month-nothing-payable');
    expect(note).toHaveTextContent(/still at or below what they put in/i);
    expect(note).toHaveTextContent('Update balance');
    expect(screen.getByTestId('close-month-submit-button')).toBeDisabled();
  });

  // The pool the smoke test broke on. A close sweeps every stake back to its
  // capital base, so the API reports `distributable: 0` for EVERYBODY the
  // moment the previous month is closed — the state the pool then sits in for
  // the whole of the next month. Gating on that figure made the primary
  // monthly workflow unreachable: the user types a total that plainly pays
  // their friend and the dialog answers "nothing is payable", button disabled.
  const sweptClean = () =>
    pool({
      accountBalance: 2092,
      unpaidDistributionCash: 0,
      unpaidDistributionCount: 0,
      poolValue: 2092,
      totalUnits: 2092,
      navPerUnit: 1,
      participants: [
        participant({
          id: OWNER_ID,
          name: 'Me',
          isOwner: true,
          units: 1092,
          capitalBase: 0,
          stake: 1092,
          distributable: 1092,
        }),
        participant({
          id: ANDREI_ID,
          name: 'Andrei',
          units: 1000,
          capitalBase: 1000,
          stake: 1000,
          distributable: 0,
        }),
      ],
    });

  it('reprices the payout list against the total being typed, not the last mark', () => {
    renderWithClient(<CloseMonthDialog pool={sweptClean()} open onOpenChange={noop} />);

    // At the mark the API priced against, nothing is payable — and that is a
    // true statement about the OLD mark, not about the close being attempted.
    expect(screen.getByTestId('close-month-submit-button')).toBeDisabled();

    // 2300 typed, nothing unpaid → 2300 / 2092 shares = NAV 1.099426386233.
    // Andrei's 1000 shares are worth 1099.43 against a capital base of 1000.
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2300' } });

    expect(screen.getByTestId('close-month-submit-button')).toBeEnabled();
    expect(screen.getByTestId('close-month-payout-amount-Andrei')).toHaveTextContent(/99[.,]43/);
    // The server FLOORS what it actually pays (99.42 for this exact close), so
    // the copy must not promise the figure on screen to the cent.
    expect(screen.getByTestId('close-month-estimate')).toHaveTextContent(/cent either side/i);
  });

  it('stays disabled and points at Update balance when the typed total pays nobody', () => {
    renderWithClient(<CloseMonthDialog pool={sweptClean()} open onOpenChange={noop} />);

    // Exactly this month's existing value: everyone is still at their base, so
    // a close would have no lines to write. Zero payable is a real answer —
    // the message just has to be true of the total actually typed.
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2092' } });

    const note = screen.getByTestId('close-month-nothing-payable');
    expect(note).toHaveTextContent(/still at or below what they put in/i);
    expect(note).toHaveTextContent('Update balance');
    expect(note).toHaveTextContent(/2[.,]092/);
    expect(screen.getByTestId('close-month-submit-button')).toBeDisabled();
  });

  it('keeps the owner out of the payout set however large their recomputed figure', () => {
    renderWithClient(<CloseMonthDialog pool={sweptClean()} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2300' } });

    // A seed carries no cash, so the owner's capital base is 0 and their
    // recomputed distributable is their WHOLE stake — 1092 shares at 1.0994,
    // about 1200.57. That is the formula being honest, not a payout: their
    // profit stays in the pool and the server's close excludes them too.
    expect(screen.queryByTestId('close-month-payout-toggle-Me')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('close-month-payout-row')).toHaveLength(1);
    expect(screen.getByTestId('close-month-estimate')).toHaveTextContent(/99[.,]43/);
    expect(screen.getByTestId('close-month-estimate')).not.toHaveTextContent(/1[.,]200/);
  });

  it('subtracts the unpaid payout before striking the NAV', () => {
    const withUnpaid = pool({
      accountBalance: 2292,
      unpaidDistributionCash: 200,
      unpaidDistributionCount: 1,
      poolValue: 2092,
      totalUnits: 2092,
      navPerUnit: 1,
      participants: [
        participant({
          id: OWNER_ID,
          name: 'Me',
          isOwner: true,
          units: 1092,
          capitalBase: 0,
          stake: 1092,
          distributable: 1092,
        }),
        participant({
          id: ANDREI_ID,
          name: 'Andrei',
          units: 1000,
          capitalBase: 1000,
          stake: 1000,
          distributable: 0,
        }),
      ],
    });

    renderWithClient(<CloseMonthDialog pool={withUnpaid} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2300' } });

    // 2300 typed − 200 closed but not yet sent = 2100 over 2092 shares →
    // 1003.82 for Andrei, 3.82 above his base. Skip that subtraction and the
    // owed cash counts as pool value a SECOND time (99.43) — paying the
    // friends twice on the same money, which is the entire reason the close is
    // two-phase.
    const amount = screen.getByTestId('close-month-payout-amount-Andrei');
    expect(amount).toHaveTextContent(/3[.,]82/);
    expect(amount).not.toHaveTextContent(/99[.,]43/);
  });

  it('does not divide by zero when the pool holds no shares', () => {
    const empty = pool({
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

    renderWithClient(<CloseMonthDialog pool={empty} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2300' } });

    // No units, no price — and the reason on screen has to be that, not the
    // high-water rule, which has nothing to do with it.
    const note = screen.getByTestId('close-month-nothing-payable');
    expect(note).toHaveTextContent(/no shares/i);
    expect(note).not.toHaveTextContent(/NaN|Infinity/);
    expect(screen.getByTestId('close-month-submit-button')).toBeDisabled();
  });
});

describe('RecordCostReimbursementDialog', () => {
  it('treats the exchange total as optional here, and only here', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/cost-reimbursements', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
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

    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);
    expect(screen.getByTestId('cost-reimbursement-dialog')).toHaveTextContent('optional');

    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    await user.click(screen.getByTestId('cost-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ amount: 25, poolValueNow: null });
  });

  it('still requires an amount', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('cost-submit-button'));
    expect(await screen.findByTestId('cost-amount-error')).toHaveTextContent(
      'Amount must be greater than 0',
    );
  });

  it('passes the total through when it is supplied', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/cost-reimbursements', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          navPerUnit: 1.25,
          totalUnitsTransferred: 16,
          totalAmountRecovered: 20,
          markDelta: 0,
          markTransactionId: null,
          ownerEventId: 'e',
          lines: [
            {
              eventId: 'x',
              participantId: ANDREI_ID,
              participantName: 'Andrei',
              units: 8,
              amount: 10,
            },
          ],
        });
      }),
    );

    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);
    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    fireEvent.change(screen.getByTestId('cost-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('cost-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ amount: 25, poolValueNow: 2600 });
  });
});

describe('AddParticipantDialog', () => {
  it('requires a name', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="Binance pool" open onOpenChange={noop} />,
    );
    await user.click(screen.getByTestId('participant-submit-button'));
    expect(await screen.findByTestId('participant-name-error')).toHaveTextContent(
      'Name is required',
    );
  });

  it('makes clear the new participant holds nothing until their money is recorded', async () => {
    const user = userEvent.setup();
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/participants', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new' }, { status: 201 });
      }),
    );

    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="Binance pool" open onOpenChange={noop} />,
    );
    expect(screen.getByTestId('add-participant-dialog')).toHaveTextContent(/no share yet/i);

    await user.type(screen.getByTestId('participant-name-input'), 'Bogdan');
    await user.click(screen.getByTestId('participant-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ name: 'Bogdan' });
  });
});

describe('ArchivePoolDialog', () => {
  const archivable = { id: POOL_ID, name: 'Binance pool', currency: 'USD', outsideCapital: 0 };

  it('warns that archiving cannot be undone', () => {
    renderWithClient(<ArchivePoolDialog pool={archivable} open onOpenChange={noop} />);
    expect(screen.getByTestId('archive-pool-dialog')).toHaveTextContent(
      /there is no unarchive for pools/i,
    );
  });

  it('blocks the button while other people still hold money in the pool', () => {
    renderWithClient(
      <ArchivePoolDialog pool={{ ...archivable, outsideCapital: 2000 }} open onOpenChange={noop} />,
    );
    expect(screen.getByTestId('archive-pool-outside-warning')).toHaveTextContent(
      /their money would silently become yours/i,
    );
    expect(screen.getByTestId('archive-pool-confirm-button')).toBeDisabled();
  });

  it('archives once nobody else holds a share', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<ArchivePoolDialog pool={archivable} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('archive-pool-confirm-button'));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it('shows a server refusal inline', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/pools/:id/archive', () =>
        HttpResponse.json({ detail: 'Outside units are still outstanding.' }, { status: 409 }),
      ),
    );
    renderWithClient(<ArchivePoolDialog pool={archivable} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('archive-pool-confirm-button'));
    expect(await screen.findByTestId('archive-pool-error')).toHaveTextContent(
      'Outside units are still outstanding.',
    );
  });
});

describe('the shared valuation field', () => {
  it('carries the same hard-block copy into every money-moving dialog', () => {
    for (const [name, ui] of [
      ['subscription', <RecordSubscriptionDialog key="s" pool={pool()} open onOpenChange={noop} />],
      ['redemption', <RecordRedemptionDialog key="r" pool={pool()} open onOpenChange={noop} />],
      ['close', <CloseMonthDialog key="c" pool={pool()} open onOpenChange={noop} />],
    ] as const) {
      const { unmount } = renderWithClient(ui);
      const field = screen.getByText(/Required: no valuation, no movement\./i);
      expect(field, name).toBeInTheDocument();
      expect(within(field).getByText('including BNB')).toBeInTheDocument();
      unmount();
    }
  });
});
