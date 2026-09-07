'use client';

import { AlertTriangle, MoreHorizontal } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { ArchivePoolDialog } from '@/src/components/pools/archive-pool-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/src/components/ui/dropdown-menu';
import { Label } from '@/src/components/ui/label';
import { Switch } from '@/src/components/ui/switch';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { usePools } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatFractionPercent } from '@/src/lib/utils/pool';
import type { PoolDto } from '@/src/types/api';

const COLUMN_COUNT = 8;

/**
 * The `/pools` list.
 *
 * Money-moving actions deliberately do **not** live in the row menu: every one
 * of them needs the participant roster, which only the detail endpoint
 * returns. Putting a "Record subscription" here would mean either a second
 * fetch per row or a picker that cannot name anyone. The row menu carries only
 * Archive; everything else is one click away on `/pools/{id}`.
 *
 * There is no Unarchive counterpart, and that is not an omission — the backend
 * exposes no route for it. Archiving is only permitted once no outside units
 * remain, so an archived pool's account is a wholly-owned account again and
 * re-opening it would silently re-take a share of it.
 */
export function PoolsTable() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const { data, isLoading, isError } = usePools(includeArchived);
  const [archiveTarget, setArchiveTarget] = useState<PoolDto | null>(null);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end gap-2">
        <Label htmlFor="show-archived-pools" className="text-sm text-muted-foreground">
          Show archived
        </Label>
        <Switch
          id="show-archived-pools"
          data-testid="show-archived-pools-toggle"
          checked={includeArchived}
          onCheckedChange={setIncludeArchived}
        />
      </div>

      <div className="rounded-lg border">
        <Table data-testid="pools-table">
          <TableHeader>
            <TableRow>
              <TableHead>Pool</TableHead>
              <TableHead>Account</TableHead>
              <TableHead className="text-right">Pool value</TableHead>
              <TableHead className="text-right">MDL eq.</TableHead>
              <TableHead className="text-right">Your share</TableHead>
              <TableHead className="text-right">People</TableHead>
              <TableHead className="text-right">Value per share</TableHead>
              <TableHead className="w-12 text-right">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isError ? (
              <TableRow>
                <TableCell colSpan={COLUMN_COUNT} className="text-center text-destructive">
                  Failed to load pools.
                </TableCell>
              </TableRow>
            ) : isLoading || !data ? (
              <PoolsSkeletonRows />
            ) : data.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={COLUMN_COUNT}
                  className="py-10 text-center text-muted-foreground"
                  data-testid="pools-empty"
                >
                  No pools yet — click &ldquo;Add pool&rdquo; if you hold money in an account on
                  someone else&rsquo;s behalf.
                </TableCell>
              </TableRow>
            ) : (
              data.map((pool) => (
                <PoolRow key={pool.id} pool={pool} onArchive={() => setArchiveTarget(pool)} />
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {archiveTarget && (
        <ArchivePoolDialog
          pool={archiveTarget}
          open={archiveTarget !== null}
          onOpenChange={(next) => {
            if (!next) setArchiveTarget(null);
          }}
        />
      )}
    </div>
  );
}

function PoolRow({ pool, onArchive }: { pool: PoolDto; onArchive: () => void }) {
  return (
    <TableRow data-testid="pool-row" data-archived={pool.isArchived ? 'true' : 'false'}>
      <TableCell className="font-medium">
        {/* Only the name cell is a link — keeps the row-action dropdown's
            pointer events isolated so clicking the menu never navigates.
            Archived pools stay drillable (the loans detail precedent). */}
        <span className="inline-flex flex-wrap items-center gap-1.5">
          <Link
            href={`/pools/${pool.id}`}
            className="rounded-sm underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
            data-testid="pool-name-link"
          >
            {pool.name}
          </Link>
          {pool.isArchived && (
            <Badge variant="outline" data-testid="pool-archived-badge">
              Archived
            </Badge>
          )}
        </span>
      </TableCell>
      <TableCell className="text-muted-foreground" data-testid="pool-account">
        <Link
          href={`/accounts/${pool.accountId}`}
          className="rounded-sm underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
        >
          {pool.accountName}
        </Link>
      </TableCell>
      <TableCell className="text-right tabular-nums" data-testid="pool-value">
        {formatMoney(pool.poolValue, pool.currency)}
      </TableCell>
      <TableCell
        className="text-right tabular-nums text-muted-foreground"
        data-testid="pool-value-mdl"
      >
        {pool.poolValueMdl !== null && !pool.missingFxRate ? (
          formatMoney(pool.poolValueMdl, 'MDL')
        ) : (
          // Never a zero here: an unconvertible pool renders as an explicit
          // em dash plus the amber warning the rest of the app uses, because
          // a plausible-looking 0.00 MDL is worse than no number at all.
          <span
            className="inline-flex items-center justify-end gap-1.5 text-amber-600 dark:text-amber-400"
            role="img"
            aria-label={`No FX rate available to convert ${pool.currency} to MDL`}
            title={`No FX rate available to convert ${pool.currency} to MDL. Add one in Settings → FX rates.`}
            data-testid="pool-missing-fx"
          >
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden />
            <span aria-hidden>—</span>
          </span>
        )}
      </TableCell>
      <TableCell className="text-right tabular-nums" data-testid="pool-owner-share">
        {formatFractionPercent(pool.ownerFraction)}
      </TableCell>
      <TableCell
        className="text-right tabular-nums text-muted-foreground"
        data-testid="pool-people"
      >
        {pool.participantCount}
      </TableCell>
      <TableCell className="text-right tabular-nums text-muted-foreground" data-testid="pool-nav">
        {/* NAV is the pool's price per share. It is null when no shares are
            outstanding — no shares means no price, and a placeholder 1.00
            would read as a live quote. */}
        {pool.navPerUnit !== null ? (
          formatMoney(pool.navPerUnit, pool.currency)
        ) : (
          <span title="No shares outstanding, so the pool has no price per share.">—</span>
        )}
      </TableCell>
      <TableCell className="text-right">
        {pool.isArchived ? (
          // Archiving is one-way by design — there is no unarchive route to
          // offer, so an archived row carries no menu at all.
          <span className="sr-only">Archived pools have no actions</span>
        ) : (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Actions for ${pool.name}`}
                data-testid="pool-actions"
              >
                <MoreHorizontal className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link href={`/pools/${pool.id}`} data-testid="open-pool-action">
                  Open pool
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem onClick={onArchive} data-testid="archive-pool-action">
                Archive
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </TableCell>
    </TableRow>
  );
}

const SKELETON_ROW_IDS = ['s1', 's2', 's3'] as const;
const SKELETON_CELL_IDS = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6', 'c7', 'c8'] as const;

function PoolsSkeletonRows() {
  return (
    <>
      {SKELETON_ROW_IDS.map((rowId) => (
        <TableRow key={rowId}>
          {SKELETON_CELL_IDS.map((cellId) => (
            <TableCell key={`${rowId}-${cellId}`}>
              <div className="h-4 w-full max-w-[160px] animate-pulse rounded bg-muted" />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}
