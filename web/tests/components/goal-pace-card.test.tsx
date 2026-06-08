import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoalPaceCard } from '@/src/components/goals/detail/goal-pace-card';
import type { GoalDetailDto } from '@/src/types/api';

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
  pace: {
    avgMonthlyContribution: 2500,
    projectedCompletionDate: '2027-04-01',
    monthsToAchieveAtPace: 11,
  },
  contributions: [],
  savedHistory: [],
};

describe('GoalPaceCard', () => {
  it('renders all three cells with non-null pace values', () => {
    render(<GoalPaceCard goal={baseGoal} />);
    // Avg monthly contribution = 2500 → 2.500 / 2,500 in any locale variant.
    expect(screen.getByTestId('goal-pace-avg')).toHaveTextContent(/2[.\s]?500/);
    // Projected completion subtitle echoes the months-to-achieve count.
    expect(screen.getByTestId('goal-pace-projected')).toHaveTextContent(
      /11 months at current pace/,
    );
    // Required to hit target date — 3,920 MDL/mo.
    expect(screen.getByTestId('goal-pace-required')).toHaveTextContent(/3[.\s]?920/);
  });

  it('shows "Not enough history" when avgMonthlyContribution is null', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          pace: {
            avgMonthlyContribution: null,
            projectedCompletionDate: null,
            monthsToAchieveAtPace: null,
          },
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-avg')).toHaveTextContent(/Not enough history/);
    expect(screen.getByTestId('goal-pace-avg')).toHaveTextContent(/—/);
  });

  it('shows "Pace too slow" when projectedCompletionDate is null but the goal is not achieved', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          status: 'Behind',
          pace: {
            avgMonthlyContribution: 100,
            projectedCompletionDate: null,
            monthsToAchieveAtPace: null,
          },
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-projected')).toHaveTextContent(/Pace too slow/);
    expect(screen.getByTestId('goal-pace-projected')).toHaveTextContent(/—/);
  });

  it('shows "Already achieved" subtitle on the projected cell when status is Achieved', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          status: 'Achieved',
          pace: {
            avgMonthlyContribution: 1000,
            projectedCompletionDate: null,
            monthsToAchieveAtPace: null,
          },
          requiredMonthlyContribution: null,
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-projected')).toHaveTextContent(/Already achieved/);
  });

  it('shows "Goal already met" when required is null AND the goal is achieved', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          status: 'Achieved',
          requiredMonthlyContribution: null,
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-required')).toHaveTextContent(/Goal already met/);
    expect(screen.getByTestId('goal-pace-required')).toHaveTextContent(/—/);
  });

  it('shows "No target date" when required is null because targetDate is null', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          targetDate: null,
          requiredMonthlyContribution: null,
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-required')).toHaveTextContent(/No target date/);
  });

  it('shows "At current pace" when a projected date exists but months-to-achieve is null', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          pace: {
            avgMonthlyContribution: 1000,
            projectedCompletionDate: '2027-01-01',
            monthsToAchieveAtPace: null,
          },
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-projected')).toHaveTextContent(/At current pace/);
  });

  it('shows "No required pace" when required is null but a target date exists and the goal is not achieved', () => {
    render(
      <GoalPaceCard
        goal={{
          ...baseGoal,
          status: 'OnTrack',
          targetDate: '2026-12-31',
          requiredMonthlyContribution: null,
        }}
      />,
    );
    expect(screen.getByTestId('goal-pace-required')).toHaveTextContent(/No required pace/);
  });

  it('renders the createdOn footer line', () => {
    render(<GoalPaceCard goal={baseGoal} />);
    expect(screen.getByTestId('goal-pace-created')).toHaveTextContent(/Created/);
  });
});
