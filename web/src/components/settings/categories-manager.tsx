'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Archive, ChevronRight, Pencil, Plus, X } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import {
  CategoryFormFields,
  type CategoryFormValues,
  CreateCategoryDialog,
  categoryFormSchema,
} from '@/src/components/categories/create-category-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { Card, CardDescription, CardHeader, CardTitle } from '@/src/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/src/components/ui/dialog';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import { useArchiveCategory, useCategories, useUpdateCategory } from '@/src/lib/api/categories';
import {
  useCategoryPatterns,
  useCreateCategoryPattern,
  useDeleteCategoryPattern,
} from '@/src/lib/api/category-patterns';
import type { CategoryDto, CategoryFlow, CategoryPatternDto } from '@/src/types/api';

function FlowBadge({ flow }: { flow: CategoryFlow }) {
  const variant = flow === 'Income' ? 'success' : flow === 'Both' ? 'secondary' : 'outline';
  return (
    <Badge variant={variant} data-testid="category-flow-badge" data-flow={flow}>
      {flow}
    </Badge>
  );
}

/**
 * Settings-page "Add category" entry point. Keeps the `add-category-button`
 * trigger that the page header and tests rely on, but delegates the actual form
 * + create flow to the shared, fully-controlled `CreateCategoryDialog` (also
 * reused by the import preview's inline-create flow). We own only the open
 * state here; the dialog handles validation, the mutation, and the toast.
 */
function AddCategoryDialog() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button data-testid="add-category-button" onClick={() => setOpen(true)}>
        <Plus className="h-4 w-4" />
        Add category
      </Button>
      <CreateCategoryDialog open={open} onOpenChange={setOpen} />
    </>
  );
}

/**
 * Edit dialog — mirrors the Add form but PUTs to `/categories/{id}`. The form
 * is prefilled from the row's current name / flow / colour and re-seeded
 * (via `reset`) whenever a different category opens the dialog, so reopening
 * never shows stale values. Empty colour is sent as omitted (clears it).
 */
function EditCategoryDialog({
  category,
  open,
  onOpenChange,
}: {
  category: CategoryDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { mutateAsync, isPending } = useUpdateCategory(category.id);

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<CategoryFormValues>({
    resolver: zodResolver(categoryFormSchema),
    defaultValues: {
      name: category.name,
      flow: category.flow,
      color: category.color ?? '',
    },
  });

  const flow = watch('flow');

  const onSubmit = handleSubmit(async (values) => {
    const color = values.color.trim();
    try {
      await mutateAsync({
        name: values.name.trim(),
        flow: values.flow,
        ...(color === '' ? {} : { color }),
      });
      toast.success(`Updated "${values.name.trim()}"`);
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to update category');
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="edit-category-dialog">
        <DialogHeader>
          <DialogTitle>Edit category</DialogTitle>
          <DialogDescription>
            Rename this category, change its flow, or pick a new colour. Existing transactions keep
            their link — only the label, flow, and colour change.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="space-y-4">
          <CategoryFormFields
            register={register}
            errors={errors}
            flow={flow}
            onFlowChange={(v) => setValue('flow', v, { shouldValidate: true })}
          />

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="category-edit-submit-button">
              {isPending ? 'Saving...' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function ArchiveCategoryDialog({
  category,
  open,
  onOpenChange,
}: {
  category: CategoryDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const archive = useArchiveCategory();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async () => {
    setIsSubmitting(true);
    try {
      await archive.mutateAsync(category.id);
      toast.success(`Archived "${category.name}"`);
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive category');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="archive-category-dialog">
        <DialogHeader>
          <DialogTitle>Archive category?</DialogTitle>
          <DialogDescription>
            <strong>{category.name}</strong> will be hidden from category pickers but stays attached
            to historical transactions. You can keep using its existing data.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={isSubmitting}
            onClick={handleConfirm}
            data-testid="archive-category-confirm-button"
          >
            {isSubmitting ? 'Archiving...' : 'Archive'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * One keyword rule rendered as a removable chip. Deleting is low-stakes —
 * it only affects FUTURE imports — so we skip the confirm modal and delete
 * immediately with a success toast. The source distinguishes visually:
 * Seeded chips use `outline`, Learned chips use `secondary`.
 */
function KeywordChip({ pattern }: { pattern: CategoryPatternDto }) {
  const remove = useDeleteCategoryPattern();

  const handleDelete = async () => {
    try {
      await remove.mutateAsync(pattern.id);
      toast.success(`Removed keyword "${pattern.keyword}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to remove keyword');
    }
  };

  return (
    <Badge
      variant={pattern.source === 'Learned' ? 'secondary' : 'outline'}
      className="gap-1 pr-1"
      data-testid="keyword-chip"
      data-source={pattern.source}
    >
      <span className="tabular-nums">{pattern.keyword}</span>
      <button
        type="button"
        className="inline-flex h-4 w-4 shrink-0 items-center justify-center rounded-sm text-muted-foreground transition-colors hover:bg-foreground/10 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        aria-label={`Remove keyword ${pattern.keyword}`}
        data-testid="keyword-chip-delete"
        disabled={remove.isPending}
        onClick={handleDelete}
      >
        <X className="h-3 w-3" aria-hidden />
      </button>
    </Badge>
  );
}

/**
 * Inline add-keyword control for a single category. POSTs the keyword against
 * THIS category's id (no picker needed). Surfaces the backend 409 "keyword
 * exists" message verbatim via toast and clears the input only on success.
 */
function AddKeywordInline({ categoryId }: { categoryId: string }) {
  const create = useCreateCategoryPattern();
  const [keyword, setKeyword] = useState('');

  const submit = async () => {
    const trimmed = keyword.trim();
    if (trimmed.length === 0) return;
    try {
      await create.mutateAsync({ keyword: trimmed, categoryId });
      toast.success('Keyword added');
      setKeyword('');
    } catch (err) {
      // Surface the backend's ProblemDetails `detail` verbatim — the 409
      // "keyword exists" message is the one the user needs to read.
      toast.error(err instanceof Error ? err.message : 'Failed to add keyword');
    }
  };

  const inputId = `add-keyword-input-${categoryId}`;

  return (
    <form
      className="flex items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <Label htmlFor={inputId} className="sr-only">
        Add keyword to this category
      </Label>
      <Input
        id={inputId}
        data-testid={`add-keyword-input-${categoryId}`}
        value={keyword}
        onChange={(e) => setKeyword(e.target.value)}
        placeholder="Add keyword…"
        className="h-8 w-40 text-xs"
      />
      <Button
        type="submit"
        size="sm"
        variant="outline"
        className="h-8"
        data-testid={`add-keyword-submit-${categoryId}`}
        disabled={create.isPending || keyword.trim().length === 0}
      >
        <Plus className="h-3.5 w-3.5" />
        Add
      </Button>
    </form>
  );
}

function CategoryRow({
  category,
  patterns,
  onArchive,
  onEdit,
}: {
  category: CategoryDto;
  patterns: CategoryPatternDto[];
  onArchive: (category: CategoryDto) => void;
  onEdit: (category: CategoryDto) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const count = patterns.length;

  return (
    <div data-testid="category-row" className="border-b last:border-b-0">
      <div className="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          aria-expanded={expanded}
          aria-label={`Expand ${category.name} keywords`}
          data-testid={`category-expand-${category.id}`}
          onClick={() => setExpanded((v) => !v)}
        >
          <ChevronRight
            className={`h-4 w-4 transition-transform ${expanded ? 'rotate-90' : ''}`}
            aria-hidden
          />
        </button>

        <span
          aria-hidden
          className="inline-block h-3 w-3 shrink-0 rounded-full border"
          style={{ backgroundColor: category.color ?? 'transparent' }}
        />

        <span className="min-w-0 flex-1 truncate font-medium">{category.name}</span>

        <FlowBadge flow={category.flow} />

        {category.isArchived && (
          <Badge variant="outline" className="text-muted-foreground">
            Archived
          </Badge>
        )}

        <span className="hidden text-sm text-muted-foreground sm:inline">
          · {count} {count === 1 ? 'keyword' : 'keywords'}
        </span>

        <Button
          variant="ghost"
          size="icon"
          aria-label={`Edit ${category.name}`}
          data-testid="edit-category"
          onClick={() => onEdit(category)}
        >
          <Pencil className="h-4 w-4" />
        </Button>

        {!category.isArchived && (
          <Button
            variant="ghost"
            size="icon"
            aria-label={`Archive ${category.name}`}
            data-testid="archive-category"
            onClick={() => onArchive(category)}
          >
            <Archive className="h-4 w-4" />
          </Button>
        )}
      </div>

      {expanded && (
        <div
          data-testid={`category-keywords-${category.id}`}
          className="flex flex-wrap items-center gap-2 px-4 pb-4 pl-12"
        >
          {patterns.map((pattern) => (
            <KeywordChip key={pattern.id} pattern={pattern} />
          ))}
          {count === 0 && <span className="text-sm text-muted-foreground">No keywords yet</span>}
          <AddKeywordInline categoryId={category.id} />
        </div>
      )}
    </div>
  );
}

/**
 * Merged Categories + auto-categorization keyword manager. Each category is an
 * expandable row; expanding reveals that category's keyword rules as removable
 * chips plus an inline add-keyword control. The two backend resources
 * (categories + patterns) are fetched separately and joined client-side by
 * `categoryId`.
 */
export function CategoriesManager() {
  // Show archived too so the user can see the full picture; the row badge
  // distinguishes them and archived rows hide the Archive action.
  const { data, isLoading, isError } = useCategories({ includeArchived: true });
  const { data: patterns } = useCategoryPatterns();
  const [archiveTarget, setArchiveTarget] = useState<CategoryDto | null>(null);
  const [editTarget, setEditTarget] = useState<CategoryDto | null>(null);

  // Group patterns by category id once per patterns change so each row can
  // pull its own slice without re-filtering the full list.
  const patternsByCategory = useMemo(() => {
    const map = new Map<string, CategoryPatternDto[]>();
    for (const pattern of patterns ?? []) {
      const bucket = map.get(pattern.categoryId);
      if (bucket) {
        bucket.push(pattern);
      } else {
        map.set(pattern.categoryId, [pattern]);
      }
    }
    return map;
  }, [patterns]);

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-3 space-y-0">
        <div className="space-y-1.5">
          <CardTitle>Categories</CardTitle>
          <CardDescription>
            The buckets your transactions and budgets roll up into. Expand a category to manage the
            keyword rules that auto-categorize imported statements.
          </CardDescription>
        </div>
        <AddCategoryDialog />
      </CardHeader>
      <div className="border-t" data-testid="categories-table">
        {isError ? (
          <p className="px-4 py-6 text-center text-destructive">Failed to load categories.</p>
        ) : isLoading || !data ? (
          <p className="px-4 py-6 text-center text-muted-foreground">Loading...</p>
        ) : data.length === 0 ? (
          <p className="px-4 py-6 text-center text-muted-foreground">
            No categories yet. Click &ldquo;Add category&rdquo; to start.
          </p>
        ) : (
          data.map((category) => (
            <CategoryRow
              key={category.id}
              category={category}
              patterns={patternsByCategory.get(category.id) ?? []}
              onArchive={setArchiveTarget}
              onEdit={setEditTarget}
            />
          ))
        )}
      </div>

      {archiveTarget && (
        <ArchiveCategoryDialog
          category={archiveTarget}
          open={archiveTarget !== null}
          onOpenChange={(next) => {
            if (!next) setArchiveTarget(null);
          }}
        />
      )}

      {editTarget && (
        // Keyed on the target id so opening a different row remounts the form
        // with that category's defaults — no stale react-hook-form state.
        <EditCategoryDialog
          key={editTarget.id}
          category={editTarget}
          open={editTarget !== null}
          onOpenChange={(next) => {
            if (!next) setEditTarget(null);
          }}
        />
      )}
    </Card>
  );
}
