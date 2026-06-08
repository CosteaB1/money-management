import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { ActivitySection } from '@/src/components/accounts/detail/activity-section';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const ACCOUNT_ID = '44444444-4444-4444-4444-444444444444';

interface Captured {
  url: string;
  params: Record<string, string>;
}

function captureTransactions(captured: Captured[]) {
  return http.get('*/transactions', ({ request }) => {
    const url = new URL(request.url);
    const params: Record<string, string> = {};
    url.searchParams.forEach((value, key) => {
      params[key] = value;
    });
    captured.push({ url: request.url, params });
    return HttpResponse.json({
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 25,
      totalPages: 0,
    });
  });
}

describe('ActivitySection', () => {
  it('fires the All preset by default (accountId only)', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));

    renderWithClient(<ActivitySection accountId={ACCOUNT_ID} />);

    await waitFor(() => {
      expect(captured.length).toBeGreaterThan(0);
    });
    const last = captured.at(-1)!;
    expect(last.params.accountId).toBe(ACCOUNT_ID);
    expect(last.params.isTransfer).toBeUndefined();
    expect(last.params.isAdjustment).toBeUndefined();
    expect(last.params.direction).toBeUndefined();
  });

  it('Contributions tab fires isTransfer=true & direction=Income', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));
    const user = userEvent.setup();

    renderWithClient(<ActivitySection accountId={ACCOUNT_ID} />);

    await user.click(screen.getByTestId('activity-tab-contributions'));

    await waitFor(() => {
      const matched = captured.find(
        (c) => c.params.isTransfer === 'true' && c.params.direction === 'Income',
      );
      expect(matched).toBeDefined();
    });
  });

  it('Withdrawals tab fires isTransfer=true & direction=Expense', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));
    const user = userEvent.setup();

    renderWithClient(<ActivitySection accountId={ACCOUNT_ID} />);

    await user.click(screen.getByTestId('activity-tab-withdrawals'));

    await waitFor(() => {
      const matched = captured.find(
        (c) => c.params.isTransfer === 'true' && c.params.direction === 'Expense',
      );
      expect(matched).toBeDefined();
    });
  });

  it('Adjustments tab fires isAdjustment=true', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));
    const user = userEvent.setup();

    renderWithClient(<ActivitySection accountId={ACCOUNT_ID} />);

    await user.click(screen.getByTestId('activity-tab-adjustments'));

    await waitFor(() => {
      const matched = captured.find((c) => c.params.isAdjustment === 'true');
      expect(matched).toBeDefined();
    });
  });

  it('Other tab fires isTransfer=false & isAdjustment=false', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));
    const user = userEvent.setup();

    renderWithClient(<ActivitySection accountId={ACCOUNT_ID} />);

    await user.click(screen.getByTestId('activity-tab-other'));

    await waitFor(() => {
      const matched = captured.find(
        (c) => c.params.isTransfer === 'false' && c.params.isAdjustment === 'false',
      );
      expect(matched).toBeDefined();
    });
  });

  it('pins the opening-balance row on the All tab even with no transactions', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));

    renderWithClient(
      <ActivitySection
        accountId={ACCOUNT_ID}
        openingBalance={1000}
        openingDate="2024-09-10"
        currency="USD"
      />,
    );

    const row = await screen.findByTestId('activity-opening-balance-row');
    expect(row).toHaveTextContent(/opening balance/i);
    // 1000 USD → "1.000,00 USD" in the ro-MD locale.
    expect(row).toHaveTextContent(/1[.\s]?000/);
    expect(row).toHaveTextContent(/USD/);
  });

  it('hides the opening-balance row on the Contributions tab', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));
    const user = userEvent.setup();

    renderWithClient(
      <ActivitySection
        accountId={ACCOUNT_ID}
        openingBalance={1000}
        openingDate="2024-09-10"
        currency="USD"
      />,
    );

    // Present on the default All tab.
    await screen.findByTestId('activity-opening-balance-row');

    await user.click(screen.getByTestId('activity-tab-contributions'));

    await waitFor(() => {
      expect(screen.queryByTestId('activity-opening-balance-row')).toBeNull();
    });
  });

  it('renders the opening-balance row even for a zero opening balance', async () => {
    const captured: Captured[] = [];
    server.use(captureTransactions(captured));

    renderWithClient(
      <ActivitySection
        accountId={ACCOUNT_ID}
        openingBalance={0}
        openingDate="2024-09-10"
        currency="USD"
      />,
    );

    // A zero opening balance is still the account's starting point, so the
    // row stays visible.
    const row = await screen.findByTestId('activity-opening-balance-row');
    expect(row).toHaveTextContent(/opening balance/i);
    expect(row).toHaveTextContent(/0/);
  });
});
