import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { GoalDetailView } from '@/src/components/goals/detail/goal-detail-view';
import { server } from '@/src/lib/mocks/server';

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

describe('GoalDetailView', () => {
  it('renders the skeleton during the initial fetch', async () => {
    server.use(
      http.get('*/goals/:id', async () => {
        // Keep the promise pending long enough for the loading branch to
        // be observable in the test.
        await new Promise((r) => setTimeout(r, 50));
        return HttpResponse.json({
          id: 'pending',
          name: 'Pending',
          targetAmount: 0,
          targetDate: null,
          linkedAccountId: null,
          linkedAccountName: null,
          saved: 0,
          remaining: 0,
          progressPercent: 0,
          status: 'OnTrack',
          requiredMonthlyContribution: null,
          isLinkedMode: false,
          missingFxRate: false,
          createdOn: '2025-01-01',
          isArchived: false,
          pace: {
            avgMonthlyContribution: null,
            projectedCompletionDate: null,
            monthsToAchieveAtPace: null,
          },
          contributions: [],
          savedHistory: [],
        });
      }),
    );

    renderWithClient(<GoalDetailView id="g0000001-0000-0000-0000-000000000001" />);
    expect(screen.getByTestId('goal-detail-skeleton')).toBeInTheDocument();
  });

  it('renders the full detail view on a successful fetch', async () => {
    renderWithClient(<GoalDetailView id="g0000001-0000-0000-0000-000000000001" />);

    await waitFor(() => {
      expect(screen.getByTestId('goal-detail-view')).toBeInTheDocument();
    });
    expect(screen.getByTestId('goal-detail-name')).toHaveTextContent('Emergency fund');
    expect(screen.getByTestId('goal-progress-card')).toBeInTheDocument();
    expect(screen.getByTestId('goal-pace-card')).toBeInTheDocument();
    expect(screen.getByTestId('goal-history-card')).toBeInTheDocument();
    expect(screen.getByTestId('goal-contributions-section')).toBeInTheDocument();
  });

  it('renders the not-found error state when the backend returns 404', async () => {
    renderWithClient(<GoalDetailView id="does-not-exist" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('goal-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('true');
    });
    expect(screen.getByText(/goal not found/i)).toBeInTheDocument();
  });

  it('renders the generic error state for non-404 failures', async () => {
    server.use(
      http.get('*/goals/:id', () => HttpResponse.json({ error: 'boom' }, { status: 500 })),
    );

    renderWithClient(<GoalDetailView id="g0000001-0000-0000-0000-000000000001" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('goal-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('false');
    });
    expect(screen.getByText(/failed to load goal/i)).toBeInTheDocument();
  });

  it('renders the defensive generic error when the query is disabled (empty id)', () => {
    // An empty id disables the query → no loading, no error, no data → the
    // defensive `!data` branch renders a generic error.
    renderWithClient(<GoalDetailView id="" />);
    const errorBlock = screen.getByTestId('goal-detail-error');
    expect(errorBlock).toBeInTheDocument();
    expect(errorBlock.getAttribute('data-not-found')).toBe('false');
  });
});
