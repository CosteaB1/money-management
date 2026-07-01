'use client';

import { PiggyBank } from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useEffect } from 'react';
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/src/components/ui/sheet';
import { useUiStore } from '@/src/lib/stores/ui-store';
import { SidebarNav } from './sidebar-nav';

/**
 * Mobile navigation drawer. Below the `md` breakpoint the desktop <aside> is
 * `display:none`, leaving no nav at all — this Sheet fills that gap. It is
 * itself `md:hidden`, so opening it on desktop is a no-op (the shared header
 * toggle drives both this and the desktop collapse).
 *
 * Closes on: Esc / overlay click (Radix Dialog), a nav-item tap (onNavigate),
 * and route change (the pathname effect below — belt-and-suspenders alongside
 * onNavigate, so a programmatic navigation still dismisses it).
 */
export function SidebarDrawer() {
  const open = useUiStore((s) => s.mobileNavOpen);
  const setOpen = useUiStore((s) => s.setMobileNavOpen);
  const close = useUiStore((s) => s.closeMobileNav);
  const pathname = usePathname();

  // Dismiss on navigation. `onNavigate` already closes on a click, but this also
  // covers programmatic route changes (e.g. a redirect) while the drawer is open.
  // biome-ignore lint/correctness/useExhaustiveDependencies: close is a stable Zustand action; we intentionally re-run only when the route changes.
  useEffect(() => {
    close();
  }, [pathname]);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      {/* No description needed for a nav drawer — opt out of Radix's
          aria-describedby requirement explicitly to avoid its dev warning. */}
      <SheetContent
        data-testid="mobile-nav-drawer"
        aria-describedby={undefined}
        className="md:hidden"
      >
        {/* sr-only accessible name for the dialog — Radix Dialog requires a
            Title or it warns. Kept visually hidden so the drawer's chrome is the
            decorative wordmark below, while screen readers announce the purpose. */}
        <SheetTitle className="sr-only">Navigation menu</SheetTitle>
        <SheetHeader className="flex h-14 flex-row items-center gap-2 border-b border-border px-4">
          <PiggyBank className="h-5 w-5 shrink-0 text-foreground" aria-hidden />
          <span className="font-semibold tracking-tight">Money</span>
        </SheetHeader>
        <SidebarNav onNavigate={close} />
      </SheetContent>
    </Sheet>
  );
}
