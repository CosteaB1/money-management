import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { GoalsTable } from '@/src/components/goals/goals-table';
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
    name: overrides.name ?? 'Untitled goal',
    targetAmount: overrides.targetAmount ?? 10000,
    targetDate: overrides.targetDate ?? null,
    linkedAccountId: overrides.linkedAccountId ?? null,
    linkedAccountName: overrides.linkedAccountName ?? null,
    saved: overrides.saved ?? 0,
    remaining: overrides.remaining ?? 10000,
    progressPercent: overrides.progressPercent ?? 0,
    status: overrides.status ?? 'OnTrack',
    requiredMonthlyContribution: overrides.requiredMonthlyContribution ?? null,
    isLinkedMode: overrides.isLinkedMode ?? false,
    missingFxRate: overrides.missingFxRate ?? false,
  };
}

describe('GoalsTable', () => {
  it('renders the four seeded goals with mode and status pills', async () => {
    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      const rows = screen.getAllByTestId('goal-row');
      expect(rows.length).toBe(4);
    });

    const pills = screen.getAllByTestId('goal-status-pill').map((p) => p.textContent);
    expect(pills).toContain('On track');
    expect(pills).toContain('At risk');
    expect(pills).toContain('Achieved');
    expect(pills).toContain('Behind');

    const modes = screen.getAllByTestId('goal-mode-badge').map((b) => b.textContent);
    expect(modes.filter((m) => m === 'Linked').length).toBe(2);
    expect(modes.filter((m) => m === 'Manual').length).toBe(2);
  });

  it('renders the linked-account name muted under the Linked mode badge', async () => {
    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('goal-row').length).toBeGreaterThan(0);
    });

    // Two linked-mode rows in the seed; both should expose the account name.
    const accountNames = screen
      .getAllByTestId('goal-linked-account-name')
      .map((n) => n.textContent);
    expect(accountNames).toContain('ING Savings');
    expect(accountNames).toContain('XTB');
  });

  it('only shows "Update saved" in the row menu for manual-mode goals', async () => {
    const user = userEvent.setup();
    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('goal-row').length).toBe(4);
    });

    // Open the menu on the first manual row (Vacation, AtRisk).
    const rows = screen.getAllByTestId('goal-row');
    const manualRow = rows.find((r) => r.getAttribute('data-mode') === 'manual');
    expect(manualRow).toBeDefined();
    await user.click(within(manualRow as HTMLElement).getByTestId('goal-actions'));

    await waitFor(() => {
      expect(screen.getByTestId('update-saved-action')).toBeInTheDocument();
    });

    // Close menu — press Escape so the dropdown closes cleanly.
    await user.keyboard('{Escape}');

    // Now open the menu on a linked-mode row (Emergency fund).
    const linkedRow = rows.find((r) => r.getAttribute('data-mode') === 'linked');
    expect(linkedRow).toBeDefined();
    await user.click(within(linkedRow as HTMLElement).getByTestId('goal-actions'));

    await waitFor(() => {
      expect(screen.getByTestId('edit-goal-action')).toBeInTheDocument();
    });
    // Update saved must NOT show up on linked rows.
    expect(screen.queryByTestId('update-saved-action')).not.toBeInTheDocument();
  });

  it('caps progress width visually at 120% on overshot rows', async () => {
    // Seed a single row that's 300% over target — should clamp to 120%.
    server.use(
      http.get('*/goals', () =>
        HttpResponse.json([
          makeGoal({
            id: 'overshoot',
            name: 'Overshot',
            targetAmount: 1000,
            saved: 3000,
            progressPercent: 3,
            status: 'Achieved',
          }),
        ]),
      ),
    );

    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('goal-row').length).toBe(1);
    });

    const bar = screen.getByTestId('goal-progress-bar');
    const style = bar.getAttribute('style') ?? '';
    expect(style).toMatch(/width:\s*120%/);
  });

  it('shows the missing-FX warning icon on linked rows that lack a convertible rate', async () => {
    server.use(
      http.get('*/goals', () =>
        HttpResponse.json([
          makeGoal({
            id: 'missing-fx',
            name: 'Foreign-currency linked',
            targetAmount: 5000,
            saved: 1000,
            progressPercent: 0.2,
            status: 'OnTrack',
            isLinkedMode: true,
            linkedAccountId: 'acc-1',
            linkedAccountName: 'BinanceUSD',
            missingFxRate: true,
          }),
        ]),
      ),
    );

    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getByTestId('goal-missing-fx-icon')).toBeInTheDocument();
    });
  });

  it('renders the empty hint when no goals exist', async () => {
    server.use(http.get('*/goals', () => HttpResponse.json([])));

    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getByText(/no goals yet/i)).toBeInTheDocument();
    });
  });

  it('renders the error state when the request fails', async () => {
    server.use(http.get('*/goals', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getByText(/failed to load goals/i)).toBeInTheDocument();
    });
  });

  it('renders skeleton rows while loading (no goal rows visible)', async () => {
    server.use(http.get('*/goals', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<GoalsTable />);

    expect(screen.queryAllByTestId('goal-row').length).toBe(0);
    expect(screen.getByTestId('goals-table')).toBeInTheDocument();
  });

  it('renders the name cell as a link to /goals/{id}', async () => {
    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('goal-name-link').length).toBe(4);
    });

    // Emergency fund has id g0000001-0000-0000-0000-000000000001 in the seed.
    const link = screen.getByRole('link', { name: 'Emergency fund' });
    expect(link).toHaveAttribute('href', '/goals/g0000001-0000-0000-0000-000000000001');
  });

  it('opens the row-action dropdown WITHOUT navigating', async () => {
    const user = userEvent.setup();
    renderWithClient(<GoalsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('goal-actions').length).toBe(4);
    });

    const triggers = screen.getAllByTestId('goal-actions');
    await user.click(triggers[0] as HTMLElement);

    await waitFor(() => {
      // Edit is in every row's menu — its presence confirms the dropdown
      // opened in place rather than navigating away.
      expect(screen.getByTestId('edit-goal-action')).toBeInTheDocument();
    });
  });

  it('opens then closes the Edit dialog from the row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<GoalsTable />);
    await waitFor(() => expect(screen.getAllByTestId('goal-actions').length).toBe(4));
    await user.click(screen.getAllByTestId('goal-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('edit-goal-action'));
    expect(await screen.findByTestId('edit-goal-dialog')).toBeInTheDocument();
    // Closing runs the table's `if (!next) setEditTarget(null)`.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByTestId('edit-goal-dialog')).not.toBeInTheDocument());
  });

  it('opens then closes the Update-saved dialog from a manual goal row', async () => {
    const user = userEvent.setup();
    renderWithClient(<GoalsTable />);
    // Vacation (id ...0002) is a manual goal — its menu has "Update saved".
    const vacationRow = (await screen.findByText('Vacation')).closest('tr');
    await user.click(within(vacationRow as HTMLElement).getByTestId('goal-actions'));
    await user.click(await screen.findByTestId('update-saved-action'));
    expect(await screen.findByTestId('update-saved-dialog')).toBeInTheDocument();
    // Closing the dialog runs the table's `if (!next) setSavedTarget(null)`.
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('update-saved-dialog')).not.toBeInTheDocument(),
    );
  });

  it('opens then closes the Archive dialog from the row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<GoalsTable />);
    await waitFor(() => expect(screen.getAllByTestId('goal-actions').length).toBe(4));
    await user.click(screen.getAllByTestId('goal-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('archive-goal-action'));
    expect(await screen.findByTestId('archive-goal-dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('archive-goal-dialog')).not.toBeInTheDocument(),
    );
  });
});
