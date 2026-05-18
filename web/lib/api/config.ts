import "server-only";

/**
 * Базовый URL .NET API. API не публикуется наружу — доступен только из
 * Next.js (Server Components и BFF route handlers) внутри сети Aspire.
 */
export function getApiBaseUrl(): string {
  const baseUrl = process.env.API_BASE_URL;
  if (!baseUrl) {
    throw new Error("API_BASE_URL не задан — проверь окружение.");
  }
  return baseUrl.replace(/\/$/, "");
}
