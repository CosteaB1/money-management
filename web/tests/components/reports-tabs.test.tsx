import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ReportsTabs } from '@/src/components/reports/reports-tabs';

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
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('ReportsTabs', () => {
  it('renders the tab list and switches between sections', async () => {
    const user = userEvent.setup();
    renderWithClient(<ReportsTabs />);

    expect(screen.getByTestId('reports-tabs')).toBeInTheDocument();
    // Monthly content renders by default.
    await waitFor(() => {
      expect(screen.getByTestId('reports-tab-monthly')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('reports-tab-categories'));
    await user.click(screen.getByTestId('reports-tab-payees'));
    await user.click(screen.getByTestId('reports-tab-balance'));
    await user.click(screen.getByTestId('reports-tab-yoy'));

    // After landing on YoY the trigger should be selected.
    expect(screen.getByTestId('reports-tab-yoy')).toHaveAttribute('data-state', 'active');
  });
});
