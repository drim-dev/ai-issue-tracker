import Link from "next/link";

export default function HomePage() {
  return (
    <main className="mx-auto flex min-h-screen max-w-5xl flex-col items-center justify-center gap-6 px-4">
      <h1 className="text-2xl font-semibold">AI Issue Tracker</h1>
      <p className="text-sm text-muted">
        Issue-трекер с AI-триажем бэклога и AI-ревьюером pull request&apos;ов.
      </p>
      <Link
        href="/projects"
        className="inline-flex h-9 items-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground"
      >
        Перейти к проектам
      </Link>
    </main>
  );
}
