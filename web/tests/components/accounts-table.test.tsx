import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { AccountsTable } from '@/src/components/accounts/accounts-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

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

describe('AccountsTable navigation', () => {
  it('renders the account name as a link to /accounts/{id}', async () => {
    renderWithClient(<AccountsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('account-name-link').length).toBeGreaterThan(0);
    });

    // Cash Wallet has id 1111…1111 in the MSW seed.
    const link = screen.getByRole('link', { name: 'Cash Wallet' });
    expect(link).toHaveAttribute('href', '/accounts/11111111-1111-1111-1111-111111111111');
  });

  it('opens the row-action dropdown without navigating', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('account-actions').length).toBeGreaterThan(0);
    });

    // Pick an active (non-archived) row's action trigger and open it.
    const triggers = screen.getAllByTestId('account-actions');
    await user.click(triggers[0] as HTMLElement);

    // Archive action is always present in the menu.
    await waitFor(() => {
      expect(screen.getByTestId('archive-account')).toBeInTheDocument();
    });
  });

  it('unarchives an archived row when "Show archived" is on', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);

    // Default list (includeArchived=false) hides the archived seed row.
    await waitFor(() => {
      expect(screen.getAllByTestId('account-row').length).toBeGreaterThan(0);
    });
    expect(screen.queryByText('Old Revolut')).not.toBeInTheDocument();

    // Flip the toggle so the archived account ("Old Revolut") is fetched.
    await user.click(screen.getByTestId('show-archived-toggle'));

    const archivedLink = await screen.findByRole('link', { name: 'Old Revolut' });
    const archivedRow = archivedLink.closest('[data-testid="account-row"]');
    expect(archivedRow).not.toBeNull();

    // The archived row exposes a single action: Unarchive.
    const actionsTrigger = (archivedRow as HTMLElement).querySelector(
      '[data-testid="account-actions"]',
    );
    expect(actionsTrigger).not.toBeNull();
    await user.click(actionsTrigger as HTMLElement);

    const unarchive = await screen.findByTestId('unarchive-account');
    await user.click(unarchive);

    // Success toast confirms the mutation resolved; no error toast surfaced.
    await waitFor(() => {
      expect(screen.getByText('Unarchived "Old Revolut"')).toBeInTheDocument();
    });
    expect(screen.queryByText(/failed to unarchive/i)).not.toBeInTheDocument();
  });

  it('permanently deletes an account with no history and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);

    // BRD Visa (id 2222…) is an active row the MSW handler 204s on — anything
    // other than Cash Wallet deletes cleanly.
    const brdLink = await screen.findByRole('link', { name: 'BRD Visa' });
    const brdRow = brdLink.closest('[data-testid="account-row"]');
    expect(brdRow).not.toBeNull();

    const actionsTrigger = (brdRow as HTMLElement).querySelector('[data-testid="account-actions"]');
    await user.click(actionsTrigger as HTMLElement);

    const deleteItem = await screen.findByTestId('delete-account');
    await user.click(deleteItem);

    // Confirm in the shared dialog.
    const confirm = await screen.findByTestId('delete-account-confirm-button');
    await user.click(confirm);

    await waitFor(() => {
      expect(screen.getByText('Deleted "BRD Visa"')).toBeInTheDocument();
    });
    expect(screen.queryByText(/failed to delete account/i)).not.toBeInTheDocument();
  });

  it('surfaces the backend 409 message when the account still has history', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);

    // Cash Wallet (id 1111…) is the seeded account the handler 409s on.
    const cashLink = await screen.findByRole('link', { name: 'Cash Wallet' });
    const cashRow = cashLink.closest('[data-testid="account-row"]');
    expect(cashRow).not.toBeNull();

    const actionsTrigger = (cashRow as HTMLElement).querySelector(
      '[data-testid="account-actions"]',
    );
    await user.click(actionsTrigger as HTMLElement);

    const deleteItem = await screen.findByTestId('delete-account');
    await user.click(deleteItem);

    const confirm = await screen.findByTestId('delete-account-confirm-button');
    await user.click(confirm);

    // The verbatim 409 detail surfaces as an error toast.
    await waitFor(() => {
      expect(screen.getByText(/can't be permanently deleted/i)).toBeInTheDocument();
    });
    expect(screen.queryByText('Deleted "Cash Wallet"')).not.toBeInTheDocument();
  });

  it('opens the Update-balance dialog from an adjustable row', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);

    // XTB is a Brokerage account (adjustable) — the row menu exposes
    // "Update balance" which mounts the balance-change dialog.
    const xtbLink = await screen.findByRole('link', { name: 'XTB' });
    const xtbRow = xtbLink.closest('[data-testid="account-row"]');
    const trigger = (xtbRow as HTMLElement).querySelector('[data-testid="account-actions"]');
    await user.click(trigger as HTMLElement);

    await user.click(await screen.findByTestId('update-balance-action'));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    // Closing runs the table's `if (!next) setAdjustTarget(null)`.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('renders the empty-state row when there are no accounts', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json([])));
    renderWithClient(<AccountsTable />);
    expect(await screen.findByText(/No accounts yet/)).toBeInTheDocument();
  });

  it('renders an em dash in the MDL-eq column when an account has no rate', async () => {
    server.use(
      http.get('*/accounts', () =>
        HttpResponse.json([
          {
            id: 'no-rate',
            name: 'USD no rate',
            type: 'Brokerage',
            currency: 'USD',
            openingDate: '2025-01-01',
            isArchived: false,
            notes: null,
            balance: 100,
            balanceMdl: null,
          },
        ]),
      ),
    );
    renderWithClient(<AccountsTable />);
    const cell = await screen.findByTestId('account-mdl-eq');
    expect(cell).toHaveTextContent('—');
    expect(cell.querySelector('[title]')?.getAttribute('title')).toMatch(/No FX rate available/);
  });

  it('shows the error row when the accounts query fails', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<AccountsTable />);
    expect(await screen.findByText('Failed to load accounts.')).toBeInTheDocument();
  });

  it('archives an active account from the row menu and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);
    const brdLink = await screen.findByRole('link', { name: 'BRD Visa' });
    const brdRow = brdLink.closest('[data-testid="account-row"]');
    await user.click(
      (brdRow as HTMLElement).querySelector('[data-testid="account-actions"]') as HTMLElement,
    );
    await user.click(await screen.findByTestId('archive-account'));
    expect(await screen.findByText('Archived "BRD Visa"')).toBeInTheDocument();
  });

  it('shows an error toast when archiving fails', async () => {
    server.use(
      http.delete('*/accounts/:id', () => HttpResponse.json({ detail: 'no' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);
    const brdLink = await screen.findByRole('link', { name: 'BRD Visa' });
    const brdRow = brdLink.closest('[data-testid="account-row"]');
    await user.click(
      (brdRow as HTMLElement).querySelector('[data-testid="account-actions"]') as HTMLElement,
    );
    await user.click(await screen.findByTestId('archive-account'));
    expect(await screen.findByText('no')).toBeInTheDocument();
  });

  it('shows an error toast when unarchiving fails', async () => {
    server.use(
      http.post('*/accounts/:id/unarchive', () =>
        HttpResponse.json({ detail: 'busy' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<AccountsTable />);
    await user.click(screen.getByTestId('show-archived-toggle'));
    const archivedLink = await screen.findByRole('link', { name: 'Old Revolut' });
    const archivedRow = archivedLink.closest('[data-testid="account-row"]');
    await user.click(
      (archivedRow as HTMLElement).querySelector('[data-testid="account-actions"]') as HTMLElement,
    );
    await user.click(await screen.findByTestId('unarchive-account'));
    expect(await screen.findByText('busy')).toBeInTheDocument();
  });
});
