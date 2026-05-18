import { render, screen } from "@testing-library/react";

import HomePage from "@/app/page";

describe("HomePage", () => {
  it("рендерит заголовок приложения", () => {
    render(<HomePage />);
    expect(
      screen.getByRole("heading", { name: "AI Issue Tracker" }),
    ).toBeInTheDocument();
  });
});
