import { NextResponse } from "next/server";

import { getSession } from "@/lib/auth/session";

/**
 * BFF: логаут. Уничтожает сессионную cookie. API stateless — отзывать нечего.
 */
export async function POST() {
  const session = await getSession();
  session.destroy();
  return NextResponse.json({ ok: true }, { status: 200 });
}
