import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { NetWorthTrendChart } from '@/src/components/dashboard/net-worth-trend-chart';
import { server } from '@/src/lib/mocks/server';

// Recharts' ResponsiveContainer measures its parent via ResizeObserver; jsdom
// reports a 0×0 box, so the chart wouldn't render any SVG and we'd have
// nothing to assert against. Stub it to a fixed-size <div> so the LineChart
// at least mounts. We still assert on the screen-reader fallback list — that's
// the part the component guarantees, regardless of Recharts internals.
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

describe('NetWorthTrendChart', () => {
  it('shows the loading skeleton while the query is in flight', () => {
    server.use(
      http.get(
        '*/dashboard/net-worth-trend',
        () => new Promise(() => {}), // never resolves
      ),
    );
    renderWithClient(<NetWorthTrendChart />);
    expect(screen.getByTestId('net-worth-trend-loading')).toBeInTheDocument();
  });

  it('renders an error message on a failed fetch', async () => {
    server.use(
      http.get('*/dashboard/net-worth-trend', () =>
        HttpResponse.json({ error: 'boom' }, { status: 500 }),
      ),
    );
    renderWithClient(<NetWorthTrendChart />);
    await waitFor(() => {
      expect(screen.getByTestId('net-worth-trend-error')).toBeInTheDocument();
    });
  });

  it('renders 6 points when the endpoint returns a 6-month series', async () => {
    server.use(
      http.get('*/dashboard/net-worth-trend', () =>
        HttpResponse.json([
          { month: '2025-12', netWorthMdl: 40000, missingFxRate: false },
          { month: '2026-01', netWorthMdl: 42500, missingFxRate: false },
          { month: '2026-02', netWorthMdl: 44100, missingFxRate: false },
          { month: '2026-03', netWorthMdl: 45000, missingFxRate: false },
          { month: '2026-04', netWorthMdl: 46500, missingFxRate: false },
          { month: '2026-05', netWorthMdl: 48100, missingFxRate: false },
        ]),
      ),
    );
    renderWithClient(<NetWorthTrendChart />);

    await waitFor(() => {
      expect(screen.getByTestId('net-worth-trend-chart')).toBeInTheDocument();
    });

    // Assert on the sr-only fallback list — Recharts' SVG output is unreliable
    // under jsdom, but the data list is rendered straight from React state.
    const points = screen.getAllByTestId('net-worth-trend-point');
    expect(points.length).toBe(6);

    // The missing-FX warning should be absent in the happy path.
    expect(screen.queryByTestId('net-worth-trend-missing-fx')).not.toBeInTheDocument();
  });

  it('shows the missing-FX warning when any point has missingFxRate=true', async () => {
    server.use(
      http.get('*/dashboard/net-worth-trend', () =>
        HttpResponse.json([
          { month: '2026-04', netWorthMdl: 46500, missingFxRate: false },
          { month: '2026-05', netWorthMdl: 48100, missingFxRate: true },
        ]),
      ),
    );
    renderWithClient(<NetWorthTrendChart />);
    await waitFor(() => {
      expect(screen.getByTestId('net-worth-trend-missing-fx')).toBeInTheDocument();
    });
  });

  it('shows the empty-state message when the endpoint returns no points', async () => {
    server.use(http.get('*/dashboard/net-worth-trend', () => HttpResponse.json([])));
    renderWithClient(<NetWorthTrendChart />);
    await waitFor(() => {
      expect(screen.getByTestId('net-worth-trend-empty')).toBeInTheDocument();
    });
  });
});
