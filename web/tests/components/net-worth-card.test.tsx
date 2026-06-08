import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { NetWorthCard } from '@/src/components/dashboard/net-worth-card';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('NetWorthCard', () => {
  it('renders the formatted MDL net worth value from the MDL-equivalent sum', async () => {
    renderWithClient(<NetWorthCard />);

    await waitFor(() => {
      const amount = screen.getByTestId('net-worth-amount');
      // Sum of balanceMdl across mocks:
      // 500 + (-1200) + 22550 + 26250 = 48100 MDL
      // ro-MD formats currency with locale-specific grouping; assert digits only.
      expect(amount.textContent ?? '').toMatch(/48\D?100/);
      expect(amount.textContent ?? '').toMatch(/(MDL|L)/);
    });
  });
});
