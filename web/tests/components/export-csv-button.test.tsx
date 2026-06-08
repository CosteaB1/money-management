import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { buildExportCsvUrl, ExportCsvButton } from '@/src/components/reports/export-csv-button';

describe('buildExportCsvUrl', () => {
  it('returns the bare endpoint when no filters are set', () => {
    expect(buildExportCsvUrl({}, 'http://api.local')).toBe(
      'http://api.local/reports/transactions.csv',
    );
  });

  it('serializes every supported filter to the query string', () => {
    const url = buildExportCsvUrl(
      {
        accountId: 'acc-1',
        from: '2026-05-01',
        to: '2026-05-23',
        direction: 'Expense',
        categoryIds: ['cat-1', 'cat-2'],
        isTransfer: false,
        isAdjustment: true,
      },
      'http://api.local',
    );
    // The URL constructor's iteration order matches the order we
    // appended in — assert against the parsed query so the order is
    // irrelevant.
    const parsed = new URL(url);
    expect(parsed.pathname).toBe('/reports/transactions.csv');
    expect(parsed.searchParams.get('accountId')).toBe('acc-1');
    expect(parsed.searchParams.get('from')).toBe('2026-05-01');
    expect(parsed.searchParams.get('to')).toBe('2026-05-23');
    expect(parsed.searchParams.get('direction')).toBe('Expense');
    expect(parsed.searchParams.getAll('categoryId')).toEqual(['cat-1', 'cat-2']);
    expect(parsed.searchParams.get('isTransfer')).toBe('false');
    expect(parsed.searchParams.get('isAdjustment')).toBe('true');
  });
});

describe('ExportCsvButton', () => {
  it('renders the button with a visible label', () => {
    render(<ExportCsvButton filters={{}} apiBaseUrl="http://api.local" />);
    expect(screen.getByTestId('export-csv-button')).toBeInTheDocument();
    expect(screen.getByTestId('export-csv-button').textContent).toMatch(/export csv/i);
  });

  it('creates a download anchor with the current filter set on click', async () => {
    const user = userEvent.setup();
    // Stub `click` on the anchor so the download is not actually
    // triggered — we only need to capture the `href`.
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    render(
      <ExportCsvButton
        filters={{
          accountId: 'acc-9',
          from: '2026-01-01',
          to: '2026-02-01',
          direction: 'Income',
          categoryIds: ['cat-x'],
          isTransfer: true,
        }}
        apiBaseUrl="http://api.local"
      />,
    );

    await user.click(screen.getByTestId('export-csv-button'));

    expect(clickSpy).toHaveBeenCalledTimes(1);
    const anchor = clickSpy.mock.instances[0] as unknown as HTMLAnchorElement;
    const parsed = new URL(anchor.href);
    expect(parsed.pathname).toBe('/reports/transactions.csv');
    expect(parsed.searchParams.get('accountId')).toBe('acc-9');
    expect(parsed.searchParams.get('from')).toBe('2026-01-01');
    expect(parsed.searchParams.get('to')).toBe('2026-02-01');
    expect(parsed.searchParams.get('direction')).toBe('Income');
    expect(parsed.searchParams.get('isTransfer')).toBe('true');
    expect(parsed.searchParams.getAll('categoryId')).toEqual(['cat-x']);

    clickSpy.mockRestore();
  });
});
