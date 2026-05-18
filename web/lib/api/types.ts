/** Контракты .NET API. Зеркалят DTO бэкенда — держим в синхроне. */

/** Ответ эндпоинтов Auth (`/auth/register`, `/auth/login`, `/auth/me`). */
export interface UserResponse {
  id: string;
  email: string;
  name: string;
  avatar: string | null;
}

/** RFC 7807 ProblemDetails — единый формат ошибок бэкенда. */
export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  traceId?: string;
  /** Ошибки валидации по полям (400). */
  errors?: Record<string, string[]>;
  /** Коды ошибок по полям — соглашение `domain:entity:field:error_type`. */
  errorCodes?: Record<string, string[]>;
}
