import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { PerformanceCard } from '@/src/components/accounts/detail/performance-card';
import type { AccountDetailDto } from '@/src/types/api';

const baseAccount: AccountDetailDto = {
  id: 'acc-1',
  name: 'XTB',
  type: 'Brokerage',
  currency: 'USD',
  openingDate: '2024-09-10',
  isArchived: false,
  notes: null,
  balance: 1500,
  balanceMdl: 26250,
  initialCapital: 1000,
  allTime: {
    contributionsMdl: 12000,
    withdrawalsMdl: 2000,
    netPnLMdl: 4375,
    contributionCount: 3,
    withdrawalCount: 1,
    adjustmentCount: 2,
    missingFxRate: false,
  },
  yearToDate: {
    contributionsMdl: 5000,
    withdrawalsMdl: 500,
    netPnLMdl: -120,
    contributionCount: 1,
    withdrawalCount: 1,
    adjustmentCount: 1,
    missingFxRate: false,
  },
  firstActivityDate: '2024-09-10',
  lastActivityDate: '2025-05-15',
  realActivityCount: 2,
};

describe('PerformanceCard', () => {
  it('renders all three KPI cells with YTD totals by default', () => {
    render(<PerformanceCard account={baseAccount} />);

    expect(screen.getByTestId('perf-contributions')).toBeInTheDocument();
    expect(screen.getByTestId('perf-withdrawals')).toBeInTheDocument();
    expect(screen.getByTestId('perf-pnl')).toBeInTheDocument();

    // YTD contributions = 5000 → "5.000" in the Romanian locale.
    expect(screen.getByTestId('perf-contributions')).toHaveTextContent(/5[.\s]?000/);
    // YTD contributionCount = 1 → singular subtitle.
    expect(screen.getByTestId('perf-contributions')).toHaveTextContent(/1 deposit/);

    // Card carries the window in a data attribute for assertion ergonomics.
    expect(screen.getByTestId('performance-card')).toHaveAttribute('data-window', 'YTD');
  });

  it('toggling to All-time swaps the totals source', async () => {
    const user = userEvent.setup();
    render(<PerformanceCard account={baseAccount} />);

    // Initially YTD: 5.000 in contributions.
    expect(screen.getByTestId('perf-contributions')).toHaveTextContent(/5[.\s]?000/);

    await user.click(screen.getByTestId('perf-window-all'));

    // All-time contributions = 12.000.
    expect(screen.getByTestId('perf-contributions')).toHaveTextContent(/12[.\s]?000/);
    expect(screen.getByTestId('perf-contributions')).toHaveTextContent(/3 deposits/);
    expect(screen.getByTestId('performance-card')).toHaveAttribute('data-window', 'AllTime');
  });

  it('color-codes the Net P&L cell green when positive', () => {
    render(<PerformanceCard account={baseAccount} />);
    // Default window is YTD with netPnLMdl = -120 → rose.
    const pnl = screen.getByTestId('perf-pnl');
    expect(pnl.querySelector('.text-rose-500')).not.toBeNull();
  });

  it('color-codes the Net P&L cell using the All-time totals on toggle', async () => {
    const user = userEvent.setup();
    render(<PerformanceCard account={baseAccount} />);
    await user.click(screen.getByTestId('perf-window-all'));
    // All-time netPnLMdl = 4375 → emerald.
    const pnl = screen.getByTestId('perf-pnl');
    expect(pnl.querySelector('.text-emerald-500')).not.toBeNull();
  });

  it('color-codes Net P&L muted when zero', () => {
    const zeroPnl: AccountDetailDto = {
      ...baseAccount,
      yearToDate: { ...baseAccount.yearToDate, netPnLMdl: 0 },
    };
    render(<PerformanceCard account={zeroPnl} />);
    const pnl = screen.getByTestId('perf-pnl');
    expect(pnl.querySelector('.text-muted-foreground')).not.toBeNull();
  });

  it('surfaces the missing-FX warning when totals.missingFxRate is true', () => {
    const missing: AccountDetailDto = {
      ...baseAccount,
      yearToDate: { ...baseAccount.yearToDate, missingFxRate: true },
    };
    render(<PerformanceCard account={missing} />);
    expect(screen.getByTestId('perf-missing-fx')).toBeInTheDocument();
  });

  it('shows the native value alongside the MDL current value for non-MDL accounts', () => {
    render(<PerformanceCard account={baseAccount} />);
    expect(screen.getByTestId('perf-current-mdl')).toHaveTextContent(/26[.\s]?250/);
    const native = screen.getByTestId('perf-current-native');
    expect(native).toHaveTextContent(/USD/);
    // formatMoney already emits the ISO code; the parenthetical must NOT
    // duplicate it with a trailing " in USD".
    expect(native).not.toHaveTextContent(/in USD/);
  });

  it('omits the native side note for MDL accounts', () => {
    const mdlAccount: AccountDetailDto = {
      ...baseAccount,
      currency: 'MDL',
      balance: 1000,
      balanceMdl: 1000,
    };
    render(<PerformanceCard account={mdlAccount} />);
    expect(screen.queryByTestId('perf-current-native')).not.toBeInTheDocument();
  });

  it('renders an em dash for the MDL current value when the rate is missing', () => {
    render(<PerformanceCard account={{ ...baseAccount, balanceMdl: null }} />);
    const current = screen.getByTestId('perf-current-mdl');
    expect(current).toHaveTextContent('—');
    expect(current.querySelector('[title]')?.getAttribute('title')).toMatch(/No FX rate available/);
  });
});
