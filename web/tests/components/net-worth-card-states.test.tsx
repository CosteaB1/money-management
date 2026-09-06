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

/** A complete NetWorthDto body with the missing-FX counts dialled in. */
function netWorthBody(accountsMissing: number, loansMissing: number) {
  return {
    grossAssetsMdl: 48100,
    externalLiabilitiesMdl: 57600,
    externalAssetsMdl: 2000,
    netWorthMdl: -7500,
    accountsMissingFxRate: accountsMissing,
    loansMissingFxRate: loansMissing,
  };
}

describe('NetWorthCard states', () => {
  it('shows the loading skeleton while pending', async () => {
    server.use(
      http.get('*/dashboard/net-worth', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json(netWorthBody(0, 0));
      }),
    );
    renderWithClient(<NetWorthCard />);
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    await screen.findByTestId('net-worth-amount');
  });

  it('shows an error message on failure', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<NetWorthCard />);
    expect(await screen.findByText('Failed to load.')).toBeInTheDocument();
  });

  // A missing account rate understates net worth; a missing loan rate
  // overstates it. The card names the source(s) so the user knows which
  // way the figure is off, and links to the page that fixes it.
  it('names accounts as the incomplete source', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json(netWorthBody(2, 0))));
    renderWithClient(<NetWorthCard />);

    const note = await screen.findByTestId('net-worth-missing-rates');
    expect(note).toHaveTextContent('2 accounts missing FX rate — net worth is incomplete');
    await waitFor(() => {
      expect(screen.getByRole('link')).toHaveAttribute('href', '/settings/fx-rates');
    });
  });

  it('names loans as the incomplete source, singularised', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json(netWorthBody(0, 1))));
    renderWithClient(<NetWorthCard />);

    const note = await screen.findByTestId('net-worth-missing-rates');
    expect(note).toHaveTextContent('1 loan missing FX rate — net worth is incomplete');
  });

  it('names both sources when accounts and loans are each short a rate', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json(netWorthBody(1, 3))));
    renderWithClient(<NetWorthCard />);

    const note = await screen.findByTestId('net-worth-missing-rates');
    expect(note).toHaveTextContent(
      '1 account and 3 loans missing FX rate — net worth is incomplete',
    );
  });

  it('stays quiet when every rate is available', async () => {
    renderWithClient(<NetWorthCard />);
    await screen.findByTestId('net-worth-amount');
    expect(screen.queryByTestId('net-worth-missing-rates')).not.toBeInTheDocument();
  });
});
