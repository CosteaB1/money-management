import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { RecentTransactions } from '@/src/components/dashboard/recent-transactions';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('RecentTransactions', () => {
  it('renders the seeded transactions with transfer + adjustment badges', async () => {
    renderWithClient(<RecentTransactions />);

    await waitFor(() => {
      expect(screen.getByText('Coffee at Tucano')).toBeInTheDocument();
    });

    // Transfer rows render a Transfer badge; the XTB adjustment row a
    // "Balance adjustment" badge with a USD native + MDL-eq amount line.
    expect(screen.getAllByText('Transfer').length).toBeGreaterThan(0);
    // "Balance adjustment" appears both as the row description and the badge.
    expect(screen.getAllByText('Balance adjustment').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Uncategorized').length).toBeGreaterThan(0);
  });

  it('renders a categorized income row in emerald with its category name', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({
          items: [
            {
              id: 'tx-income',
              accountId: '11111111-1111-1111-1111-111111111111',
              transactionDate: '2025-05-20',
              direction: 'Income',
              amount: 5000,
              description: 'Monthly paycheck',
              categoryName: 'Salary',
              notes: null,
              source: 'Manual',
              isTransfer: false,
              counterAccountId: null,
              currency: 'MDL',
              amountMdl: 5000,
              isAdjustment: false,
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 10,
          totalPages: 1,
        }),
      ),
    );

    renderWithClient(<RecentTransactions />);
    expect(await screen.findByText('Monthly paycheck')).toBeInTheDocument();
    // The category name renders as a muted span (not the Uncategorized badge).
    const categoryCell = screen.getByText('Salary', { selector: 'span.text-muted-foreground' });
    expect(categoryCell).toBeInTheDocument();
  });

  it('shows the empty state when there are no transactions', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 10,
          totalPages: 1,
        }),
      ),
    );

    renderWithClient(<RecentTransactions />);
    expect(await screen.findByText('No transactions yet.')).toBeInTheDocument();
  });

  it('renders an em dash when a non-MDL row has no MDL-eq amount', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({
          items: [
            {
              id: 'tx-no-rate',
              accountId: '44444444-4444-4444-4444-444444444444',
              transactionDate: '2025-05-12',
              direction: 'Expense',
              amount: 10,
              description: 'USD no rate',
              notes: null,
              source: 'Manual',
              isTransfer: false,
              counterAccountId: null,
              currency: 'USD',
              amountMdl: null,
              isAdjustment: false,
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 10,
          totalPages: 1,
        }),
      ),
    );

    renderWithClient(<RecentTransactions />);
    expect(await screen.findByText('USD no rate')).toBeInTheDocument();
    const noRate = screen.getByTitle(/No FX rate available for USD/);
    expect(noRate).toHaveTextContent('—');
  });
});
