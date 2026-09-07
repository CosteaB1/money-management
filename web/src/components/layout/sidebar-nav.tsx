'use client';

import {
  ArrowLeftRight,
  Coins,
  Database,
  HandCoins,
  LayoutDashboard,
  type LucideIcon,
  PieChart,
  Receipt,
  Settings,
  Target,
  TrendingUp,
  Wallet,
} from 'lucide-react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { cn } from '@/src/lib/utils/cn';

interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
}

// Single source of truth for the app's primary navigation. Rendered by both the
// desktop <aside> (Sidebar) and the mobile drawer (SidebarDrawer) so the two
// never drift. Keep the `nav-*` testids stable — QA + unit tests key off them.
export const NAV_ITEMS: readonly NavItem[] = [
  { href: '/', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/accounts', label: 'Accounts', icon: Wallet },
  { href: '/transactions', label: 'Transactions', icon: ArrowLeftRight },
  { href: '/budgets', label: 'Budgets', icon: Receipt },
  { href: '/goals', label: 'Goals', icon: Target },
  { href: '/loans', label: 'Loans', icon: HandCoins },
  { href: '/pools', label: 'Pools', icon: PieChart },
  { href: '/reports', label: 'Reports', icon: TrendingUp },
  { href: '/settings/fx-rates', label: 'FX rates', icon: Coins },
  { href: '/settings/data', label: 'Data', icon: Database },
  { href: '/settings', label: 'Settings', icon: Settings },
] as const;

interface SidebarNavProps {
  /**
   * When true, hides the per-item label (desktop collapsed rail). The mobile
   * drawer always renders labels, so it leaves this unset.
   */
  collapsed?: boolean;
  /**
   * Fired after a nav link is clicked. The mobile drawer passes a close handler
   * here so tapping an item both navigates and dismisses the drawer; the desktop
   * sidebar omits it.
   */
  onNavigate?: () => void;
}

export function SidebarNav({ collapsed = false, onNavigate }: SidebarNavProps) {
  const pathname = usePathname();
  return (
    <nav className="flex-1 space-y-1 p-2">
      {NAV_ITEMS.map((item) => {
        const isActive =
          item.href === '/'
            ? pathname === '/'
            : pathname === item.href || pathname?.startsWith(`${item.href}/`);
        const Icon = item.icon;
        return (
          <Link
            key={item.href}
            href={item.href}
            data-testid={`nav-${item.label.toLowerCase().replace(/\s+/g, '-')}`}
            // Only attach the handler when provided — `exactOptionalPropertyTypes`
            // rejects an explicit `onClick={undefined}` on next/link's Link.
            {...(onNavigate ? { onClick: onNavigate } : {})}
            className={cn(
              'flex h-10 items-center gap-3 rounded-md px-3 text-sm transition-colors',
              'min-w-10',
              isActive
                ? 'bg-accent text-accent-foreground'
                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
            )}
            aria-current={isActive ? 'page' : undefined}
          >
            <Icon className="h-4 w-4 shrink-0" aria-hidden />
            {!collapsed && <span>{item.label}</span>}
          </Link>
        );
      })}
    </nav>
  );
}
