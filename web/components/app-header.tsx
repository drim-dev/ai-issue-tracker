import Link from "next/link";

import { LogoutButton } from "@/components/auth/logout-button";

/** Шапка приложения для аутентифицированных разделов: бренд + логаут. */
export function AppHeader() {
  return (
    <header className="border-b border-border bg-surface">
      <div className="mx-auto flex h-12 max-w-5xl items-center justify-between px-4">
        <Link href="/projects" className="text-sm font-semibold">
          AI Issue Tracker
        </Link>
        <LogoutButton />
      </div>
    </header>
  );
}
