const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? '';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<TResponse>(path: string, init: RequestInit = {}): Promise<TResponse> {
  const headers = new Headers(init.headers);
  const isFormData = typeof FormData !== 'undefined' && init.body instanceof FormData;
  if (init.body && !isFormData && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  headers.set('Accept', 'application/json');

  const res = await fetch(`${BASE_URL}${path}`, { ...init, headers });

  if (!res.ok) {
    let body: unknown = null;
    try {
      body = await res.json();
    } catch {
      // ignore
    }
    // The .NET API returns RFC-7807 ProblemDetails — the human message lives
    // in `detail` (with `title` as a coarser fallback). A few MSW mocks and
    // ad-hoc responses use `error`. Prefer the real backend shape, fall back
    // to the legacy key, then a generic status message.
    const record = body && typeof body === 'object' ? (body as Record<string, unknown>) : null;
    const message = record
      ? String(
          record.detail ??
            record.error ??
            record.title ??
            `Request failed with status ${res.status}`,
        )
      : `Request failed with status ${res.status}`;
    throw new ApiError(res.status, message, body);
  }

  if (res.status === 204) {
    return undefined as TResponse;
  }

  return (await res.json()) as TResponse;
}

export const apiClient = {
  get: <T>(path: string): Promise<T> => request<T>(path, { method: 'GET' }),
  post: <T>(path: string, body?: unknown): Promise<T> => {
    const init: RequestInit = { method: 'POST' };
    if (body !== undefined) init.body = JSON.stringify(body);
    return request<T>(path, init);
  },
  put: <T>(path: string, body?: unknown): Promise<T> => {
    const init: RequestInit = { method: 'PUT' };
    if (body !== undefined) init.body = JSON.stringify(body);
    return request<T>(path, init);
  },
  patch: <T>(path: string, body?: unknown): Promise<T> => {
    const init: RequestInit = { method: 'PATCH' };
    if (body !== undefined) init.body = JSON.stringify(body);
    return request<T>(path, init);
  },
  postForm: <T>(path: string, formData: FormData): Promise<T> =>
    request<T>(path, { method: 'POST', body: formData }),
  delete: <T>(path: string): Promise<T> => request<T>(path, { method: 'DELETE' }),
};
