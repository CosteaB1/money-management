import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoalContributionsTable } from '@/src/components/goals/detail/goal-contributions-table';
import type { GoalContributionDto, GoalDetailDto } from '@/src/types/api';

const baseGoal: GoalDetailDto = {
  id: 'g-1',
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
  createdOn: '2026-01-15',
  isArchived: false,
  pace: {
    avgMonthlyContribution: 900,
    projectedCompletionDate: '2026-11-01',
    monthsToAchieveAtPace: 6,
  },
  contributions: [],
  savedHistory: [],
};

const positive: GoalContributionDto = {
  id: 'gc-1',
  amount: 1500,
  occurredOn: '2026-04-30',
  notes: 'Bonus stash',
  source: 'Manual',
};

const negative: GoalContributionDto = {
  id: 'gc-2',
  amount: -300,
  occurredOn: '2026-03-22',
  notes: 'Quick withdrawal',
  source: 'Manual',
};

const linkedRow: GoalContributionDto = {
  id: null,
  amount: 2000,
  occurredOn: '2026-04-12',
  notes: 'Salary deposit',
  source: 'LinkedAccountTransaction',
};

describe('GoalContributionsTable', () => {
  it('renders the empty state when contributions are empty', () => {
    render(<GoalContributionsTable goal={baseGoal} />);
    expect(screen.getByTestId('goal-contributions-empty')).toHaveTextContent(
      /no contributions logged yet/i,
    );
  });

  it('renders rows in the order provided by the backend (already desc by date)', () => {
    render(
      <GoalContributionsTable
        goal={{ ...baseGoal, contributions: [positive, linkedRow, negative] }}
      />,
    );
    const rows = screen.getAllByTestId('goal-contribution-row');
    expect(rows.length).toBe(3);
    // First row's amount should be the +1.500 we passed first.
    expect(rows[0]?.textContent ?? '').toMatch(/\+/);
  });

  it('color-codes positive amounts emerald and negative amounts rose', () => {
    render(<GoalContributionsTable goal={{ ...baseGoal, contributions: [positive, negative] }} />);
    const amounts = screen.getAllByTestId('goal-contribution-amount');
    expect(amounts[0]?.className ?? '').toMatch(/text-emerald-500/);
    expect(amounts[1]?.className ?? '').toMatch(/text-rose-500/);
    // Signed prefix on positives.
    expect(amounts[0]?.textContent ?? '').toMatch(/^\+/);
  });

  it('renders the Manual source badge for manual rows', () => {
    render(<GoalContributionsTable goal={{ ...baseGoal, contributions: [positive] }} />);
    expect(screen.getByTestId('goal-contribution-source-manual')).toHaveTextContent('Manual');
    expect(screen.queryByTestId('goal-contribution-source-linked')).not.toBeInTheDocument();
  });

  it('renders the "From <account>" linked-source badge with the linked account name', () => {
    render(
      <GoalContributionsTable
        goal={{
          ...baseGoal,
          linkedAccountId: 'acc-1',
          linkedAccountName: 'ING Savings',
          isLinkedMode: true,
          contributions: [linkedRow],
        }}
      />,
    );
    expect(screen.getByTestId('goal-contribution-source-linked')).toHaveTextContent(
      /From ING Savings/i,
    );
  });

  it('renders a zero-amount row muted and an em dash when notes are absent', () => {
    const zeroNoNotes: GoalContributionDto = {
      id: 'gc-zero',
      amount: 0,
      occurredOn: '2026-02-01',
      notes: null,
      source: 'Manual',
    };
    render(<GoalContributionsTable goal={{ ...baseGoal, contributions: [zeroNoNotes] }} />);
    const amount = screen.getByTestId('goal-contribution-amount');
    expect(amount.className).toMatch(/text-muted-foreground/);
    // No notes → an em dash placeholder, and no notes span.
    expect(screen.queryByTestId('goal-contribution-notes')).not.toBeInTheDocument();
    expect(screen.getByTestId('goal-contribution-row')).toHaveTextContent('—');
  });

  it('truncates long notes and exposes the full text via the title attribute', () => {
    const longNotes = 'A'.repeat(120);
    render(
      <GoalContributionsTable
        goal={{
          ...baseGoal,
          contributions: [{ ...positive, notes: longNotes }],
        }}
      />,
    );
    const note = screen.getByTestId('goal-contribution-notes');
    expect(note.getAttribute('title')).toBe(longNotes);
    expect((note.textContent ?? '').length).toBeLessThanOrEqual(61);
  });
});
