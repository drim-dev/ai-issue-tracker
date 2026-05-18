import type { ProblemDetails } from "@/lib/api/types";

/** Типизированная ошибка API: оборачивает ProblemDetails от бэкенда. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problemDetails: ProblemDetails,
  ) {
    super(problemDetails.detail ?? problemDetails.title);
    this.name = "ApiError";
  }

  get errorCode(): string | undefined {
    return this.problemDetails.errorCode;
  }

  get isValidationError(): boolean {
    return this.status === 400 && !!this.problemDetails.errors;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isConflict(): boolean {
    return this.status === 409;
  }

  get isServerError(): boolean {
    return this.status >= 500;
  }
}
