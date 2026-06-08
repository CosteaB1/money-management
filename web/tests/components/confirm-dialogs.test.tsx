import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { DeleteAccountDialog } from '@/src/components/accounts/delete-account-dialog';
import { ArchiveBudgetDialog } from '@/src/components/budgets/archive-budget-dialog';
import { ArchiveGoalDialog } from '@/src/components/goals/archive-goal-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { BudgetDto, GoalDto } from '@/src/types/api';

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

const goal: GoalDto = {
  id: 'g0000001-0000-0000-0000-000000000002',
  name: 'Vacation',
  targetAmount: 10000,
  targetDate: '2026-08-15',
  linkedAccountId: null,
  linkedAccountName: null,
  saved: 4500,
  remaining: 5500,
  progressPercent: 0.45,
  status: 'AtRisk',
  requiredMonthlyContribution: 1830,
  isLinkedMode: false,
  missingFxRate: false,
};

describe('ArchiveBudgetDialog', () => {
  it('archives on confirm and closes', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<ArchiveBudgetDialog budget={budget} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('archive-budget-confirm-button'));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(await screen.findByText(/Archived "Groceries" budget/)).toBeInTheDocument();
  });

  it('cancel closes without archiving', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<ArchiveBudgetDialog budget={budget} open onOpenChange={onOpenChange} />);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows an error toast when archive fails', async () => {
    server.use(
      http.delete('*/budgets/:id', () =>
        HttpResponse.json({ detail: 'cannot archive' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<ArchiveBudgetDialog budget={budget} open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('archive-budget-confirm-button'));
    expect(await screen.findByText('cannot archive')).toBeInTheDocument();
  });
});

describe('ArchiveGoalDialog', () => {
  it('archives on confirm', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<ArchiveGoalDialog goal={goal} open onOpenChange={onOpenChange} />);
    await user.click(screen.getByTestId('archive-goal-confirm-button'));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(await screen.findByText(/Archived "Vacation" goal/)).toBeInTheDocument();
  });

  it('shows an error toast when archive fails', async () => {
    server.use(
      http.delete('*/goals/:id', () => HttpResponse.json({ detail: 'nope' }, { status: 400 })),
    );
    const user = userEvent.setup();
    renderWithClient(<ArchiveGoalDialog goal={goal} open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('archive-goal-confirm-button'));
    expect(await screen.findByText('nope')).toBeInTheDocument();
  });
});

describe('DeleteAccountDialog', () => {
  it('deletes cleanly and fires onDeleted', async () => {
    const onOpenChange = vi.fn();
    const onDeleted = vi.fn();
    const user = userEvent.setup();
    renderWithClient(
      <DeleteAccountDialog
        account={{ id: '33333333-3333-3333-3333-333333333333', name: 'ING Savings' }}
        open
        onOpenChange={onOpenChange}
        onDeleted={onDeleted}
      />,
    );

    await user.click(screen.getByTestId('delete-account-confirm-button'));
    await waitFor(() => expect(onDeleted).toHaveBeenCalled());
    expect(await screen.findByText(/Deleted "ING Savings"/)).toBeInTheDocument();
  });

  it('surfaces the 409 conflict message verbatim', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <DeleteAccountDialog
        account={{ id: '11111111-1111-1111-1111-111111111111', name: 'Cash Wallet' }}
        open
        onOpenChange={vi.fn()}
      />,
    );

    await user.click(screen.getByTestId('delete-account-confirm-button'));
    expect(await screen.findByText(/Archive it instead/)).toBeInTheDocument();
  });
});
