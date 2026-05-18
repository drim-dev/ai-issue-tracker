"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { LogOut } from "lucide-react";

import { Button } from "@/components/ui/button";

/** Кнопка логаута: POST /api/auth/logout → редирект на /login. */
export function LogoutButton() {
  const router = useRouter();
  const [isLoading, setIsLoading] = useState(false);

  async function handleLogout() {
    setIsLoading(true);
    try {
      await fetch("/api/auth/logout", { method: "POST" });
    } finally {
      router.push("/login");
      router.refresh();
    }
  }

  return (
    <Button
      type="button"
      variant="ghost"
      size="compact"
      onClick={handleLogout}
      disabled={isLoading}
    >
      <LogOut aria-hidden="true" />
      Выйти
    </Button>
  );
}
