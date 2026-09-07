import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  type BackfillRowSnapshot,
  CreatePoolDialog,
  projectBackfills,
} from '@/src/components/pools/create-pool-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { todayIsoUtc } from '@/src/lib/utils/date';

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

async function openDialog() {
  const user = userEvent.setup();
  renderWithClient(<CreatePoolDialog />);
  await user.click(screen.getByTestId('add-pool-button'));
  await screen.findByTestId('create-pool-dialog');
  return user;
}

describe('CreatePoolDialog', () => {
  it('offers only accounts a pool can actually live in', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));

    // Investment-type, non-archived, not already pooled.
    expect(await screen.findByRole('option', { name: 'ING Savings (MDL)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'XTB (USD)' })).toBeInTheDocument();
    // Cash and credit cards can never take a balance adjustment, so their NAV
    // could never be re-priced.
    expect(screen.queryByRole('option', { name: /Cash Wallet/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /BRD Visa/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Old Revolut/ })).not.toBeInTheDocument();
  });

  it('hides an account that already has a pool on it', async () => {
    server.use(
      http.get('*/accounts', () =>
        HttpResponse.json([
          {
            id: '44444444-4444-4444-4444-444444444444',
            name: 'XTB',
            type: 'Brokerage',
            currency: 'USD',
            openingDate: '2024-09-10',
            isArchived: false,
            isPooled: true,
            notes: null,
            balance: 1500,
            balanceMdl: 26250,
          },
        ]),
      ),
    );
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    expect(await screen.findByText(/No eligible accounts/)).toBeInTheDocument();
  });

  it('requires an account, a name and the exchange total on the start date', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('pool-account-error')).toHaveTextContent(
      'Pick the account the pool lives in',
    );
    expect(screen.getByTestId('pool-name-error')).toHaveTextContent('Pool name is required');
    expect(screen.getByTestId('pool-inception-value-error')).toHaveTextContent(
      'Type the exchange total on the start date',
    );
  });

  it('derives the currency from the account and never asks for it', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));

    expect(screen.getByTestId('create-pool-dialog')).toHaveTextContent(
      /The pool is denominated in the account.s own currency \(USD\)/,
    );
  });

  it('submits the account, the derived currency and the opening valuation', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      accountId: '44444444-4444-4444-4444-444444444444',
      name: 'Binance pool',
      currency: 'USD',
      ownerName: 'Me',
      poolValueAtInception: 1500,
      inceptionDate: todayIsoUtc(),
      backdatedSubscriptions: null,
    });
  });

  it('demands a pre-money total per backdated arrival, because the balance already contains their cash', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    expect(await screen.findByTestId('pool-backfill-row')).toBeInTheDocument();

    await user.type(screen.getByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('backfill-pre-error-0')).toHaveTextContent(
      'Type the total from just BEFORE their money landed',
    );
  });

  it('sends each backdated arrival priced at its own day', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await user.type(await screen.findByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-date-input-0'), {
      target: { value: '2026-08-05' },
    });
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    fireEvent.change(screen.getByTestId('backfill-pre-input-0'), { target: { value: '1500' } });
    // No click on the write toggle: it now arrives ON, which is the case the
    // whole back-fill flow exists for.
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).backdatedSubscriptions).toEqual([
      {
        participantName: 'Andrei',
        occurredOn: '2026-08-05',
        cash: 1000,
        poolValuePreMoney: 1500,
        writeMovementTransaction: true,
      },
    ]);
  });

  it('drops backfill rows again when the toggle goes off, so nothing stale is sent', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await user.type(await screen.findByTestId('backfill-name-input-0'), 'Andrei');
    // A half-filled row would otherwise both fail validation and be sent.
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await waitFor(() => expect(screen.queryByTestId('pool-backfill-row')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('pool-submit-button'));
    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).backdatedSubscriptions).toBeNull();
  });

  it('adds and removes extra arrival rows', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');

    await user.click(screen.getByTestId('backfill-add'));
    await waitFor(() => expect(screen.getAllByTestId('pool-backfill-row').length).toBe(2));

    await user.click(screen.getByTestId('backfill-remove-1'));
    await waitFor(() => expect(screen.getAllByTestId('pool-backfill-row').length).toBe(1));
  });

  it('surfaces a server rejection inline rather than closing', async () => {
    server.use(
      http.post('*/pools', () =>
        HttpResponse.json(
          { detail: 'A savings goal already links that account.' },
          { status: 409 },
        ),
      ),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('create-pool-error')).toHaveTextContent(
      'A savings goal already links that account.',
    );
    expect(screen.getByTestId('create-pool-dialog')).toBeInTheDocument();
  });

  it('falls back to a toast on a network failure', async () => {
    server.use(http.post('*/pools', () => HttpResponse.error()));

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('closes and resets when cancelled', async () => {
    const user = await openDialog();
    await user.type(screen.getByTestId('pool-name-input'), 'Scratch');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByTestId('create-pool-dialog')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('add-pool-button'));
    expect(await screen.findByTestId('pool-name-input')).toHaveValue('');
  });
});

describe('CreatePoolDialog — a11y', () => {
  it('labels the opening valuation as the whole exchange total, BNB included', async () => {
    await openDialog();
    expect(screen.getByText(/including BNB/i)).toBeInTheDocument();
    expect(screen.getByTestId('create-pool-dialog')).toHaveTextContent(
      /spot, futures, earn, fiat/i,
    );
  });
});

// Guards the one place the dialog silently trusts the server: the mutation's
// success path resets local state, so re-opening must not resurrect the
// backfill section.
describe('CreatePoolDialog — after a successful create', () => {
  it('reopens clean', async () => {
    const onCreated = vi.fn();
    server.use(
      http.post('*/pools', () => {
        onCreated();
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');
    await user.type(screen.getByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-date-input-0'), {
      target: { value: '2026-08-05' },
    });
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    fireEvent.change(screen.getByTestId('backfill-pre-input-0'), { target: { value: '1500' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByTestId('create-pool-dialog')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('add-pool-button'));
    await screen.findByTestId('create-pool-dialog');
    expect(screen.queryByTestId('pool-backfill-row')).not.toBeInTheDocument();
  });
});

describe('CreatePoolDialog field guards', () => {
  it('requires a name for yourself in the pool', async () => {
    const user = await openDialog();
    const owner = screen.getByTestId('pool-owner-name-input');
    await user.clear(owner);
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Your name is required')).toBeInTheDocument();
  });

  it('refuses a start date in the future', async () => {
    const user = await openDialog();
    const date = screen.getByTestId('pool-inception-input') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Start date cannot be in the future')).toBeInTheDocument();
  });

  it('rejects an over-long note', async () => {
    const user = await openDialog();
    const notes = screen.getByTestId('pool-notes-input') as HTMLTextAreaElement;
    notes.removeAttribute('maxlength');
    fireEvent.change(notes, { target: { value: 'x'.repeat(501) } });
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('validates every field of a backdated arrival', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');

    const date = screen.getByTestId('backfill-date-input-0') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Arrival date cannot be in the future')).toBeInTheDocument();
    expect(screen.getByText('Amount must be greater than 0')).toBeInTheDocument();
  });
});

/**
 * Regression cover for the incident that produced a pool holding 3,000 units
 * against a 2,000 balance: two back-dated arrivals were entered, only one was
 * marked as still needing to be written to the account, and nothing on screen
 * said so.
 *
 * The investigation cleared the framework — `register` + `useFieldArray`
 * round-trips both flags correctly, through append, remove and the index shift
 * a remove causes — so what follows pins the two properties that actually
 * failed the user: the answer is defaulted to the common case, and the
 * resulting account balance is stated before submit.
 */
describe('CreatePoolDialog — the write-to-account decision', () => {
  const captureBody = () => {
    const box: { body: Record<string, unknown> | null } = { body: null };
    server.use(
      http.post('*/pools', async ({ request }) => {
        box.body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1000,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );
    return box;
  };

  const sent = (box: { body: Record<string, unknown> | null }) =>
    box.body?.backdatedSubscriptions as Record<string, unknown>[] | null | undefined;

  async function pickAccountAndSeed(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1000' },
    });
  }

  function fillArrival(index: number, name: string, cash: string, preMoney: string) {
    fireEvent.change(screen.getByTestId(`backfill-name-input-${index}`), {
      target: { value: name },
    });
    fireEvent.change(screen.getByTestId(`backfill-date-input-${index}`), {
      target: { value: '2026-08-05' },
    });
    fireEvent.change(screen.getByTestId(`backfill-cash-input-${index}`), {
      target: { value: cash },
    });
    fireEvent.change(screen.getByTestId(`backfill-pre-input-${index}`), {
      target: { value: preMoney },
    });
  }

  async function twoArrivals(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-name-input-0');
    fillArrival(0, 'Felix', '1000', '1000');
    await user.click(screen.getByTestId('backfill-add'));
    await screen.findByTestId('backfill-name-input-1');
    fillArrival(1, 'Student B', '1000', '2000');
  }

  it('arrives ticked, so the common case needs no click at all', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));

    expect(await screen.findByTestId('backfill-write-tx-0')).toBeChecked();
  });

  it('gives an appended row the same ticked default as the first', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-write-tx-0');
    await user.click(screen.getByTestId('backfill-add'));

    expect(await screen.findByTestId('backfill-write-tx-1')).toBeChecked();
    expect(screen.getByTestId('backfill-write-tx-0')).toBeChecked();
  });

  it('carries BOTH rows flags through to the payload', async () => {
    const box = captureBody();
    const user = await openDialog();
    await pickAccountAndSeed(user);
    await twoArrivals(user);
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(box.body).not.toBeNull());
    expect(sent(box)).toEqual([
      {
        participantName: 'Felix',
        occurredOn: '2026-08-05',
        cash: 1000,
        poolValuePreMoney: 1000,
        writeMovementTransaction: true,
      },
      {
        participantName: 'Student B',
        occurredOn: '2026-08-05',
        cash: 1000,
        poolValuePreMoney: 2000,
        writeMovementTransaction: true,
      },
    ]);
  });

  // Pinned per row, not as a pair: the bug was one row's answer going missing
  // while the other survived, which a combined assertion would not have caught.
  it('unticks exactly one row and leaves the other alone', async () => {
    const box = captureBody();
    const user = await openDialog();
    await pickAccountAndSeed(user);
    await twoArrivals(user);

    await user.click(screen.getByTestId('backfill-write-tx-0'));
    expect(screen.getByTestId('backfill-write-tx-0')).not.toBeChecked();
    expect(screen.getByTestId('backfill-write-tx-1')).toBeChecked();

    await user.click(screen.getByTestId('pool-submit-button'));
    await waitFor(() => expect(box.body).not.toBeNull());
    expect(sent(box)?.[0]?.writeMovementTransaction).toBe(false);
    expect(sent(box)?.[1]?.writeMovementTransaction).toBe(true);
  });

  it('unticks the second row without disturbing the first', async () => {
    const box = captureBody();
    const user = await openDialog();
    await pickAccountAndSeed(user);
    await twoArrivals(user);

    await user.click(screen.getByTestId('backfill-write-tx-1'));
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(box.body).not.toBeNull());
    expect(sent(box)?.[0]?.writeMovementTransaction).toBe(true);
    expect(sent(box)?.[1]?.writeMovementTransaction).toBe(false);
  });

  it('says what each state means, in that state', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-name-input-0');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });

    expect(await screen.findByTestId('backfill-write-tx-help-0')).toHaveTextContent(
      'Records 1.000,00 USD onto XTB on the arrival date. This is the usual case: the money reached the exchange but was never entered here.',
    );

    await user.click(screen.getByTestId('backfill-write-tx-0'));
    expect(screen.getByTestId('backfill-write-tx-help-0')).toHaveTextContent(
      'Nothing will be recorded. The app takes it that 1.000,00 USD is already on XTB as a transaction on the arrival date. Their share is issued either way.',
    );
  });
});

/**
 * The part that would have caught the incident on its own: the closing balance,
 * stated before the Create button, computed only from the ticked rows.
 */
describe('CreatePoolDialog — consequence summary', () => {
  async function seededDialog() {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1000' },
    });
    return user;
  }

  it('stays hidden until there is an arrival to summarise', async () => {
    const user = await seededDialog();
    expect(screen.queryByTestId('pool-consequence')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    expect(await screen.findByTestId('pool-consequence')).toBeInTheDocument();
  });

  it('states the total and the balance it lands on, and follows every edit', async () => {
    const user = await seededDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-cash-input-0');

    // One arrival, singular.
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'This will record 1 arrival of 1.000,00 USD on XTB, taking it from 1.000,00 USD to 2.000,00 USD.',
      ),
    );

    // Adding a row moves the total.
    await user.click(screen.getByTestId('backfill-add'));
    await screen.findByTestId('backfill-cash-input-1');
    fireEvent.change(screen.getByTestId('backfill-cash-input-1'), { target: { value: '1000' } });
    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'This will record 2 arrivals totalling 2.000,00 USD on XTB, taking it from 1.000,00 USD to 3.000,00 USD.',
      ),
    );

    // Editing an amount moves it again.
    fireEvent.change(screen.getByTestId('backfill-cash-input-1'), { target: { value: '2500.50' } });
    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'This will record 2 arrivals totalling 3.500,50 USD on XTB, taking it from 1.000,00 USD to 4.500,50 USD.',
      ),
    );
  });

  // The exact shape of the incident: the total silently loses the unticked row,
  // and the gap it opens is named in the same units the pool will be wrong by.
  it('drops an unticked row out of the total and names what is assumed instead', async () => {
    const user = await seededDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-cash-input-0');
    fireEvent.change(screen.getByTestId('backfill-name-input-0'), { target: { value: 'Felix' } });
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('backfill-add'));
    await screen.findByTestId('backfill-cash-input-1');
    fireEvent.change(screen.getByTestId('backfill-name-input-1'), {
      target: { value: 'Student B' },
    });
    fireEvent.change(screen.getByTestId('backfill-cash-input-1'), { target: { value: '1000' } });

    expect(screen.queryByTestId('pool-consequence-assumed')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('backfill-write-tx-0'));

    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'This will record 1 arrival of 1.000,00 USD on XTB, taking it from 1.000,00 USD to 2.000,00 USD.',
      ),
    );
    const assumed = screen.getByTestId('pool-consequence-assumed');
    expect(assumed).toHaveTextContent(
      'Not recorded — the app expects this money is already on XTB:',
    );
    expect(assumed).toHaveTextContent('Felix — 1.000,00 USD');
    expect(assumed).not.toHaveTextContent('Student B');
    expect(assumed).toHaveTextContent(
      'Their share is issued either way, so if it is not already there the pool will hold 1.000,00 USD more than the balance backs.',
    );
  });

  it('says plainly when nothing at all will be recorded', async () => {
    const user = await seededDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-cash-input-0');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('backfill-write-tx-0'));

    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'No arrival will be recorded on XTB, so its total stays at 1.000,00 USD.',
      ),
    );
  });

  it('names an unnamed arrival rather than listing a blank', async () => {
    const user = await seededDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-cash-input-0');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '250' } });
    await user.click(screen.getByTestId('backfill-write-tx-0'));

    expect(await screen.findByTestId('pool-consequence-assumed')).toHaveTextContent(
      'Unnamed arrival — 250,00 USD',
    );
  });

  // No account picked yet means no currency to name, and stamping the wrong ISO
  // code onto a real figure is worse than leaving it off.
  it('omits the currency code until an account is chosen', async () => {
    const user = await openDialog();
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1000' },
    });
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('backfill-cash-input-0');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });

    await waitFor(() =>
      expect(screen.getByTestId('pool-consequence-written')).toHaveTextContent(
        'This will record 1 arrival of 1.000,00 on the account, taking it from 1.000,00 to 2.000,00.',
      ),
    );
  });
});

describe('projectBackfills', () => {
  const row = (over: Partial<BackfillRowSnapshot> = {}): BackfillRowSnapshot => ({
    id: 'r1',
    participantName: 'Felix',
    cash: '1000',
    writeMovementTransaction: true,
    ...over,
  });

  it('splits the rows on the flag and totals each side', () => {
    const result = projectBackfills(
      [row(), row({ id: 'r2', participantName: 'Ana', writeMovementTransaction: false })],
      '1000',
    );

    expect(result.written.map((a) => a.name)).toEqual(['Felix']);
    expect(result.assumed.map((a) => a.name)).toEqual(['Ana']);
    expect(result.writtenTotal).toBe(1000);
    expect(result.assumedTotal).toBe(1000);
    expect(result.opening).toBe(1000);
    expect(result.closing).toBe(2000);
  });

  it('treats blanks and junk as zero rather than NaN', () => {
    const result = projectBackfills(
      [row({ cash: '' }), row({ id: 'r2', cash: undefined }), row({ id: 'r3', cash: 'abc' })],
      undefined,
    );

    expect(result.writtenTotal).toBe(0);
    expect(result.opening).toBe(0);
    expect(result.closing).toBe(0);
  });

  it('rounds to the cent so a float artefact never reaches the screen', () => {
    const result = projectBackfills([row({ cash: 0.1 }), row({ id: 'r2', cash: 0.2 })], 0);

    expect(result.writtenTotal).toBe(0.3);
    expect(result.closing).toBe(0.3);
  });

  it('falls back to a placeholder name for a whitespace-only row', () => {
    const result = projectBackfills([row({ participantName: '   ' })], 0);

    expect(result.written[0]?.name).toBe('Unnamed arrival');
  });
});
