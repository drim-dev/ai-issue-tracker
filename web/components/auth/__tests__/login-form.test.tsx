import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { LoginForm } from "@/components/auth/login-form";

const pushMock = jest.fn();
const refreshMock = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock, refresh: refreshMock }),
}));

function mockFetch(response: Partial<Response> & { json?: () => unknown }) {
  (global.fetch as jest.Mock).mockResolvedValue({
    ok: response.ok ?? false,
    status: response.status ?? 200,
    json: async () => response.json?.() ?? {},
  });
}

describe("LoginForm", () => {
  beforeEach(() => {
    global.fetch = jest.fn() as jest.Mock;
    jest.clearAllMocks();
  });

  it("рендерит поля email и пароль и кнопку входа", () => {
    render(<LoginForm />);

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Пароль")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Войти" }),
    ).toBeInTheDocument();
  });

  it("показывает клиентские ошибки валидации при пустой форме", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.click(screen.getByRole("button", { name: "Войти" }));

    await waitFor(() => {
      expect(screen.getByText("Email обязателен")).toBeInTheDocument();
      expect(screen.getByText("Пароль обязателен")).toBeInTheDocument();
    });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("показывает клиентскую ошибку при невалидном формате email", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "not-an-email");
    await user.type(screen.getByLabelText("Пароль"), "secret123");
    await user.click(screen.getByRole("button", { name: "Войти" }));

    await waitFor(() => {
      expect(screen.getByText("Неверный формат email")).toBeInTheDocument();
    });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("показывает общую ошибку формы при ответе 401", async () => {
    mockFetch({ ok: false, status: 401 });
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "user@example.com");
    await user.type(screen.getByLabelText("Пароль"), "wrongpass");
    await user.click(screen.getByRole("button", { name: "Войти" }));

    await waitFor(() => {
      expect(
        screen.getByText("Неверный email или пароль"),
      ).toBeInTheDocument();
    });
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("редиректит на /projects при успешном логине", async () => {
    mockFetch({
      ok: true,
      status: 200,
      json: () => ({
        id: "1",
        email: "user@example.com",
        name: "User",
        avatar: null,
      }),
    });
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "user@example.com");
    await user.type(screen.getByLabelText("Пароль"), "secret123");
    await user.click(screen.getByRole("button", { name: "Войти" }));

    await waitFor(() => {
      expect(pushMock).toHaveBeenCalledWith("/projects");
    });
  });
});
