'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/src/components/ui/button';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useCreateCategory } from '@/src/lib/api/categories';
import type { CategoryFlow } from '@/src/types/api';

const FLOW_OPTIONS: { value: CategoryFlow; label: string }[] = [
  { value: 'Expense', label: 'Expense' },
  { value: 'Income', label: 'Income' },
  { value: 'Both', label: 'Both' },
];

// Shared create/edit form schema. Lives here so both the settings manager
// (create + edit) and any programmatic opener (the import preview) validate
// against the same rules. Empty colour is allowed and treated as "no colour".
export const categoryFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required'),
  flow: z.enum(['Expense', 'Income', 'Both'], {
    errorMap: () => ({ message: 'Flow is required' }),
  }),
  // Optional hex colour — empty string is allowed (treated as "no colour").
  color: z
    .string()
    .trim()
    .refine(
      (v) => v === '' || /^#[0-9a-fA-F]{6}$/.test(v),
      'Use a 6-digit hex colour like #22c55e',
    ),
});

export type CategoryFormValues = z.input<typeof categoryFormSchema>;

/**
 * Shared name / flow / colour fields used by the create dialog here AND the
 * edit dialog in `categories-manager.tsx`. The parent owns the
 * `react-hook-form` instance (so each dialog can wire its own defaults, submit
 * handler, and reset lifecycle); this component just renders the inputs and the
 * validation messages. The data-testids are stable across both modes so tests
 * target the same handles whether they're creating or editing.
 */
export function CategoryFormFields({
  register,
  errors,
  flow,
  onFlowChange,
}: {
  register: ReturnType<typeof useForm<CategoryFormValues>>['register'];
  errors: ReturnType<typeof useForm<CategoryFormValues>>['formState']['errors'];
  flow: CategoryFlow;
  onFlowChange: (flow: CategoryFlow) => void;
}) {
  return (
    <>
      <div className="space-y-2">
        <Label htmlFor="category-name">Name</Label>
        <Input
          id="category-name"
          data-testid="category-name-input"
          {...register('name')}
          aria-invalid={Boolean(errors.name)}
          aria-describedby={errors.name ? 'category-name-error' : undefined}
        />
        {errors.name && (
          <p id="category-name-error" className="text-xs text-destructive" role="alert">
            {errors.name.message}
          </p>
        )}
      </div>

      <div className="grid grid-cols-[1fr_auto] gap-3">
        <div className="space-y-2">
          <Label htmlFor="category-flow">Flow</Label>
          <Select value={flow} onValueChange={(v) => onFlowChange(v as CategoryFlow)}>
            <SelectTrigger id="category-flow" data-testid="category-flow-select">
              <SelectValue placeholder="Select flow" />
            </SelectTrigger>
            <SelectContent>
              {FLOW_OPTIONS.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  {opt.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* Defensive: the flow Select only emits valid enum values. */}
          {/* v8 ignore start */}
          {errors.flow && (
            <p className="text-xs text-destructive" role="alert">
              {errors.flow.message}
            </p>
          )}
          {/* v8 ignore stop */}
        </div>

        <div className="space-y-2">
          <Label htmlFor="category-color">Colour</Label>
          <Input
            id="category-color"
            data-testid="category-color-input"
            type="color"
            className="h-10 w-16 p-1"
            {...register('color')}
            aria-invalid={Boolean(errors.color)}
          />
        </div>
      </div>
      {/* Defensive: the native colour input always yields a valid hex value. */}
      {/* v8 ignore start */}
      {errors.color && (
        <p className="text-xs text-destructive" role="alert">
          {errors.color.message}
        </p>
      )}
      {/* v8 ignore stop */}
    </>
  );
}

/**
 * Fully controlled create-category dialog. Unlike a `DialogTrigger`-driven
 * dialog, the parent owns `open` / `onOpenChange`, so this can be opened
 * programmatically — e.g. from a row in the import preview when the user picks
 * "+ New category…" in the per-row category picker. The settings manager wires
 * its own `add-category-button` trigger around it.
 *
 * `defaultFlow` seeds the form's flow on each open (the import row passes the
 * row's direction so a new category is created with the matching flow);
 * `onCreated` fires after a successful POST with the new id + name + flow so the
 * caller can immediately assign the category to the row that opened the dialog.
 */
export function CreateCategoryDialog({
  open,
  onOpenChange,
  defaultFlow = 'Expense',
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultFlow?: CategoryFlow;
  onCreated?: (category: { id: string; name: string; flow: CategoryFlow }) => void;
}) {
  const { mutateAsync, isPending } = useCreateCategory();

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<CategoryFormValues>({
    resolver: zodResolver(categoryFormSchema),
    defaultValues: { name: '', flow: defaultFlow, color: '#22c55e' },
  });

  const flow = watch('flow');

  const onSubmit = handleSubmit(async (values) => {
    const color = values.color.trim();
    try {
      const result = await mutateAsync({
        name: values.name.trim(),
        flow: values.flow,
        ...(color === '' ? {} : { color }),
      });
      toast.success('Category created');
      // Hand the new category back to the caller BEFORE resetting so it can
      // assign the id to whatever opened the dialog (e.g. the import row).
      onCreated?.({ id: result.id, name: values.name.trim(), flow: values.flow });
      reset();
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to create category');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) {
          // Re-seed defaults (incl. the caller's `defaultFlow`) on close so the
          // next open starts clean — a different import row may pass a
          // different flow.
          reset({ name: '', flow: defaultFlow, color: '#22c55e' });
        }
      }}
    >
      <DialogContent data-testid="create-category-dialog">
        <DialogHeader>
          <DialogTitle>Add category</DialogTitle>
          <DialogDescription>
            Categories group your transactions. Pick a flow to control where the category can be
            used; the colour shows up on charts and tables.
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
            <Button type="submit" disabled={isPending} data-testid="category-submit-button">
              {isPending ? 'Creating...' : 'Create category'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
