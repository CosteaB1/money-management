'use client';

import { PanelLeft } from 'lucide-react';
import { Button } from '@/src/components/ui/button';
import { useUiStore } from '@/src/lib/stores/ui-store';
import { SidebarDrawer } from './sidebar-drawer';
import { ThemeToggle } from './theme-toggle';

export function Header() {
  const toggleSidebar = useUiStore((s) => s.toggleSidebar);
  const openMobileNav = useUiStore((s) => s.openMobileNav);
  const mobileNavOpen = useUiStore((s) => s.mobileNavOpen);
  return (
    <header className="sticky top-0 z-30 flex h-14 items-center justify-between gap-4 border-b border-border bg-background/80 px-4 backdrop-blur md:px-6">
      <div className="flex items-center gap-2">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Toggle sidebar"
          aria-expanded={mobileNavOpen}
          data-testid="sidebar-toggle"
          onClick={() => {
            // One button, two responsibilities that never overlap on a given
            // viewport: collapse the desktop rail AND open the mobile drawer.
            // The drawer is md:hidden so opening it is a no-op on desktop, and
            // the desktop rail is display:none on mobile so the width flip is a
            // no-op there. Wiring both keeps a single affordance for both modes.
            toggleSidebar();
            openMobileNav();
          }}
        >
          <PanelLeft className="h-4 w-4" />
        </Button>
      </div>
      <div className="flex items-center gap-2">
        <ThemeToggle />
      </div>
      {/* Mobile-only nav drawer, controlled via the ui-store. Mounted here (a
          client component) rather than in the server-rendered root layout. */}
      <SidebarDrawer />
    </header>
  );
}
