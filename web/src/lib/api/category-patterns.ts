'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  CategoryPatternDto,
  CreateCategoryPatternRequest,
  CreateCategoryPatternResponse,
  UpdateCategoryPatternRequest,
} from '@/src/types/api';
import { apiClient } from './client';

export const categoryPatternKeys = {
  all: ['category-patterns'] as const,
};

/**
 * Lists every auto-categorization keyword pattern (both Seeded defaults and
 * Learned rules). Patterns only influence future imports, so this is the
 * single query root the mutations below invalidate.
 */
export function useCategoryPatterns() {
  return useQuery({
    queryKey: categoryPatternKeys.all,
    queryFn: () => apiClient.get<CategoryPatternDto[]>('/category-patterns'),
  });
}

export function useCreateCategoryPattern() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateCategoryPatternRequest) =>
      apiClient.post<CreateCategoryPatternResponse>('/category-patterns', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryPatternKeys.all });
    },
  });
}

export function useUpdateCategoryPattern(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateCategoryPatternRequest) =>
      apiClient.put<void>(`/category-patterns/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryPatternKeys.all });
    },
  });
}

export function useDeleteCategoryPattern() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/category-patterns/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryPatternKeys.all });
    },
  });
}
