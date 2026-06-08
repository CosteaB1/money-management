import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { NetWorthCard } from '@/src/components/dashboard/net-worth-card';
import { server } from '@/src/lib/mocks/server';

vi.mock('next/navigation', () => ({ usePathname: () => '/' }));

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('NetWorthCard states', () => {
  it('shows the loading skeleton while pending', async () => {
    server.use(
      http.get('*/accounts', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json([]);
      }),
    );
    renderWithClient(<NetWorthCard />);
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
  });

  it('shows an error message on failure', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<NetWorthCard />);
    expect(await screen.findByText('Failed to load.')).toBeInTheDocument();
  });

  it('links to FX settings when accounts are missing a rate', async () => {
    server.use(
      http.get('*/accounts', () =>
        HttpResponse.json([
          {
            id: 'a1',
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
    renderWithClient(<NetWorthCard />);
    const link = await screen.findByTestId('net-worth-missing-rates');
    expect(link).toHaveTextContent('1 account missing FX rate');
    await waitFor(() => {
      expect(screen.getByRole('link')).toHaveAttribute('href', '/settings/fx-rates');
    });
  });
});
