import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { AccountCards } from '@/src/components/dashboard/account-card';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('AccountCards', () => {
  it('renders one card per non-archived account with type label + native balance', async () => {
    renderWithClient(<AccountCards />);

    await waitFor(() => {
      expect(screen.getAllByTestId('account-summary').length).toBe(4);
    });

    expect(screen.getByText('Cash Wallet')).toBeInTheDocument();
    expect(screen.getByText('Brokerage')).toBeInTheDocument();
    // The USD XTB account shows a MDL-equivalent secondary line.
    expect(screen.getByText(/≈/)).toBeInTheDocument();
  });

  it('shows "MDL rate missing" when a non-MDL account has a null MDL-eq balance', async () => {
    server.use(
      http.get('*/accounts', () =>
        HttpResponse.json([
          {
            id: 'a1',
            name: 'No-rate USD',
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

    renderWithClient(<AccountCards />);
    expect(await screen.findByText('MDL rate missing')).toBeInTheDocument();
  });

  it('shows the loading skeleton while accounts are pending', async () => {
    server.use(
      http.get('*/accounts', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json([]);
      }),
    );

    renderWithClient(<AccountCards />);
    expect(screen.getByRole('status', { name: 'Loading accounts' })).toBeInTheDocument();
  });
});
