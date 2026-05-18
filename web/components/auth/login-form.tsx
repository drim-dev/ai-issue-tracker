"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { loginSchema, type LoginInput } from "@/lib/validations/auth";
import type { ProblemDetails } from "@/lib/api/types";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { applyFieldErrors } from "@/components/auth/problem-details";

export function LoginForm() {
  const router = useRouter();
  const [formError, setFormError] = useState<string | null>(null);

  const form = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  async function onSubmit(data: LoginInput) {
    setFormError(null);

    let response: Response;
    try {
      response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data),
      });
    } catch {
      setFormError("Не удалось связаться с сервером. Попробуйте позже.");
      return;
    }

    if (response.ok) {
      router.push("/projects");
      router.refresh();
      return;
    }

    // 401 — не раскрываем, существует ли email: общая ошибка формы.
    if (response.status === 401) {
      setFormError("Неверный email или пароль");
      return;
    }

    const problem = (await response.json().catch(() => null)) as
      | ProblemDetails
      | null;

    // 400 — раскладываем ошибки валидации по полям.
    if (response.status === 400 && problem?.errors) {
      applyFieldErrors(form, problem, ["email", "password"]);
      return;
    }

    setFormError(problem?.detail ?? "Произошла ошибка. Попробуйте позже.");
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Вход</CardTitle>
        <CardDescription>
          Войдите в AI Issue Tracker по email и паролю.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
            noValidate
          >
            {formError && (
              <p
                role="alert"
                className="rounded-md border border-danger bg-surface-muted px-3 py-2 text-sm text-danger"
              >
                {formError}
              </p>
            )}

            <FormField
              control={form.control}
              name="email"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Email</FormLabel>
                  <FormControl>
                    <Input
                      type="email"
                      autoComplete="email"
                      placeholder="you@example.com"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Пароль</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      autoComplete="current-password"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <Button
              type="submit"
              className="w-full"
              disabled={form.formState.isSubmitting}
            >
              {form.formState.isSubmitting ? "Вход..." : "Войти"}
            </Button>
          </form>
        </Form>

        <p className="mt-4 text-sm text-muted">
          Нет аккаунта?{" "}
          <Link href="/register" className="font-medium text-primary">
            Зарегистрироваться
          </Link>
        </p>
      </CardContent>
    </Card>
  );
}
