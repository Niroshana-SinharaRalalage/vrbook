/**
 * Typed fetch wrapper for the VrBook REST API (proposal §6).
 *
 * Responsibilities:
 *   - Resolve the base URL from NEXT_PUBLIC_API_BASE_URL.
 *   - Inject `Authorization: Bearer <token>` from an injected token provider
 *     (set up in `Providers.tsx`; defaults to anonymous).
 *   - Propagate W3C `traceparent` if Application Insights / OTel set one.
 *   - Throw `ApiProblemError` on non-2xx with RFC 7807 problem details.
 *   - Surface `Idempotency-Key` as a first-class option for mutating calls.
 *
 * The generated OpenAPI client (`npm run gen:api`) will sit alongside this
 * file; that client can be configured to delegate header injection here.
 */

export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;
  readonly [key: string]: unknown;
}

export class ApiProblemError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.title ?? problem.detail ?? `Request failed (${status})`);
    this.name = 'ApiProblemError';
    this.status = status;
    this.problem = problem;
  }
}

export type TokenProvider = () => Promise<string | null> | string | null;

let tokenProvider: TokenProvider = () => null;

/** Wire the auth provider from `Providers.tsx`. */
export const setTokenProvider = (provider: TokenProvider): void => {
  tokenProvider = provider;
};

/** Read the current W3C trace header, if any tracer set one. */
const readTraceparent = (): string | null => {
  if (typeof window === 'undefined') return null;
  // App Insights / OTel browser SDKs typically expose the current span via a global.
  const w = window as unknown as { traceparent?: string };
  return w.traceparent ?? null;
};

export interface RequestOptions extends Omit<RequestInit, 'body' | 'headers'> {
  readonly query?: Record<string, string | number | boolean | undefined | null>;
  readonly body?: unknown;
  readonly headers?: Record<string, string>;
  readonly idempotencyKey?: string;
  /** Skip Authorization header (for anonymous endpoints). */
  readonly anonymous?: boolean;
}

const buildUrl = (path: string, query?: RequestOptions['query']): string => {
  const base = process.env.NEXT_PUBLIC_API_BASE_URL?.replace(/\/+$/, '') ?? '';
  const cleanPath = path.startsWith('/') ? path : `/${path}`;
  const url = new URL(`${base}${cleanPath}`);
  if (query) {
    for (const [k, v] of Object.entries(query)) {
      if (v === undefined || v === null) continue;
      url.searchParams.append(k, String(v));
    }
  }
  return url.toString();
};

const parseProblem = async (res: Response): Promise<ProblemDetails> => {
  const ct = res.headers.get('content-type') ?? '';
  if (ct.includes('application/problem+json') || ct.includes('application/json')) {
    try {
      return (await res.json()) as ProblemDetails;
    } catch {
      /* fall through */
    }
  }
  return { status: res.status, title: res.statusText };
};

export const apiFetch = async <T = unknown>(
  path: string,
  options: RequestOptions = {},
): Promise<T> => {
  const {
    query,
    body,
    headers: extraHeaders,
    idempotencyKey,
    anonymous = false,
    method = body === undefined ? 'GET' : 'POST',
    ...init
  } = options;

  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...extraHeaders,
  };

  if (body !== undefined && !(body instanceof FormData)) {
    headers['Content-Type'] = headers['Content-Type'] ?? 'application/json';
  }

  if (!anonymous) {
    const token = await tokenProvider();
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }

  const traceparent = readTraceparent();
  if (traceparent) headers['traceparent'] = traceparent;

  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  const res = await fetch(buildUrl(path, query), {
    ...init,
    method,
    headers,
    body:
      body === undefined
        ? undefined
        : body instanceof FormData
          ? body
          : JSON.stringify(body),
  });

  if (!res.ok) {
    const problem = await parseProblem(res);
    throw new ApiProblemError(res.status, problem);
  }

  if (res.status === 204) return undefined as T;

  const ct = res.headers.get('content-type') ?? '';
  if (ct.includes('application/json')) {
    return (await res.json()) as T;
  }
  return (await res.text()) as unknown as T;
};
