import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ComingSoon } from '@/src/components/coming-soon';
import { EmptyWidget } from '@/src/components/dashboard/empty-widget';
import { PageHeader } from '@/src/components/page-header';
import { Separator } from '@/src/components/ui/separator';

describe('PageHeader', () => {
  it('renders the title only', () => {
    render(<PageHeader title="Accounts" />);
    expect(screen.getByRole('heading', { name: 'Accounts' })).toBeInTheDocument();
    expect(screen.queryByText('desc')).not.toBeInTheDocument();
  });

  it('renders description and actions when provided', () => {
    render(
      <PageHeader
        title="Budgets"
        description="Monthly limits"
        actions={<button type="button">Add</button>}
      />,
    );
    expect(screen.getByText('Monthly limits')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
  });
});

describe('ComingSoon', () => {
  it('renders the feature name', () => {
    render(<ComingSoon feature="Reports" />);
    expect(screen.getByText('Reports')).toBeInTheDocument();
    expect(screen.getByText(/coming soon/i)).toBeInTheDocument();
  });
});

describe('EmptyWidget', () => {
  it('renders the title and hint', () => {
    render(<EmptyWidget title="Budgets" hint="No budgets yet." />);
    expect(screen.getByText('Budgets')).toBeInTheDocument();
    expect(screen.getByText('No budgets yet.')).toBeInTheDocument();
  });
});

describe('Separator', () => {
  it('renders a horizontal separator by default', () => {
    const { container } = render(<Separator />);
    const el = container.querySelector('[data-orientation="horizontal"]');
    expect(el).not.toBeNull();
  });

  it('renders a vertical separator when requested', () => {
    const { container } = render(<Separator orientation="vertical" />);
    const el = container.querySelector('[data-orientation="vertical"]');
    expect(el).not.toBeNull();
  });
});
