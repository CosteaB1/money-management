import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AccountDetailView } from '@/src/components/accounts/detail/account-detail-view';
import { server } from '@/src/lib/mocks/server';

// AccountDetailHeader (rendered inside the view) calls `useRouter().push` from
// its Delete-permanently dialog's onDeleted. The default next/navigation stub
// throws without an App-Router context in jsdom, so mock the hook to a noop.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts');
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactNode }) => (
      <div style={{ width: 800, height: 300 }}>{children}</div>
    ),
  };
});

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('AccountDetailView', () => {
  it('renders the skeleton during the initial fetch', async () => {
    server.use(
      http.get('*/accounts/:id', async () => {
        // Keep the promise pending long enough for the loading branch to
        // be observable.
        await new Promise((r) => setTimeout(r, 50));
        return HttpResponse.json({
          id: 'pending',
          name: 'Pending',
          type: 'Cash',
          currency: 'MDL',
          openingDate: '2025-01-01',
          isArchived: false,
          notes: null,
          balance: 0,
          balanceMdl: 0,
          initialCapital: 0,
          allTime: {
            contributionsMdl: 0,
            withdrawalsMdl: 0,
            netPnLMdl: 0,
            contributionCount: 0,
            withdrawalCount: 0,
            adjustmentCount: 0,
            missingFxRate: false,
          },
          yearToDate: {
            contributionsMdl: 0,
            withdrawalsMdl: 0,
            netPnLMdl: 0,
            contributionCount: 0,
            withdrawalCount: 0,
            adjustmentCount: 0,
            missingFxRate: false,
          },
          firstActivityDate: null,
          lastActivityDate: null,
          realActivityCount: 0,
        });
      }),
    );

    renderWithClient(<AccountDetailView id="44444444-4444-4444-4444-444444444444" />);
    expect(screen.getByTestId('account-detail-skeleton')).toBeInTheDocument();
  });

  it('renders the full detail view on a successful fetch', async () => {
    renderWithClient(<AccountDetailView id="44444444-4444-4444-4444-444444444444" />);

    await waitFor(() => {
      expect(screen.getByTestId('account-detail-view')).toBeInTheDocument();
    });
    expect(screen.getByTestId('account-detail-name')).toHaveTextContent('XTB');
    expect(screen.getByTestId('performance-card')).toBeInTheDocument();
    expect(screen.getByTestId('balance-trend-card')).toBeInTheDocument();
    expect(screen.getByTestId('activity-section')).toBeInTheDocument();
  });

  it('renders the not-found error state when the backend returns 404', async () => {
    renderWithClient(<AccountDetailView id="does-not-exist" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('account-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('true');
    });
    expect(screen.getByText(/account not found/i)).toBeInTheDocument();
  });

  it('renders the generic error state for non-404 failures', async () => {
    server.use(
      http.get('*/accounts/:id', () => HttpResponse.json({ error: 'boom' }, { status: 500 })),
    );

    renderWithClient(<AccountDetailView id="44444444-4444-4444-4444-444444444444" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('account-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('false');
    });
    expect(screen.getByText(/failed to load account/i)).toBeInTheDocument();
  });

  it('renders the defensive generic error when the query is disabled (empty id)', () => {
    renderWithClient(<AccountDetailView id="" />);
    const errorBlock = screen.getByTestId('account-detail-error');
    expect(errorBlock).toBeInTheDocument();
    expect(errorBlock.getAttribute('data-not-found')).toBe('false');
  });
});
