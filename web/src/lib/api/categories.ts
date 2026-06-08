'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CategoryDto, CategoryFlow, UpdateCategoryRequest } from '@/src/types/api';
import { apiClient } from './client';

export interface CreateCategoryRequest {
  name: string;
  flow: CategoryFlow;
  parentId?: string;
  color?: string;
  icon?: string;
}

export interface CreateCategoryResponse {
  id: string;
}

export const categoryKeys = {
  all: ['categories'] as const,
  list: (includeArchived: boolean) => [...categoryKeys.all, { includeArchived }] as const,
};

export function useCategories({ includeArchived = false }: { includeArchived?: boolean } = {}) {
  return useQuery({
    queryKey: categoryKeys.list(includeArchived),
    queryFn: () => apiClient.get<CategoryDto[]>(`/categories?includeArchived=${includeArchived}`),
  });
}

export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateCategoryRequest) =>
      apiClient.post<CreateCategoryResponse>('/categories', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryKeys.all });
    },
  });
}

/**
 * Renames / re-flows / re-colours a category via `PUT /categories/{id}`.
 * Fully replaces the editable fields (the backend returns 204). Invalidates
 * the category list so the row, its flow badge, and the colour dot refresh.
 */
export function useUpdateCategory(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateCategoryRequest) => apiClient.put<void>(`/categories/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryKeys.all });
    },
  });
}

export function useDeleteCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryKeys.all });
    },
  });
}

/**
 * Soft-archives a category. `DELETE /categories/{id}` is a soft delete on the
 * backend — the row stays and continues to back historical transactions; it
 * just stops appearing in non-archived lists. Same network call as
 * `useDeleteCategory`, named for the user-facing "Archive" action.
 */
export function useArchiveCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: categoryKeys.all });
    },
  });
}
