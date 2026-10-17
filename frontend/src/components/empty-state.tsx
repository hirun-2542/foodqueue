import type { ReactNode } from "react";

/**
 * Honest empty state for a screen whose data does not exist yet.
 * It says what is missing and why — never a mock dashboard with invented numbers.
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex max-w-md flex-col items-start gap-3 rounded-xl border border-border bg-surface p-6">
      <span className="flex size-11 items-center justify-center rounded-lg bg-sunken text-ink-2">{icon}</span>
      <h2 className="text-title">{title}</h2>
      <p className="text-body text-muted-foreground">{description}</p>
      {action}
    </div>
  );
}
