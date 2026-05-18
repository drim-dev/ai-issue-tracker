import { NextResponse, type NextRequest } from "next/server";

import { getApiBaseUrl } from "@/lib/api/config";
import type { ProblemDetails, UserResponse } from "@/lib/api/types";
import { getSession } from "@/lib/auth/session";

/**
 * BFF: логин. Проксирует в `.NET /auth/login` → при успехе ставит сессию и
 * возвращает `200`. Ошибки бэкенда (в т.ч. `401`) пробрасываются как есть.
 */
export async function POST(request: NextRequest) {
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    const problem: ProblemDetails = {
      type: "about:blank",
      title: "Bad Request",
      status: 400,
      detail: "Тело запроса не является корректным JSON.",
    };
    return NextResponse.json(problem, { status: 400 });
  }

  const response = await fetch(`${getApiBaseUrl()}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });

  if (!response.ok) {
    const text = await response.text();
    return new NextResponse(text, {
      status: response.status,
      headers: {
        "Content-Type":
          response.headers.get("Content-Type") ?? "application/problem+json",
      },
    });
  }

  const user = (await response.json()) as UserResponse;

  const session = await getSession();
  session.userId = user.id;
  await session.save();

  return NextResponse.json(user, { status: 200 });
}
