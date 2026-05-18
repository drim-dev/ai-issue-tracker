import { z } from "zod";

/*
 * Zod-схемы Auth — зеркалят правила FluentValidation бэкенда.
 * Правила (см. дизайн Auth): email — формат, name — непусто + лимит,
 * password — мин. длина 8. confirmPassword проверяется только на клиенте.
 */

const NAME_MAX_LENGTH = 100;
const PASSWORD_MIN_LENGTH = 8;

const emailSchema = z
  .string()
  .min(1, "Email обязателен")
  .email("Неверный формат email");

const passwordSchema = z
  .string()
  .min(PASSWORD_MIN_LENGTH, "Пароль должен быть не менее 8 символов");

export const loginSchema = z.object({
  email: emailSchema,
  password: z.string().min(1, "Пароль обязателен"),
});

export type LoginInput = z.infer<typeof loginSchema>;

export const registerSchema = z
  .object({
    email: emailSchema,
    name: z
      .string()
      .min(1, "Имя обязательно")
      .max(NAME_MAX_LENGTH, "Имя должно быть не более 100 символов"),
    password: passwordSchema,
    confirmPassword: z.string().min(1, "Подтвердите пароль"),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Пароли не совпадают",
    path: ["confirmPassword"],
  });

export type RegisterInput = z.infer<typeof registerSchema>;
