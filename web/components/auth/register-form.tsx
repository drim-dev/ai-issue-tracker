"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { registerSchema, type RegisterInput } from "@/lib/validations/auth";
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

const REGISTER_FIELDS = [
  "email",
  "name",
  "password",
  "confirmPassword",
] as const;

export function RegisterForm() {
  const router = useRouter();
  const [formError, setFormError] = useState<string | null>(null);

  const form = useForm<RegisterInput>({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: "", name: "", password: "", confirmPassword: "" },
  });

  async function onSubmit(data: RegisterInput) {
    setFormError(null);

    let response: Response;
    try {
      response = await fetch("/api/auth/register", {
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

    const problem = (await response.json().catch(() => null)) as
      | ProblemDetails
      | null;

    // 400 — ошибки валидации по полям; 409 — занятый email через errorCode.
    if (problem && (response.status === 400 || response.status === 409)) {
      const applied = applyFieldErrors(form, problem, REGISTER_FIELDS);
      if (applied) return;
    }

    setFormError(problem?.detail ?? "Произошла ошибка. Попробуйте позже.");
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Регистрация</CardTitle>
        <CardDescription>
          Создайте аккаунт в AI Issue Tracker.
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
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Имя</FormLabel>
                  <FormControl>
                    <Input autoComplete="name" {...field} />
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
                      autoComplete="new-password"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="confirmPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Подтверждение пароля</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      autoComplete="new-password"
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
              {form.formState.isSubmitting
                ? "Регистрация..."
                : "Зарегистрироваться"}
            </Button>
          </form>
        </Form>

        <p className="mt-4 text-sm text-muted">
          Уже есть аккаунт?{" "}
          <Link href="/login" className="font-medium text-primary">
            Войти
          </Link>
        </p>
      </CardContent>
    </Card>
  );
}
