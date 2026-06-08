import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { MonthlySummarySection } from '@/src/components/reports/monthly-summary-section';
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

describe('MonthlySummarySection', () => {
  it('renders a loading skeleton while the query is pending', () => {
    server.use(http.get('*/reports/monthly-summary', () => new Promise(() => {})));
    renderWithClient(<MonthlySummarySection />);
    expect(screen.getByTestId('monthly-summary-section-loading')).toBeInTheDocument();
  });

  it('renders an error message on a 500', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json({ error: 'boom' }, { status: 500 }),
      ),
    );
    renderWithClient(<MonthlySummarySection />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-section-error')).toBeInTheDocument();
    });
  });

  it('renders the empty hint when the endpoint returns an empty array', async () => {
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json([])));
    renderWithClient(<MonthlySummarySection />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-section-empty')).toBeInTheDocument();
    });
  });

  it('renders one point per month from the response and a row per month in the table', async () => {
    renderWithClient(<MonthlySummarySection />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-section-chart')).toBeInTheDocument();
    });
    // sr-only point list should mirror the seeded handler (3 rows).
    expect(screen.getAllByTestId('monthly-summary-point').length).toBe(3);
    expect(screen.getAllByTestId('monthly-summary-row').length).toBe(3);
    // Default happy-path: no missing-FX warning.
    expect(screen.queryByTestId('monthly-summary-section-missing-fx')).not.toBeInTheDocument();
  });

  it('surfaces the missing-FX warning when any row has missingFxRate=true', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json([
          {
            month: '2026-04',
            income: 1000,
            expense: 800,
            net: 200,
            savingsRate: 0.2,
            transactionCount: 6,
            missingFxRate: false,
          },
          {
            month: '2026-05',
            income: 1100,
            expense: 950,
            net: 150,
            savingsRate: 0.136,
            transactionCount: 7,
            missingFxRate: true,
          },
        ]),
      ),
    );
    renderWithClient(<MonthlySummarySection />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-section-missing-fx')).toBeInTheDocument();
    });
  });
});
