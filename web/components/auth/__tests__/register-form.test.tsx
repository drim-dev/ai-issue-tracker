import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { RegisterForm } from "@/components/auth/register-form";

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

describe("RegisterForm", () => {
  beforeEach(() => {
    global.fetch = jest.fn() as jest.Mock;
    jest.clearAllMocks();
  });

  it("рендерит все поля регистрации", () => {
    render(<RegisterForm />);

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Имя")).toBeInTheDocument();
    expect(screen.getByLabelText("Пароль")).toBeInTheDocument();
    expect(
      screen.getByLabelText("Подтверждение пароля"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Зарегистрироваться" }),
    ).toBeInTheDocument();
  });

  it("показывает ошибку, если confirmPassword не совпадает с password", async () => {
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Email"), "user@example.com");
    await user.type(screen.getByLabelText("Имя"), "User");
    await user.type(screen.getByLabelText("Пароль"), "secret123");
    await user.type(
      screen.getByLabelText("Подтверждение пароля"),
      "different123",
    );
    await user.click(
      screen.getByRole("button", { name: "Зарегистрироваться" }),
    );

    await waitFor(() => {
      expect(screen.getByText("Пароли не совпадают")).toBeInTheDocument();
    });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("раскладывает ошибку 409 на поле email", async () => {
    mockFetch({
      ok: false,
      status: 409,
      json: () => ({
        type: "about:blank",
        title: "Conflict",
        status: 409,
        detail: "An account with this email already exists.",
        errorCode: "auth:user:email:already_exists",
      }),
    });
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Email"), "taken@example.com");
    await user.type(screen.getByLabelText("Имя"), "User");
    await user.type(screen.getByLabelText("Пароль"), "secret123");
    await user.type(
      screen.getByLabelText("Подтверждение пароля"),
      "secret123",
    );
    await user.click(
      screen.getByRole("button", { name: "Зарегистрироваться" }),
    );

    await waitFor(() => {
      expect(
        screen.getByText(
          "Пользователь с таким email уже зарегистрирован",
        ),
      ).toBeInTheDocument();
    });
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("редиректит на /projects при успешной регистрации", async () => {
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
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Email"), "user@example.com");
    await user.type(screen.getByLabelText("Имя"), "User");
    await user.type(screen.getByLabelText("Пароль"), "secret123");
    await user.type(
      screen.getByLabelText("Подтверждение пароля"),
      "secret123",
    );
    await user.click(
      screen.getByRole("button", { name: "Зарегистрироваться" }),
    );

    await waitFor(() => {
      expect(pushMock).toHaveBeenCalledWith("/projects");
    });
  });
});
