'use client';

import { PiggyBank } from 'lucide-react';
import { useUiStore } from '@/src/lib/stores/ui-store';
import { cn } from '@/src/lib/utils/cn';
import { SidebarNav } from './sidebar-nav';

export function Sidebar() {
  const collapsed = useUiStore((s) => s.sidebarCollapsed);

  return (
    <aside
      data-testid="sidebar"
      data-collapsed={collapsed}
      className={cn(
        'sticky top-0 hidden h-screen shrink-0 border-r border-border bg-card transition-[width] duration-200 md:flex md:flex-col',
        collapsed ? 'w-16' : 'w-60',
      )}
    >
      <div className="flex h-14 items-center gap-2 border-b border-border px-4">
        <PiggyBank className="h-5 w-5 shrink-0 text-foreground" aria-hidden />
        {!collapsed && <span className="font-semibold tracking-tight">Money</span>}
      </div>
      <SidebarNav collapsed={collapsed} />
    </aside>
  );
}
