import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { EditGoalDialog } from '@/src/components/goals/edit-goal-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { GoalDto } from '@/src/types/api';

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

const manualGoal: GoalDto = {
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

const linkedGoal: GoalDto = {
  ...manualGoal,
  id: 'g0000001-0000-0000-0000-000000000001',
  name: 'Emergency fund',
  linkedAccountId: '33333333-3333-3333-3333-333333333333',
  linkedAccountName: 'ING Savings',
  isLinkedMode: true,
};

describe('EditGoalDialog', () => {
  it('saves an edited manual goal', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    const name = screen.getByTestId('edit-goal-name-input');
    await user.clear(name);
    await user.type(name, 'Big vacation');
    await user.click(screen.getByTestId('edit-goal-submit-button'));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(await screen.findByText('Goal updated')).toBeInTheDocument();
  });

  it('switches a linked goal to manual mode (hides the account select)', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={linkedGoal} open onOpenChange={vi.fn()} />);

    expect(screen.getByTestId('edit-goal-linked-account-select')).toBeInTheDocument();
    await user.click(screen.getByTestId('edit-goal-mode-manual'));
    await waitFor(() => {
      expect(screen.queryByTestId('edit-goal-linked-account-select')).not.toBeInTheDocument();
    });
  });

  it('switches a manual goal to linked, picks an account, and saves', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('edit-goal-mode-linked'));
    await user.click(await screen.findByTestId('edit-goal-linked-account-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));
    await user.click(screen.getByTestId('edit-goal-submit-button'));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it('rejects an empty name', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={vi.fn()} />);
    await user.clear(screen.getByTestId('edit-goal-name-input'));
    await user.click(screen.getByTestId('edit-goal-submit-button'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
  });

  it('rejects a non-positive target amount', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={vi.fn()} />);
    const target = screen.getByTestId('edit-goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '0');
    await user.click(screen.getByTestId('edit-goal-submit-button'));
    expect(await screen.findByText('Target amount must be greater than 0')).toBeInTheDocument();
  });

  it('rejects a past target date', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={vi.fn()} />);
    const dateInput = screen.getByTestId('edit-goal-target-date-input') as HTMLInputElement;
    // Remove the min constraint so jsdom accepts the past value, then change it.
    dateInput.removeAttribute('min');
    fireEvent.change(dateInput, { target: { value: '2020-01-01' } });
    await user.click(screen.getByTestId('edit-goal-submit-button'));
    expect(await screen.findByText('Target date cannot be in the past')).toBeInTheDocument();
  });

  it('surfaces a 404 on the linked-account field', async () => {
    server.use(
      http.put('*/goals/:id', () => HttpResponse.json({ detail: 'no account' }, { status: 404 })),
    );
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={linkedGoal} open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('edit-goal-submit-button'));
    expect(await screen.findByText('Linked account not found.')).toBeInTheDocument();
  });

  it('shows a generic error toast for a non-404 failure', async () => {
    server.use(
      http.put('*/goals/:id', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<EditGoalDialog goal={manualGoal} open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('edit-goal-submit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });
});
