import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { BalanceOverTimeSection } from '@/src/components/reports/balance-over-time-section';
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

describe('BalanceOverTimeSection', () => {
  it('prompts the user to pick an account before fetching', () => {
    renderWithClient(<BalanceOverTimeSection />);
    expect(screen.getByTestId('balance-over-time-section-empty-account')).toBeInTheDocument();
  });

  it('renders only the native line when the selected account currency is MDL', async () => {
    const user = userEvent.setup();
    renderWithClient(<BalanceOverTimeSection />);

    await waitFor(() => {
      // Wait for the accounts list to populate so we can pick one.
      expect(screen.getByTestId('balance-account')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('balance-account'));
    const cashOption = await screen.findByRole('option', { name: /cash wallet/i });
    await user.click(cashOption);

    await waitFor(() => {
      expect(screen.getByTestId('balance-over-time-chart')).toBeInTheDocument();
    });

    // sr-only enumeration of points should be present.
    expect(screen.getAllByTestId('balance-over-time-point').length).toBeGreaterThan(0);

    // MDL account → no secondary MDL line.
    expect(screen.queryByTestId('balance-mdl-line')).not.toBeInTheDocument();
  });

  it('plots only the native line but keeps the MDL equivalent when account currency != MDL', async () => {
    const user = userEvent.setup();
    renderWithClient(<BalanceOverTimeSection />);

    await waitFor(() => {
      expect(screen.getByTestId('balance-account')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('balance-account'));
    const xtbOption = await screen.findByRole('option', { name: /xtb/i });
    await user.click(xtbOption);

    await waitFor(() => {
      expect(screen.getByTestId('balance-over-time-chart')).toBeInTheDocument();
    });

    // USD account → the chart plots ONLY the native line; the redundant
    // MDL-equivalent line (which would blow up the native Y-axis ~17x) is
    // no longer rendered. The old sentinel testid is gone.
    expect(screen.queryByTestId('balance-mdl-line')).not.toBeInTheDocument();
    // The wrapper still flags MDL availability — it gates the tooltip's
    // MDL row and the sr-only "≈" suffix, not a second plotted line.
    expect(screen.getByTestId('balance-over-time-chart').getAttribute('data-show-mdl')).toBe(
      'true',
    );

    // The MDL equivalent survives in the sr-only enumeration (mirror of the
    // hover tooltip). The `≈` arrow we render before it is the reliable
    // sentinel — the value itself is formatted by `Intl.NumberFormat`.
    const points = screen.getAllByTestId('balance-over-time-point');
    expect(points[0]?.textContent ?? '').toMatch(/≈/);
  });

  it('renders an error state when the endpoint returns 500 after an account is picked', async () => {
    server.use(
      http.get('*/reports/balance-over-time', () =>
        HttpResponse.json({ error: 'boom' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<BalanceOverTimeSection />);

    await waitFor(() => {
      expect(screen.getByTestId('balance-account')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('balance-account'));
    const cashOption = await screen.findByRole('option', { name: /cash wallet/i });
    await user.click(cashOption);

    await waitFor(() => {
      expect(screen.getByTestId('balance-over-time-section-error')).toBeInTheDocument();
    });
  });
});
