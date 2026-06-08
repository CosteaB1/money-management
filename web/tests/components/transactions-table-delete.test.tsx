import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { TransactionsTable } from '@/src/components/transactions/transactions-table';
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

describe('TransactionsTable delete (allowDelete)', () => {
  it('does not render any delete control when allowDelete is unset', async () => {
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    // Wait for rows to render so we know the table is past its loading state.
    await waitFor(() => {
      expect(screen.getAllByTestId('transaction-row').length).toBeGreaterThan(0);
    });

    expect(screen.queryByTestId('delete-transaction')).not.toBeInTheDocument();
  });

  it('deletes a row through the confirm dialog and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} allowDelete />,
    );

    // Each rendered row gets its own delete control; click the first.
    const triggers = await screen.findAllByTestId('delete-transaction');
    expect(triggers.length).toBeGreaterThan(0);
    await user.click(triggers[0]!);

    const confirm = await screen.findByTestId('delete-transaction-confirm');
    await user.click(confirm);

    // Success toast renders in the Toaster portal — match by visible text.
    await waitFor(() => {
      expect(screen.getByText('Transaction deleted')).toBeInTheDocument();
    });

    // The error fallback must never appear on the happy path.
    expect(screen.queryByText('Failed to delete transaction')).not.toBeInTheDocument();

    // Dialog closes on success.
    await waitFor(() => {
      expect(screen.queryByTestId('delete-transaction-confirm')).not.toBeInTheDocument();
    });
  });

  it('surfaces an error toast when the delete request fails', async () => {
    server.use(
      http.delete('*/transactions/:id', () =>
        HttpResponse.json(
          {
            type: 'transaction.delete_failed',
            title: 'Bad Request',
            status: 400,
            detail: 'Could not delete transaction',
            errorCode: 'transaction.delete_failed',
            errorType: 'BadRequest',
          },
          { status: 400 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} allowDelete />,
    );

    const triggers = await screen.findAllByTestId('delete-transaction');
    await user.click(triggers[0]!);
    await user.click(await screen.findByTestId('delete-transaction-confirm'));

    await waitFor(() => {
      expect(screen.getByText('Could not delete transaction')).toBeInTheDocument();
    });
    expect(screen.queryByText('Transaction deleted')).not.toBeInTheDocument();
  });
});
