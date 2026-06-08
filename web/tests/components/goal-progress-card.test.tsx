import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoalProgressCard } from '@/src/components/goals/detail/goal-progress-card';
import type { GoalDetailDto } from '@/src/types/api';

const basePace = {
  avgMonthlyContribution: 2500,
  projectedCompletionDate: '2027-04-01',
  monthsToAchieveAtPace: 11,
};

const baseGoal: GoalDetailDto = {
  id: 'g-1',
  name: 'Emergency fund',
  targetAmount: 50000,
  targetDate: '2026-12-31',
  linkedAccountId: null,
  linkedAccountName: null,
  saved: 22550,
  remaining: 27450,
  progressPercent: 22550 / 50000,
  status: 'OnTrack',
  requiredMonthlyContribution: 3920,
  isLinkedMode: false,
  missingFxRate: false,
  createdOn: '2025-08-01',
  isArchived: false,
  pace: basePace,
  contributions: [],
  savedHistory: [],
};

describe('GoalProgressCard', () => {
  it('renders the Saved value, percent-of-target subtitle, and progress bar', () => {
    render(<GoalProgressCard goal={baseGoal} />);
    // 22,550 in the Romanian locale renders as 22.550 / 22 550.
    expect(screen.getByTestId('goal-detail-saved')).toHaveTextContent(/22[.\s]?550/);
    expect(screen.getByTestId('goal-detail-progress-subtitle')).toHaveTextContent(/45%/);
    expect(screen.getByTestId('goal-detail-progress-bar')).toBeInTheDocument();
  });

  it('renders the OnTrack status pill with an accessible label', () => {
    render(<GoalProgressCard goal={baseGoal} />);
    const pill = screen.getByTestId('goal-detail-status-pill');
    expect(pill).toHaveTextContent('On track');
    expect(pill).toHaveAttribute('aria-label', expect.stringMatching(/status: on track/i));
  });

  it('caps the progress-bar width visually at 120% on overshot goals', () => {
    render(
      <GoalProgressCard
        goal={{ ...baseGoal, saved: 150000, remaining: 0, progressPercent: 3, status: 'Achieved' }}
      />,
    );
    const bar = screen.getByTestId('goal-detail-progress-bar');
    const style = bar.getAttribute('style') ?? '';
    expect(style).toMatch(/width:\s*120%/);
  });

  it('renders the Achieved status pill when status is Achieved', () => {
    render(<GoalProgressCard goal={{ ...baseGoal, status: 'Achieved' }} />);
    expect(screen.getByTestId('goal-detail-status-pill')).toHaveTextContent('Achieved');
    // Achieved keeps the emerald progress bar shape.
    expect(screen.getByTestId('goal-detail-progress-bar').getAttribute('data-status')).toBe(
      'Achieved',
    );
  });

  it('surfaces the missing-FX warning when the flag is true', () => {
    render(<GoalProgressCard goal={{ ...baseGoal, missingFxRate: true }} />);
    expect(screen.getByTestId('goal-detail-missing-fx')).toBeInTheDocument();
  });

  it('does not render the missing-FX warning by default', () => {
    render(<GoalProgressCard goal={baseGoal} />);
    expect(screen.queryByTestId('goal-detail-missing-fx')).not.toBeInTheDocument();
  });

  it('renders an em-dash for remaining when the goal is fully met', () => {
    render(
      <GoalProgressCard
        goal={{ ...baseGoal, saved: 50000, remaining: 0, progressPercent: 1, status: 'Achieved' }}
      />,
    );
    expect(screen.getByTestId('goal-detail-remaining')).toHaveTextContent(/—/);
  });
});
