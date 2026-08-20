import type { JobStatus } from "../types/job";

const STATUS_META: Record<JobStatus, { label: string; bg: string; fg: string }> = {
  Pendente: { label: "Pendente", bg: "var(--status-pendente-bg)", fg: "var(--status-pendente-fg)" },
  EmProcessamento: { label: "Processando", bg: "var(--status-processando-bg)", fg: "var(--status-processando-fg)" },
  Concluido: { label: "Concluído", bg: "var(--status-concluido-bg)", fg: "var(--status-concluido-fg)" },
  Falhou: { label: "Falhou", bg: "var(--status-falhou-bg)", fg: "var(--status-falhou-fg)" },
};

export function StatusBadge({ status, errorCount = 0 }: { status: JobStatus; errorCount?: number }) {
  const meta = STATUS_META[status];
  const showErrorPill = status === "Concluido" && errorCount > 0;

  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
      <span className="badge" style={{ background: `hsl(${meta.bg})`, color: `hsl(${meta.fg})` }}>
        {meta.label}
      </span>
      {showErrorPill && (
        <span
          className="badge"
          style={{ background: "hsl(var(--status-falhou-bg))", color: "hsl(var(--status-falhou-fg))" }}
        >
          {errorCount === 1 ? "1 erro" : `${errorCount} erros`}
        </span>
      )}
    </span>
  );
}
