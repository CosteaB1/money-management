'use client';

import { PanelLeft } from 'lucide-react';
import { Button } from '@/src/components/ui/button';
import { useUiStore } from '@/src/lib/stores/ui-store';
import { ThemeToggle } from './theme-toggle';

export function Header() {
  const toggleSidebar = useUiStore((s) => s.toggleSidebar);
  return (
    <header className="sticky top-0 z-30 flex h-14 items-center justify-between gap-4 border-b border-border bg-background/80 px-4 backdrop-blur md:px-6">
      <div className="flex items-center gap-2">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Toggle sidebar"
          data-testid="sidebar-toggle"
          onClick={toggleSidebar}
        >
          <PanelLeft className="h-4 w-4" />
        </Button>
      </div>
      <div className="flex items-center gap-2">
        <ThemeToggle />
      </div>
    </header>
  );
}
