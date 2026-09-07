'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
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
import { ApiError } from '@/src/lib/api/client';
import { useAddPoolParticipant } from '@/src/lib/api/pools';
import { todayIsoUtc } from '@/src/lib/utils/date';

const today = () => todayIsoUtc();

const schema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(100, 'Name must be 100 characters or less'),
  joinedOn: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Join date is required')
    .refine((v) => v <= today(), { message: 'Join date cannot be in the future' }),
});

type FormValues = z.input<typeof schema>;

interface Props {
  poolId: string;
  poolName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Adds someone to the pool's roster. They start with **no share** — this
 * writes no money and no units. Their stake begins the moment their first
 * subscription is recorded, which is the step that demands a fresh valuation.
 *
 * Splitting the two is deliberate: it lets the roster carry someone who has
 * agreed to join before their transfer clears, without a phantom claim on the
 * pool in the meantime.
 */
export function AddParticipantDialog({ poolId, poolName, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const { mutateAsync, isPending } = useAddPoolParticipant(poolId);

  const {
    handleSubmit,
    register,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', joinedOn: today() },
  });

  const onSubmit = handleSubmit(async (values) => {
    setApiError(null);
    try {
      await mutateAsync({ name: values.name, joinedOn: values.joinedOn });
      toast.success(`Added ${values.name} to ${poolName}`);
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to add participant');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) {
          reset();
          setApiError(null);
        }
      }}
    >
      <DialogContent data-testid="add-participant-dialog">
        <DialogHeader>
          <DialogTitle>Add participant</DialogTitle>
          <DialogDescription>
            Adds someone to {poolName}&rsquo;s roster with no share yet. Record their money
            separately, when it actually arrives — that is the step that prices their share.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="participant-name">Name</Label>
            <Input
              id="participant-name"
              data-testid="participant-name-input"
              maxLength={100}
              placeholder="e.g. Andrei"
              {...register('name')}
              aria-invalid={Boolean(errors.name)}
            />
            {errors.name && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="participant-name-error"
              >
                {errors.name.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="participant-joined">Joined on</Label>
            <Input
              id="participant-joined"
              data-testid="participant-joined-input"
              type="date"
              max={today()}
              {...register('joinedOn')}
              aria-invalid={Boolean(errors.joinedOn)}
            />
            {errors.joinedOn && (
              <p className="text-xs text-destructive" role="alert">
                {errors.joinedOn.message}
              </p>
            )}
          </div>

          {apiError && (
            <p
              className="text-sm text-destructive"
              role="alert"
              data-testid="add-participant-error"
            >
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="participant-submit-button">
              {isPending ? 'Adding...' : 'Add participant'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
