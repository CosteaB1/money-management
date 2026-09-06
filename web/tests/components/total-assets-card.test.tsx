import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { TotalAssetsCard } from '@/src/components/dashboard/total-assets-card';
import { server } from '@/src/lib/mocks/server';

vi.mock('next/navigation', () => ({ usePathname: () => '/' }));

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function netWorthBody(overrides: Record<string, number> = {}) {
  return {
    grossAssetsMdl: 48100,
    externalLiabilitiesMdl: 57600,
    externalAssetsMdl: 2000,
    netWorthMdl: -7500,
    accountsMissingFxRate: 0,
    loansMissingFxRate: 0,
    ...overrides,
  };
}

describe('TotalAssetsCard', () => {
  it('renders gross account assets, untouched by what the user owes', async () => {
    renderWithClient(<TotalAssetsCard />);

    const amount = await screen.findByTestId('total-assets-amount');
    // Seed gross is 48100; the 57600 of borrowed money must not touch it.
    expect(amount.textContent ?? '').toMatch(/48\D?100/);
    expect(amount.textContent ?? '').toMatch(/(MDL|L)/);
    expect(screen.getByTestId('total-assets-card')).toBeInTheDocument();
    expect(screen.getByText('Total assets')).toBeInTheDocument();
  });

  it('shows the loading skeleton while pending', async () => {
    server.use(
      http.get('*/dashboard/net-worth', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json(netWorthBody());
      }),
    );
    renderWithClient(<TotalAssetsCard />);
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    await screen.findByTestId('total-assets-amount');
  });

  it('shows an error message on failure', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<TotalAssetsCard />);
    expect(await screen.findByText('Failed to load.')).toBeInTheDocument();
  });

  it('warns when accounts are missing an FX rate and links to the fix', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json(netWorthBody({ accountsMissingFxRate: 1 })),
      ),
    );
    renderWithClient(<TotalAssetsCard />);

    const note = await screen.findByTestId('total-assets-missing-rates');
    expect(note).toHaveTextContent('1 account missing FX rate — total is incomplete');
    await waitFor(() => {
      expect(screen.getByRole('link')).toHaveAttribute('href', '/settings/fx-rates');
    });
  });

  it('ignores loan FX gaps — they cannot dent an accounts-only total', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json(netWorthBody({ loansMissingFxRate: 4 })),
      ),
    );
    renderWithClient(<TotalAssetsCard />);
    await screen.findByTestId('total-assets-amount');
    expect(screen.queryByTestId('total-assets-missing-rates')).not.toBeInTheDocument();
  });
});
