import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  buildYoyPairs,
  YearOverYearSection,
} from '@/src/components/reports/year-over-year-section';
import { server } from '@/src/lib/mocks/server';
import type { MonthlySummaryReportRow } from '@/src/types/api';

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

function buildSyntheticSeries(): MonthlySummaryReportRow[] {
  // 24 rows: prior 12 (2024-06 → 2025-05) followed by current 12
  // (2025-06 → 2026-05). Each month gets a recognizable pair: prior=100,
  // current=200 in month index 0, and so on, so the pairing assertions
  // below can read the values straight off.
  const rows: MonthlySummaryReportRow[] = [];
  for (let i = 0; i < 24; i++) {
    const baseDate = new Date(2024, 5 + i, 1); // start 2024-06
    const month = `${baseDate.getFullYear()}-${String(baseDate.getMonth() + 1).padStart(2, '0')}`;
    rows.push({
      month,
      income: 1000 + i,
      expense: 800 + i,
      net: 200 + i,
      savingsRate: 0.2,
      transactionCount: 10,
      missingFxRate: false,
    });
  }
  return rows;
}

describe('YearOverYearSection', () => {
  it('buildYoyPairs splits a 24-month series into prior 12 + current 12 paired by month-of-year', () => {
    const rows = buildSyntheticSeries();
    const pairs = buildYoyPairs(rows);
    // 12 paired months, one per month-of-year in the current window.
    expect(pairs.length).toBe(12);
    // First pair: month index 5 = June (2025-06 current, 2024-06 prior).
    expect(pairs[0]?.priorMonth).toBe('2024-06');
    expect(pairs[0]?.currentMonth).toBe('2025-06');
    // Final pair sits at the most recent month in `current` = 2026-05.
    const last = pairs[pairs.length - 1];
    expect(last?.currentMonth).toBe('2026-05');
    expect(last?.priorMonth).toBe('2025-05');
  });

  it('renders the trailing 12 paired rows in the year-over-year table', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () => HttpResponse.json(buildSyntheticSeries())),
    );

    renderWithClient(<YearOverYearSection />);
    await waitFor(() => {
      expect(screen.getByTestId('year-over-year-chart')).toBeInTheDocument();
    });

    const rows = screen.getAllByTestId('year-over-year-row');
    expect(rows.length).toBe(12);
    const points = screen.getAllByTestId('year-over-year-point');
    expect(points.length).toBe(12);
  });

  it('renders an error state on a failed request', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json({ error: 'boom' }, { status: 500 }),
      ),
    );
    renderWithClient(<YearOverYearSection />);
    await waitFor(() => {
      expect(screen.getByTestId('year-over-year-section-error')).toBeInTheDocument();
    });
  });

  it('shares the same monthly-summary cache as the dedicated Monthly Summary tab', async () => {
    // Counter only increments per network call. If TanStack Query reuses
    // the cache by query key, two consecutive renders pointing at the
    // same range hit the network once.
    let calls = 0;
    server.use(
      http.get('*/reports/monthly-summary', () => {
        calls += 1;
        return HttpResponse.json(buildSyntheticSeries());
      }),
    );

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <YearOverYearSection />
        <YearOverYearSection />
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(calls).toBe(1);
    });
  });
});
