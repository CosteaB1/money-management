import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { DeletePoolDialog } from '@/src/components/pools/delete-pool-dialog';
import { PoolDetailView } from '@/src/components/pools/detail/pool-detail-view';
import { PoolsTable } from '@/src/components/pools/pools-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

/**
 * The two exits a pool had no way out of before: **Delete** (created by
 * mistake — the wrong account, a mis-click) and **Unarchive** (archived by
 * mistake, which used to be permanent *and* locking, since the unique index on
 * `account_id` is unfiltered and an archived pool went on holding its account).
 *
 * The assertions that matter most here are copy assertions, not plumbing ones:
 * a user who reads "delete the pool" as "undo everything the pool did to my
 * balance" will misread their own account afterwards.
 */

// The detail header pushes back to /pools once the pool it is showing is gone.
const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: pushMock }) }));

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

const noop = () => {};

/** A pool that never moved anyone's money — the shape Delete exists for. */
const mistakePool = {
  id: POOL_ID,
  name: 'Binance pool',
  accountName: 'Binanance',
  currency: 'USD',
  outsideCapital: 0,
  isArchived: false,
};

/** The backend's refusal for a pool that has actually been used. */
function hasMovementsConflict() {
  return HttpResponse.json(
    {
      errorCode: 'pools.delete_has_movements',
      detail:
        "This pool holds units for someone other than the owner, or has events that moved cash, so it can't be deleted.",
    },
    { status: 409 },
  );
}

describe('DeletePoolDialog copy', () => {
  it('states outright that transactions on the account are not touched or reversed', () => {
    renderWithClient(<DeletePoolDialog pool={mistakePool} open onOpenChange={noop} />);

    // The one sentence that stops a user assuming their balance rolls back.
    const note = screen.getByTestId('delete-pool-transactions-note');
    expect(note).toHaveTextContent('Transactions on Binanance are not touched.');
    expect(note).toHaveTextContent(/nothing is reversed/i);
    expect(note).toHaveTextContent(/you may already have reconciled against it/i);
    expect(note).toHaveTextContent(/delete it yourself from the transactions page/i);
  });

  it('reads as the created-by-mistake exit, not as a stronger Archive', () => {
    renderWithClient(<DeletePoolDialog pool={mistakePool} open onOpenChange={noop} />);
    const dialog = screen.getByTestId('delete-pool-dialog');

    expect(dialog).toHaveTextContent(/never moved anyone.s money/i);
    expect(dialog).toHaveTextContent(/everyone listed in it, and its whole ledger/i);
    expect(dialog).toHaveTextContent(/archive it instead/i);
  });
});

describe('DeletePoolDialog behaviour', () => {
  it('deletes the pool, closes, and reports it upwards', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onDeleted = vi.fn();
    let deletedId: string | null = null;
    server.use(
      http.delete('*/pools/:id', ({ params }) => {
        deletedId = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(
      <DeletePoolDialog
        pool={mistakePool}
        open
        onOpenChange={onOpenChange}
        onDeleted={onDeleted}
      />,
    );
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    await waitFor(() => expect(deletedId).toBe(POOL_ID));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onDeleted).toHaveBeenCalled();
  });

  it('turns the has-movements refusal into inline guidance and keeps the dialog open', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    server.use(http.delete('*/pools/:id', hasMovementsConflict));

    renderWithClient(
      <DeletePoolDialog
        pool={mistakePool}
        open
        onOpenChange={onOpenChange}
        onArchiveInstead={noop}
      />,
    );
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    const blocked = await screen.findByTestId('delete-pool-blocked');
    expect(blocked).toHaveTextContent(/money has moved through this pool/i);
    expect(blocked).toHaveTextContent(/archive it instead/i);
    // Guidance, not a dead end — and never a toast-and-close.
    expect(screen.getByTestId('delete-pool-dialog')).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(screen.getByTestId('delete-pool-confirm-button')).toBeDisabled();
  });

  it('offers Archive as the next step from the refusal', async () => {
    const user = userEvent.setup();
    const onArchiveInstead = vi.fn();
    server.use(http.delete('*/pools/:id', hasMovementsConflict));

    renderWithClient(
      <DeletePoolDialog
        pool={mistakePool}
        open
        onOpenChange={noop}
        onArchiveInstead={onArchiveInstead}
      />,
    );
    await user.click(screen.getByTestId('delete-pool-confirm-button'));
    await user.click(await screen.findByTestId('delete-pool-archive-instead'));

    expect(onArchiveInstead).toHaveBeenCalled();
  });

  it('does not send them to a second refusal when outside money is still in the pool', async () => {
    const user = userEvent.setup();
    server.use(http.delete('*/pools/:id', hasMovementsConflict));

    renderWithClient(
      <DeletePoolDialog
        pool={{ ...mistakePool, outsideCapital: 2000 }}
        open
        onOpenChange={noop}
        onArchiveInstead={noop}
      />,
    );
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    const blocked = await screen.findByTestId('delete-pool-blocked');
    expect(blocked).toHaveTextContent(/pay them out or record their exit first/i);
    // Archiving is refused too while a stake is outstanding, so it is not offered.
    expect(screen.queryByTestId('delete-pool-archive-instead')).not.toBeInTheDocument();
  });

  it('surfaces a 404 inline instead of crashing', async () => {
    const user = userEvent.setup();
    server.use(
      http.delete('*/pools/:id', () =>
        HttpResponse.json({ detail: 'Pool not found.' }, { status: 404 }),
      ),
    );

    renderWithClient(<DeletePoolDialog pool={mistakePool} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    expect(await screen.findByTestId('delete-pool-error')).toHaveTextContent('Pool not found.');
    expect(screen.getByTestId('delete-pool-dialog')).toBeInTheDocument();
    // A 404 is not the archive-instead story — no guidance block for it.
    expect(screen.queryByTestId('delete-pool-blocked')).not.toBeInTheDocument();
  });

  it('falls back to a toast when the request never reaches the server', async () => {
    const user = userEvent.setup();
    server.use(http.delete('*/pools/:id', () => HttpResponse.error()));

    renderWithClient(<DeletePoolDialog pool={mistakePool} open onOpenChange={noop} />);
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });
});

describe('PoolsTable — delete and unarchive', () => {
  it('deletes a pool from the row menu', async () => {
    const user = userEvent.setup();
    let deletedId: string | null = null;
    server.use(
      http.delete('*/pools/:id', ({ params }) => {
        deletedId = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<PoolsTable />);
    await waitFor(() => expect(screen.getAllByTestId('pool-actions').length).toBe(1));
    await user.click(screen.getByTestId('pool-actions'));
    await user.click(await screen.findByTestId('delete-pool-action'));

    expect(await screen.findByTestId('delete-pool-dialog')).toBeInTheDocument();
    await user.click(screen.getByTestId('delete-pool-confirm-button'));

    await waitFor(() => expect(deletedId).toBe(POOL_ID));
  });

  it('unarchives an archived row and refreshes the list', async () => {
    const user = userEvent.setup();
    let unarchivedId: string | null = null;
    let listFetches = 0;
    server.use(
      http.get('*/pools', ({ request }) => {
        listFetches += 1;
        const includeArchived = new URL(request.url).searchParams.get('includeArchived') === 'true';
        const rows = [
          {
            id: ARCHIVED_POOL_ID,
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
        return HttpResponse.json(includeArchived ? rows : []);
      }),
      http.post('*/pools/:id/unarchive', ({ params }) => {
        unarchivedId = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<PoolsTable />);
    await user.click(screen.getByTestId('show-archived-pools-toggle'));
    const row = await screen.findByTestId('pool-row');
    const fetchesBefore = listFetches;

    await user.click(within(row).getByTestId('pool-actions'));
    await user.click(await screen.findByTestId('unarchive-pool-action'));

    await waitFor(() => expect(unarchivedId).toBe(ARCHIVED_POOL_ID));
    // The mutation invalidates ['pools'], so the list re-reads itself.
    await waitFor(() => expect(listFetches).toBeGreaterThan(fetchesBefore));
  });
});

describe('PoolDetailView — the archived pool that used to be stuck', () => {
  it('explains that unarchiving re-arms the guards on the account', async () => {
    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);

    const note = await screen.findByTestId('pool-detail-unarchive-note');
    expect(note).toHaveTextContent(/re-arms the guards on/i);
    expect(note).toHaveTextContent('Old Kraken');
    expect(note).toHaveTextContent(
      /manual transactions, transfers, imports and loan movements on that account are blocked again/i,
    );
  });

  it('unarchives from the detail page and re-reads the pool', async () => {
    const user = userEvent.setup();
    let unarchivedId: string | null = null;
    let detailFetches = 0;
    // Re-serve the seeded archived fixture through a counting handler, so the
    // refetch is observed without leaking a server event listener.
    const seeded = await (await fetch(`http://x/pools/${ARCHIVED_POOL_ID}`)).json();
    server.use(
      http.get('*/pools/:id', () => {
        detailFetches += 1;
        return HttpResponse.json(seeded);
      }),
      http.post('*/pools/:id/unarchive', ({ params }) => {
        unarchivedId = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);
    await user.click(await screen.findByTestId('pool-detail-unarchive'));

    await waitFor(() => expect(unarchivedId).toBe(ARCHIVED_POOL_ID));
    // The mutation invalidates ['pools'], which the detail key sits under.
    await waitFor(() => expect(detailFetches).toBeGreaterThan(1));
  });

  it('leaves the dead detail page after deleting the pool it was showing', async () => {
    const user = userEvent.setup();
    pushMock.mockClear();
    server.use(http.delete('*/pools/:id', () => new HttpResponse(null, { status: 204 })));

    renderWithClient(<PoolDetailView id={ARCHIVED_POOL_ID} />);
    await user.click(await screen.findByTestId('pool-detail-delete'));
    await user.click(await screen.findByTestId('delete-pool-confirm-button'));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith('/pools'));
  });

  it('keeps the user on the page when the delete is refused', async () => {
    const user = userEvent.setup();
    pushMock.mockClear();
    server.use(http.delete('*/pools/:id', hasMovementsConflict));

    renderWithClient(<PoolDetailView id={POOL_ID} />);
    await user.click(await screen.findByTestId('pool-detail-delete'));
    await user.click(await screen.findByTestId('delete-pool-confirm-button'));

    expect(await screen.findByTestId('delete-pool-blocked')).toBeInTheDocument();
    expect(pushMock).not.toHaveBeenCalled();
  });
});
