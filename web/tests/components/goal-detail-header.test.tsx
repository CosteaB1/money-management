import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { GoalDetailHeader } from '@/src/components/goals/detail/goal-detail-header';
import type { GoalDetailDto } from '@/src/types/api';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const basePace = {
  avgMonthlyContribution: 2500,
  projectedCompletionDate: '2027-04-01',
  monthsToAchieveAtPace: 11,
};

const linkedGoal: GoalDetailDto = {
  id: 'g-1',
  name: 'Emergency fund',
  targetAmount: 50000,
  targetDate: '2026-12-31',
  linkedAccountId: 'acc-ing',
  linkedAccountName: 'ING Savings',
  saved: 22550,
  remaining: 27450,
  progressPercent: 22550 / 50000,
  status: 'OnTrack',
  requiredMonthlyContribution: 3920,
  isLinkedMode: true,
  missingFxRate: false,
  createdOn: '2025-08-01',
  isArchived: false,
  pace: basePace,
  contributions: [],
  savedHistory: [],
};

const manualGoal: GoalDetailDto = {
  ...linkedGoal,
  id: 'g-2',
  name: 'Vacation',
  targetAmount: 10000,
  saved: 4500,
  remaining: 5500,
  progressPercent: 0.45,
  status: 'AtRisk',
  linkedAccountId: null,
  linkedAccountName: null,
  isLinkedMode: false,
};

describe('GoalDetailHeader', () => {
  it('renders name, target badge, target-date subtitle, and the Linked mode badge with account name', () => {
    renderWithClient(<GoalDetailHeader goal={linkedGoal} />);

    expect(screen.getByTestId('goal-detail-name')).toHaveTextContent('Emergency fund');
    // 50.000 / 50,000 — Romanian locale separators vary; just check the digits.
    expect(screen.getByTestId('goal-detail-target')).toHaveTextContent(/50[.\s]?000/);
    expect(screen.getByTestId('goal-detail-mode')).toHaveTextContent(/Linked:\s+ING Savings/);
    expect(screen.getByTestId('goal-detail-target-date')).toHaveTextContent(/Target date/);
  });

  it('renders the Manual mode badge when the goal is not linked', () => {
    renderWithClient(<GoalDetailHeader goal={manualGoal} />);
    expect(screen.getByTestId('goal-detail-mode')).toHaveTextContent(/^Manual$/);
  });

  it('surfaces the Archived badge when the goal is archived AND hides the action group', () => {
    renderWithClient(<GoalDetailHeader goal={{ ...linkedGoal, isArchived: true }} />);
    expect(screen.getByTestId('goal-detail-archived')).toBeInTheDocument();
    expect(screen.queryByTestId('goal-detail-actions')).not.toBeInTheDocument();
  });

  it('hides "Update saved" for linked-mode goals', () => {
    renderWithClient(<GoalDetailHeader goal={linkedGoal} />);
    expect(screen.queryByTestId('goal-detail-update-saved')).not.toBeInTheDocument();
    // The other two actions are always present on active goals.
    expect(screen.getByTestId('goal-detail-edit')).toBeInTheDocument();
    expect(screen.getByTestId('goal-detail-archive')).toBeInTheDocument();
  });

  it('shows "Update saved" for manual-mode goals', () => {
    renderWithClient(<GoalDetailHeader goal={manualGoal} />);
    expect(screen.getByTestId('goal-detail-update-saved')).toBeInTheDocument();
  });

  it('renders an accessible Back-to-goals link', () => {
    renderWithClient(<GoalDetailHeader goal={linkedGoal} />);
    const link = screen.getByTestId('goal-detail-back');
    expect(link).toHaveAttribute('href', '/goals');
    expect(link).toHaveAccessibleName(/back to goals/i);
  });

  it('renders an em-dash when no target date is set', () => {
    renderWithClient(<GoalDetailHeader goal={{ ...manualGoal, targetDate: null }} />);
    expect(screen.getByTestId('goal-detail-target-date')).toHaveTextContent(/—/);
  });
});
