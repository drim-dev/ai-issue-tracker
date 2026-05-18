import type { SessionOptions } from "iron-session";
import { getIronSession, type IronSession } from "iron-session";
import { cookies } from "next/headers";

/** Полезная нагрузка сессии — только идентификатор пользователя (см. дизайн Auth). */
export interface SessionData {
  userId?: string;
}

const SESSION_TTL_SECONDS = 60 * 60 * 24 * 7; // 7 дней, sliding-продление в API-хелпере.

function getSessionSecret(): string {
  const secret = process.env.SESSION_SECRET;
  if (!secret || secret.length < 32) {
    throw new Error(
      "SESSION_SECRET не задан или короче 32 символов — проверь окружение.",
    );
  }
  return secret;
}

/** Конфиг iron-session: httpOnly + Secure + SameSite=Lax, ttl 7 дней. */
export function getSessionOptions(): SessionOptions {
  return {
    password: getSessionSecret(),
    cookieName: "ai_tracker_session",
    ttl: SESSION_TTL_SECONDS,
    cookieOptions: {
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      maxAge: SESSION_TTL_SECONDS,
    },
  };
}

/** Читает сессию из cookie текущего запроса. */
export async function getSession(): Promise<IronSession<SessionData>> {
  const cookieStore = await cookies();
  return getIronSession<SessionData>(cookieStore, getSessionOptions());
}
