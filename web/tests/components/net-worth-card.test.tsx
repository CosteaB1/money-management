import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { NetWorthCard } from '@/src/components/dashboard/net-worth-card';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('NetWorthCard', () => {
  it('renders the server-computed net worth, not a client-side account sum', async () => {
    renderWithClient(<NetWorthCard />);

    await waitFor(() => {
      const amount = screen.getByTestId('net-worth-amount');
      // Seed: 48100 gross - 57600 owed + 2000 lent = -7500 MDL. The old card
      // summed accounts in the browser and would have shown 48100 here.
      // ro-MD formats currency with locale-specific grouping; assert digits only.
      expect(amount.textContent ?? '').toMatch(/7\D?500/);
      expect(amount.textContent ?? '').not.toMatch(/48\D?100/);
      expect(amount.textContent ?? '').toMatch(/(MDL|L)/);
    });
  });

  it('equals gross assets − liabilities + external assets', async () => {
    // The real-world numbers that motivated the split: 183,864 of accounts
    // against 134,380 of borrowed money is 49,484 of actual net worth.
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json({
          grossAssetsMdl: 183864,
          externalLiabilitiesMdl: 134380,
          externalAssetsMdl: 0,
          netWorthMdl: 183864 - 134380 + 0,
          accountsMissingFxRate: 0,
          loansMissingFxRate: 0,
        }),
      ),
    );
    renderWithClient(<NetWorthCard />);

    const amount = await screen.findByTestId('net-worth-amount');
    expect(amount.textContent ?? '').toMatch(/49\D?484/);
  });

  it('surfaces money lent out when it is non-zero', async () => {
    renderWithClient(<NetWorthCard />);

    const line = await screen.findByTestId('net-worth-external-assets');
    expect(line.textContent ?? '').toMatch(/2\D?000/);
    expect(line).toHaveTextContent(/owed to you/);
  });

  it('hides the lent-out line when there is nothing lent, but still nets it in', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json({
          grossAssetsMdl: 48100,
          externalLiabilitiesMdl: 57600,
          externalAssetsMdl: 0,
          netWorthMdl: -9500,
          accountsMissingFxRate: 0,
          loansMissingFxRate: 0,
        }),
      ),
    );
    renderWithClient(<NetWorthCard />);

    const amount = await screen.findByTestId('net-worth-amount');
    expect(amount.textContent ?? '').toMatch(/9\D?500/);
    expect(screen.queryByTestId('net-worth-external-assets')).not.toBeInTheDocument();
  });

  it('explains what the figure means', async () => {
    renderWithClient(<NetWorthCard />);
    await screen.findByTestId('net-worth-amount');
    expect(
      screen.getByText('Everything you own minus everything you owe, in MDL.'),
    ).toBeInTheDocument();
  });
});
