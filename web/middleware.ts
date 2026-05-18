import { getIronSession } from "iron-session";
import { NextResponse, type NextRequest } from "next/server";

import { getSessionOptions, type SessionData } from "@/lib/auth/session";

/** Маршруты группы `(auth)` — логин/регистрация. */
const AUTH_ROUTES = ["/login", "/register"];

function isAuthRoute(pathname: string): boolean {
  return AUTH_ROUTES.some(
    (route) => pathname === route || pathname.startsWith(`${route}/`),
  );
}

/**
 * Защита маршрутов:
 * - нет сессии и маршрут защищён → редирект на `/login`;
 * - есть сессия и пользователь на `(auth)`-странице → редирект на `/projects`.
 */
export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const response = NextResponse.next();

  const session = await getIronSession<SessionData>(
    request,
    response,
    getSessionOptions(),
  );
  const isAuthenticated = !!session.userId;

  if (isAuthRoute(pathname)) {
    if (isAuthenticated) {
      return NextResponse.redirect(new URL("/projects", request.url));
    }
    return response;
  }

  if (!isAuthenticated) {
    const loginUrl = new URL("/login", request.url);
    return NextResponse.redirect(loginUrl);
  }

  return response;
}

/**
 * Защищаем всё, кроме статики, API-роутов BFF и корневой страницы.
 * `(auth)`-страницы попадают под matcher и обрабатываются выше.
 */
export const config = {
  matcher: ["/projects/:path*", "/login", "/register"],
};
