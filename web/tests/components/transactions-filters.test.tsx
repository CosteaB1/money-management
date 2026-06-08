import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  type TransactionFilterState,
  TransactionsFilters,
} from '@/src/components/transactions/transactions-filters';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const baseValue: TransactionFilterState = {
  accountId: 'all',
  from: '2026-05-01',
  to: '2026-05-23',
  categoryId: 'all',
  direction: 'all',
  transfer: 'all',
  adjustment: 'all',
};

describe('TransactionsFilters', () => {
  it('emits account, category, date and tab changes', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<TransactionsFilters value={baseValue} onChange={onChange} />);

    // Account select populated from the accounts query.
    await user.click(screen.getByTestId('filter-account'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ accountId: '11111111-1111-1111-1111-111111111111' }),
    );

    await user.click(screen.getByTestId('filter-category'));
    await user.click(await screen.findByRole('option', { name: 'Groceries' }));
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ categoryId: 'c0000001-0000-0000-0000-000000000001' }),
    );

    await user.click(screen.getByTestId('filter-direction-income'));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ direction: 'Income' }));

    await user.click(screen.getByTestId('filter-transfer-only'));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ transfer: 'transfers' }));

    await user.click(screen.getByTestId('filter-adjustment-exclude'));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ adjustment: 'exclude' }));
  });

  it('emits from/to date changes', () => {
    const onChange = vi.fn();
    renderWithClient(<TransactionsFilters value={baseValue} onChange={onChange} />);

    // Native date inputs don't accept per-character typing in jsdom; set the
    // value directly and fire the change.
    fireEvent.change(screen.getByTestId('filter-from'), { target: { value: '2026-04-01' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ from: '2026-04-01' }));

    fireEvent.change(screen.getByTestId('filter-to'), { target: { value: '2026-04-30' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ to: '2026-04-30' }));
  });
});
