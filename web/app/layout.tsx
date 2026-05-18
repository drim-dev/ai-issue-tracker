import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "AI Issue Tracker",
  description: "Issue-трекер с AI-триажем бэклога и AI-ревьюером pull request'ов",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru">
      <body>{children}</body>
    </html>
  );
}
