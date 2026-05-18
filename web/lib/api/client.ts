import "server-only";

import { getApiBaseUrl } from "@/lib/api/config";
import { ApiError } from "@/lib/api/error";
import type { ProblemDetails } from "@/lib/api/types";
import { getSession } from "@/lib/auth/session";

async function parseProblemDetails(
  response: Response,
): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return {
      type: "about:blank",
      title: response.statusText || "Request failed",
      status: response.status,
    };
  }
}

/**
 * Серверный fetch-хелпер к .NET API.
 *
 * - Читает сессию и подставляет доверенный заголовок `X-User-Id`.
 * - Sliding-продление: при валидной сессии вызывает `session.save()`,
 *   что переустанавливает cookie с новым ttl.
 * - Без сессии заголовок не ставится (для `/auth/me` это даст 401).
 * - Non-ok ответ → `ApiError` с разобранным ProblemDetails.
 */
export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const session = await getSession();

  const headers = new Headers(options.headers);
  if (session.userId) {
    headers.set("X-User-Id", session.userId);
    // Sliding-продление: каждый поход в API сдвигает срок жизни сессии.
    await session.save();
  }

  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...options,
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblemDetails(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
