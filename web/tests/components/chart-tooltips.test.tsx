import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BalanceTrendTooltip } from '@/src/components/accounts/detail/balance-trend-card';
import {
  formatMonthLabel,
  NetWorthTrendTooltip,
} from '@/src/components/dashboard/net-worth-trend-chart';
import { GoalHistoryTooltip } from '@/src/components/goals/detail/goal-history-chart';
import { BalanceOverTimeTooltip } from '@/src/components/reports/balance-over-time-section';
import { MonthlySummaryTooltip } from '@/src/components/reports/monthly-summary-section';
import { YoyTooltip } from '@/src/components/reports/year-over-year-section';

// biome-ignore lint/suspicious/noExplicitAny: Recharts tooltip props are loosely typed for tests.
const point = (payload: any) => ({ active: true, payload: [{ payload }] }) as any;

describe('chart tooltips', () => {
  it('NetWorthTrendTooltip renders nothing when inactive or empty', () => {
    const { container } = render(<NetWorthTrendTooltip active={false} payload={[]} />);
    expect(container).toBeEmptyDOMElement();
    const { container: c2 } = render(<NetWorthTrendTooltip active payload={[]} />);
    expect(c2).toBeEmptyDOMElement();
  });

  it('NetWorthTrendTooltip renders the month + amount, plus missing-FX note', () => {
    render(
      <NetWorthTrendTooltip
        {...point({ month: '2026-05', netWorthMdl: 48100, missingFxRate: true })}
      />,
    );
    expect(screen.getByRole('tooltip')).toBeInTheDocument();
    expect(screen.getByText('FX rates missing')).toBeInTheDocument();
  });

  it('NetWorthTrendTooltip returns null when the payload point is missing', () => {
    const { container } = render(<NetWorthTrendTooltip {...point(undefined)} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('formatMonthLabel formats and falls back for bad input', () => {
    expect(formatMonthLabel('2026-05')).toMatch(/\w+/);
    expect(formatMonthLabel('garbage')).toBe('garbage');
  });

  it('BalanceTrendTooltip shows native + MDL line and missing-FX', () => {
    render(
      <BalanceTrendTooltip
        {...point({ asOf: '2026-05-01', balance: 1500, balanceMdl: 26250, missingFxRate: true })}
        currency="USD"
        showMdl
      />,
    );
    expect(screen.getByText(/≈/)).toBeInTheDocument();
    expect(screen.getByText('FX rate missing')).toBeInTheDocument();
  });

  it('BalanceTrendTooltip is null when inactive', () => {
    const { container } = render(
      <BalanceTrendTooltip active={false} payload={[]} currency="USD" showMdl />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('GoalHistoryTooltip renders the date + saved amount', () => {
    render(<GoalHistoryTooltip {...point({ asOf: '2026-05-01', saved: 22550 })} />);
    expect(screen.getByRole('tooltip')).toBeInTheDocument();
  });

  it('GoalHistoryTooltip is null when inactive', () => {
    const { container } = render(<GoalHistoryTooltip active={false} payload={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('MonthlySummaryTooltip renders income/expense/net + missing-FX', () => {
    render(
      <MonthlySummaryTooltip
        {...point({ income: 12000, expense: 8500, net: 3500, missingFxRate: true })}
        label="2026-05"
      />,
    );
    expect(screen.getByText(/Income:/)).toBeInTheDocument();
    expect(screen.getByText('FX rates missing')).toBeInTheDocument();
  });

  it('MonthlySummaryTooltip is null when inactive', () => {
    const { container } = render(<MonthlySummaryTooltip active={false} payload={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('BalanceOverTimeTooltip renders + missing-FX', () => {
    render(
      <BalanceOverTimeTooltip
        {...point({ asOf: '2026-05-01', balance: 1500, balanceMdl: 26250, missingFxRate: true })}
        currency="USD"
        showMdl
      />,
    );
    expect(screen.getByText(/≈/)).toBeInTheDocument();
    expect(screen.getByText('FX rate missing')).toBeInTheDocument();
  });

  it('BalanceOverTimeTooltip is null when inactive', () => {
    const { container } = render(
      <BalanceOverTimeTooltip active={false} payload={[]} currency="MDL" showMdl={false} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('YoyTooltip renders prior + current net lines', () => {
    render(<YoyTooltip {...point({ priorNet: 1000, currentNet: 1500 })} label="May" />);
    expect(screen.getByText(/Prior net:/)).toBeInTheDocument();
    expect(screen.getByText(/Current net:/)).toBeInTheDocument();
  });

  it('YoyTooltip is null when inactive or missing the point', () => {
    const { container } = render(<YoyTooltip active={false} payload={[]} />);
    expect(container).toBeEmptyDOMElement();
    const { container: c2 } = render(<YoyTooltip {...point(undefined)} />);
    expect(c2).toBeEmptyDOMElement();
  });
});
