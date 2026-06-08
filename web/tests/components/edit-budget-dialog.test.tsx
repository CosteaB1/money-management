import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { EditBudgetDialog } from '@/src/components/budgets/edit-budget-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { BudgetDto } from '@/src/types/api';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      {ui}
      <Toaster />
    </QueryClientProvider>,
  );
}

const budget: BudgetDto = {
  id: 'b0000001-0000-0000-0000-000000000001',
  categoryId: 'c0000001-0000-0000-0000-000000000001',
  categoryName: 'Groceries',
  monthlyLimit: 3000,
  spent: 1200,
  remaining: 1800,
  status: 'OnTrack',
  year: 2026,
  month: 5,
};

describe('EditBudgetDialog', () => {
  it('shows the read-only category and current spend, then saves a new limit', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<EditBudgetDialog budget={budget} open onOpenChange={onOpenChange} />);

    expect(screen.getByTestId('edit-budget-category-readonly')).toHaveTextContent('Groceries');

    const input = screen.getByTestId('edit-budget-monthly-limit-input');
    await user.clear(input);
    await user.type(input, '3500');
    await user.click(screen.getByTestId('edit-budget-submit-button'));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(await screen.findByText('Budget updated')).toBeInTheDocument();
  });

  it('rejects a non-positive limit via Zod', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditBudgetDialog budget={budget} open onOpenChange={vi.fn()} />);
    const input = screen.getByTestId('edit-budget-monthly-limit-input');
    await user.clear(input);
    await user.type(input, '0');
    await user.click(screen.getByTestId('edit-budget-submit-button'));
    expect(await screen.findByText('Monthly limit must be greater than 0')).toBeInTheDocument();
  });

  it('surfaces a 404 as an inline field error', async () => {
    server.use(
      http.put('*/budgets/:id', () => HttpResponse.json({ detail: 'gone' }, { status: 404 })),
    );
    const user = userEvent.setup();
    renderWithClient(<EditBudgetDialog budget={budget} open onOpenChange={vi.fn()} />);
    const input = screen.getByTestId('edit-budget-monthly-limit-input');
    await user.clear(input);
    await user.type(input, '4000');
    await user.click(screen.getByTestId('edit-budget-submit-button'));
    expect(await screen.findByText('Budget not found.')).toBeInTheDocument();
  });

  it('shows an error toast for a non-404 failure', async () => {
    server.use(
      http.put('*/budgets/:id', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<EditBudgetDialog budget={budget} open onOpenChange={vi.fn()} />);
    const input = screen.getByTestId('edit-budget-monthly-limit-input');
    await user.clear(input);
    await user.type(input, '4000');
    await user.click(screen.getByTestId('edit-budget-submit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });
});
