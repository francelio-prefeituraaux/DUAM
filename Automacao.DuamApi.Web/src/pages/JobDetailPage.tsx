import { Link, useParams } from "react-router-dom";
import { useJobStatus } from "../hooks/useJobStatus";
import { StatusBadge } from "../components/StatusBadge";
import { ProgressBar } from "../components/ProgressBar";
import { ResultsTable } from "../components/ResultsTable";
import { ApiError } from "../api/duamApi";

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleDateString("pt-BR") + " " + d.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

export function JobDetailPage() {
  const { jobId } = useParams<{ jobId: string }>();
  const { data: job, isLoading, error } = useJobStatus(jobId);

  if (isLoading) return <p>Carregando job...</p>;

  if (error) {
    const message =
      error instanceof ApiError && error.status === 404
        ? "Job não encontrado."
        : "Falha ao carregar o job.";
    return <p style={{ color: "#d93025" }}>{message}</p>;
  }

  if (!job) return null;

  return (
    <div>
      <Link to="/acompanhamento" className="back-link">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
          <path d="m15 18-6-6 6-6" />
        </svg>
        <span>Voltar para Acompanhamento</span>
      </Link>

      <div className="card" style={{ marginBottom: 20 }}>
        <div className="detail-header">
          <div>
            <div className="detail-title-row">
              <h1>{job.nomeArquivoOriginal}</h1>
              <StatusBadge status={job.status} errorCount={job.linhasComErro} />
            </div>
            <p className="detail-meta">
              Enviado por {job.usuario} · Job #{job.jobId}
            </p>
          </div>
        </div>

        <div style={{ margin: "20px 0" }}>
          <div style={{ display: "flex", justifyContent: "space-between", fontSize: 13, color: "hsl(var(--muted-foreground))", marginBottom: 6 }}>
            <span>Progresso</span>
            <span>
              {job.linhasProcessadas}/{job.totalLinhas ?? "?"}
            </span>
          </div>
          <ProgressBar
            processadas={job.linhasProcessadas}
            total={job.totalLinhas}
            failed={job.status === "Falhou"}
            size="lg"
            showLabel={false}
          />
        </div>

        <div className="stat-grid">
          <div>
            <div className="stat-label">Total de linhas</div>
            <div className="stat-value">{job.totalLinhas ?? "—"}</div>
          </div>
          <div>
            <div className="stat-label">Criado em</div>
            <div className="stat-value">{formatDate(job.dataCriacao)}</div>
          </div>
          <div>
            <div className="stat-label">Iniciado em</div>
            <div className="stat-value">{formatDate(job.dataInicio)}</div>
          </div>
          <div>
            <div className="stat-label">Concluído em</div>
            <div className="stat-value">{formatDate(job.dataFim)}</div>
          </div>
        </div>

        {job.mensagemErro && <div className="error-box">{job.mensagemErro}</div>}
      </div>

      <div className="results-card">
        <div className="results-card-title">Resultado por linha</div>
        <ResultsTable resultados={job.resultados} />
      </div>
    </div>
  );
}
