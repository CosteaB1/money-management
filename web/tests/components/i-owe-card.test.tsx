import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { IOweCard } from '@/src/components/dashboard/i-owe-card';
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

describe('IOweCard', () => {
  it('renders outstanding borrowings as a positive magnitude', async () => {
    renderWithClient(<IOweCard />);

    const amount = await screen.findByTestId('i-owe-dashboard-amount');
    expect(amount.textContent ?? '').toMatch(/57\D?600/);
    expect(amount.textContent ?? '').not.toMatch(/-/);
    expect(amount.textContent ?? '').toMatch(/(MDL|L)/);
    expect(screen.getByText('I owe')).toBeInTheDocument();
  });

  // The loans page has its own "I owe" tile under `i-owe-card`; this one
  // must not shadow it when both render in the same test run.
  it('uses a dashboard-scoped testid that cannot collide with the loans page tile', async () => {
    renderWithClient(<IOweCard />);
    await screen.findByTestId('i-owe-dashboard-amount');
    expect(screen.getByTestId('i-owe-dashboard-card')).toBeInTheDocument();
    expect(screen.queryByTestId('i-owe-card')).not.toBeInTheDocument();
  });

  it('links through to the loans page', async () => {
    renderWithClient(<IOweCard />);
    const link = await screen.findByTestId('i-owe-dashboard-link');
    expect(link).toHaveAttribute('href', '/loans');
  });

  it('shows the loading skeleton while pending', async () => {
    server.use(
      http.get('*/dashboard/net-worth', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json(netWorthBody());
      }),
    );
    renderWithClient(<IOweCard />);
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    await screen.findByTestId('i-owe-dashboard-amount');
  });

  it('shows an error message on failure', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<IOweCard />);
    expect(await screen.findByText('Failed to load.')).toBeInTheDocument();
  });

  it('warns when loans are missing an FX rate and links to the fix', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json(netWorthBody({ loansMissingFxRate: 2 })),
      ),
    );
    renderWithClient(<IOweCard />);

    const note = await screen.findByTestId('i-owe-dashboard-missing-rates');
    expect(note).toHaveTextContent('2 loans missing FX rate — total is incomplete');
    expect(note.querySelector('a')).toHaveAttribute('href', '/settings/fx-rates');
  });

  it('ignores account FX gaps — they cannot dent a loans-only total', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json(netWorthBody({ accountsMissingFxRate: 3 })),
      ),
    );
    renderWithClient(<IOweCard />);
    await screen.findByTestId('i-owe-dashboard-amount');
    expect(screen.queryByTestId('i-owe-dashboard-missing-rates')).not.toBeInTheDocument();
  });
});
