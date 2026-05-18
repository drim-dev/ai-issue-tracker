import type { FieldValues, Path, UseFormReturn } from "react-hook-form";

import type { ProblemDetails } from "@/lib/api/types";

/** Сообщения для известных кодов ошибок (соглашение `domain:entity:field:type`). */
const ERROR_CODE_MESSAGES: Record<string, string> = {
  "auth:user:email:already_exists":
    "Пользователь с таким email уже зарегистрирован",
};

function messageForCode(code: string): string {
  return ERROR_CODE_MESSAGES[code] ?? code;
}

/** Ищет ключ в `record` без учёта регистра (.NET может слать PascalCase). */
function findKey(
  record: Record<string, unknown>,
  field: string,
): string | undefined {
  return Object.keys(record).find(
    (key) => key.toLowerCase() === field.toLowerCase(),
  );
}

/**
 * Достаёт сегмент поля из кода ошибки соглашения `domain:entity:field:type`
 * (напр. `auth:user:email:already_exists` → `email`).
 */
function fieldFromCode(code: string): string | undefined {
  const segments = code.split(":");
  return segments.length >= 4 ? segments[2] : undefined;
}

/**
 * Раскладывает ошибки `ProblemDetails` по полям формы.
 *
 * - `errors` (400) — сообщения валидации по полям;
 * - `errorCodes` (400) — коды ошибок по полям, маппятся на сообщения;
 * - `errorCode` (409 и др. доменные ошибки) — одиночный код; поле берётся
 *   из третьего сегмента кода (`domain:entity:field:type`).
 *
 * @param fields список полей формы, на которые допустима раскладка.
 * @returns `true`, если хотя бы одна ошибка разложена по полю.
 */
export function applyFieldErrors<TFieldValues extends FieldValues>(
  form: UseFormReturn<TFieldValues>,
  problem: ProblemDetails,
  fields: readonly Path<TFieldValues>[],
): boolean {
  let applied = false;

  for (const field of fields) {
    if (problem.errors) {
      const key = findKey(problem.errors, field);
      const message = key ? problem.errors[key]?.[0] : undefined;
      if (message) {
        form.setError(field, { type: "server", message });
        applied = true;
        continue;
      }
    }

    if (problem.errorCodes) {
      const key = findKey(problem.errorCodes, field);
      const code = key ? problem.errorCodes[key]?.[0] : undefined;
      if (code) {
        form.setError(field, { type: "server", message: messageForCode(code) });
        applied = true;
      }
    }
  }

  // Одиночный `errorCode` доменной ошибки (409 и др.) — поле из сегмента кода.
  if (problem.errorCode) {
    const fieldSegment = fieldFromCode(problem.errorCode);
    const match = fieldSegment
      ? fields.find((f) => f.toLowerCase() === fieldSegment.toLowerCase())
      : undefined;
    if (match) {
      form.setError(match, {
        type: "server",
        message: messageForCode(problem.errorCode),
      });
      applied = true;
    }
  }

  return applied;
}
