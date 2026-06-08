import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { GoalHistoryChart } from '@/src/components/goals/detail/goal-history-chart';
import type { GoalDetailDto } from '@/src/types/api';

// Recharts can't measure its ResponsiveContainer in jsdom — stub it with a
// fixed-size wrapper so the chart paints (the sr-only fallback is what
// the test asserts against, but the inner LineChart still needs to render
// without throwing).
vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts');
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactNode }) => (
      <div style={{ width: 800, height: 300 }}>{children}</div>
    ),
  };
});

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
  savedHistory: [
    { asOf: '2026-01-31', saved: 18000 },
    { asOf: '2026-02-28', saved: 19500 },
    { asOf: '2026-03-31', saved: 20850 },
    { asOf: '2026-04-30', saved: 22050 },
    { asOf: '2026-05-23', saved: 22550 },
  ],
};

describe('GoalHistoryChart', () => {
  it('enumerates every savedHistory point in the sr-only fallback', () => {
    render(<GoalHistoryChart goal={baseGoal} />);

    const items = screen.getAllByTestId('goal-history-point');
    expect(items.length).toBe(baseGoal.savedHistory.length);

    // Spot-check: first point's MDL value should appear.
    expect(items[0]?.textContent ?? '').toMatch(/18[.\s]?000/);
    // Last point.
    expect(items[items.length - 1]?.textContent ?? '').toMatch(/22[.\s]?550/);
  });

  it('renders the target-line sentinel with the target amount', () => {
    render(<GoalHistoryChart goal={baseGoal} />);
    const target = screen.getByTestId('goal-history-target-line');
    expect(target.textContent ?? '').toMatch(/50[.\s]?000/);
  });

  it('renders the target-dot sentinel only when targetDate is INSIDE the visible range', () => {
    // targetDate is 2026-12-31 — well past the last savedHistory point —
    // so the dot should NOT be rendered (would be off-chart on the right).
    render(<GoalHistoryChart goal={baseGoal} />);
    expect(screen.queryByTestId('goal-history-target-dot')).not.toBeInTheDocument();

    // With a target date inside the savedHistory window, the dot should render.
    render(
      <GoalHistoryChart
        goal={{
          ...baseGoal,
          targetDate: '2026-04-30',
        }}
      />,
    );
    expect(screen.getByTestId('goal-history-target-dot')).toBeInTheDocument();
  });

  it('renders the empty-state copy when savedHistory is empty', () => {
    render(<GoalHistoryChart goal={{ ...baseGoal, savedHistory: [] }} />);
    expect(screen.getByTestId('goal-history-empty')).toHaveTextContent(/no history yet/i);
    // The data-points list should not exist.
    expect(screen.queryByTestId('goal-history-points')).not.toBeInTheDocument();
  });
});
