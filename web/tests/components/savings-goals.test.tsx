import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { SavingsGoals } from '@/src/components/dashboard/savings-goals';
import { server } from '@/src/lib/mocks/server';
import type { GoalDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function makeGoal(overrides: Partial<GoalDto>): GoalDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    name: overrides.name ?? 'Goal',
    targetAmount: overrides.targetAmount ?? 1000,
    targetDate: overrides.targetDate ?? null,
    linkedAccountId: overrides.linkedAccountId ?? null,
    linkedAccountName: overrides.linkedAccountName ?? null,
    saved: overrides.saved ?? 0,
    remaining: overrides.remaining ?? 1000,
    progressPercent: overrides.progressPercent ?? 0,
    status: overrides.status ?? 'OnTrack',
    requiredMonthlyContribution: overrides.requiredMonthlyContribution ?? null,
    isLinkedMode: overrides.isLinkedMode ?? false,
    missingFxRate: overrides.missingFxRate ?? false,
  };
}

describe('SavingsGoals dashboard widget', () => {
  it('renders the empty hint with a link to /goals when no goals exist', async () => {
    server.use(http.get('*/goals', () => HttpResponse.json([])));

    renderWithClient(<SavingsGoals />);

    await waitFor(() => {
      expect(screen.getByTestId('savings-goals-empty')).toBeInTheDocument();
    });

    const link = screen.getByRole('link', { name: /add one in \/goals/i });
    expect(link).toHaveAttribute('href', '/goals');
  });

  it('renders an error message when the request fails', async () => {
    server.use(http.get('*/goals', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<SavingsGoals />);

    await waitFor(() => {
      expect(screen.getByTestId('savings-goals-error')).toBeInTheDocument();
    });
  });

  it('shows the top 3 goals sorted by progress percent (highest first)', async () => {
    const five: GoalDto[] = [
      makeGoal({ id: 'a', name: 'Low', progressPercent: 0.1, status: 'OnTrack' }),
      makeGoal({ id: 'b', name: 'Mid', progressPercent: 0.5, status: 'AtRisk' }),
      makeGoal({ id: 'c', name: 'High', progressPercent: 0.85, status: 'OnTrack' }),
      makeGoal({ id: 'd', name: 'Done', progressPercent: 1.04, status: 'Achieved' }),
      makeGoal({ id: 'e', name: 'Tiny', progressPercent: 0.02, status: 'Behind' }),
    ];

    server.use(http.get('*/goals', () => HttpResponse.json(five)));

    renderWithClient(<SavingsGoals />);

    await waitFor(() => {
      const rows = screen.getAllByTestId('savings-goals-row');
      expect(rows.length).toBe(3);
    });

    const labels = screen.getAllByTestId('savings-goals-row').map((r) => r.textContent ?? '');

    // Highest progress first, "Tiny" and "Low" dropped.
    expect(labels[0]).toContain('Done');
    expect(labels[1]).toContain('High');
    expect(labels[2]).toContain('Mid');
    expect(labels.join(' ')).not.toContain('Tiny');
    expect(labels.join(' ')).not.toContain('Low');
  });

  it('shows the "View all" link when goals exist', async () => {
    renderWithClient(<SavingsGoals />);

    await waitFor(() => {
      const link = screen.getByTestId('savings-goals-view-all');
      expect(link).toHaveAttribute('href', '/goals');
    });
  });

  it('renders skeletons (and no rows) while loading', async () => {
    server.use(http.get('*/goals', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<SavingsGoals />);

    expect(screen.queryAllByTestId('savings-goals-row').length).toBe(0);
    expect(screen.getByTestId('savings-goals-card')).toBeInTheDocument();
  });
});
