import { NextResponse, type NextRequest } from "next/server";

import { getApiBaseUrl } from "@/lib/api/config";
import type { ProblemDetails, UserResponse } from "@/lib/api/types";
import { getSession } from "@/lib/auth/session";
import { registerSchema } from "@/lib/validations/auth";

/**
 * BFF: регистрация. Zod-валидация → fetch `.NET /auth/register` → при успехе
 * ставит сессию и возвращает `200`. ProblemDetails бэкенда пробрасывается как есть.
 */
export async function POST(request: NextRequest) {
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return invalidPayload();
  }

  const parsed = registerSchema.safeParse(body);
  if (!parsed.success) {
    return validationProblem(parsed.error.issues);
  }

  // confirmPassword — клиентское правило, в .NET API не отправляем.
  const { email, name, password } = parsed.data;

  const response = await fetch(`${getApiBaseUrl()}/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, name, password }),
    cache: "no-store",
  });

  if (!response.ok) {
    return passthrough(response);
  }

  const user = (await response.json()) as UserResponse;

  const session = await getSession();
  session.userId = user.id;
  await session.save();

  return NextResponse.json(user, { status: 200 });
}

function invalidPayload() {
  const problem: ProblemDetails = {
    type: "about:blank",
    title: "Bad Request",
    status: 400,
    detail: "Тело запроса не является корректным JSON.",
  };
  return NextResponse.json(problem, { status: 400 });
}

function validationProblem(
  issues: { path: PropertyKey[]; message: string }[],
): NextResponse {
  const errors: Record<string, string[]> = {};
  for (const issue of issues) {
    const field = String(issue.path[0] ?? "");
    if (!field) continue;
    (errors[field] ??= []).push(issue.message);
  }
  const problem: ProblemDetails = {
    type: "about:blank",
    title: "One or more validation errors occurred",
    status: 400,
    errors,
  };
  return NextResponse.json(problem, { status: 400 });
}

async function passthrough(response: Response): Promise<NextResponse> {
  const text = await response.text();
  return new NextResponse(text, {
    status: response.status,
    headers: {
      "Content-Type":
        response.headers.get("Content-Type") ?? "application/problem+json",
    },
  });
}
