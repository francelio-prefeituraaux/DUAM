import { useState } from "react";
import { Link } from "react-router-dom";
import type { DriveStep } from "driver.js";
import { useJobList } from "../hooks/useJobList";
import { StatusBadge } from "../components/StatusBadge";
import { ProgressBar } from "../components/ProgressBar";
import { useAutoTour } from "../hooks/useAutoTour";
import { TOUR_KEYS } from "../lib/tourStorage";
import type { JobStatus } from "../types/job";

const STATUS_OPTIONS: JobStatus[] = ["Pendente", "EmProcessamento", "Concluido", "Falhou"];

const TOUR_STEPS: DriveStep[] = [
  {
    element: '[data-tour="jobs-filtros"]',
    popover: {
      title: "Filtros",
      description: "Filtre por usuário, status do processamento, ou quantos registros mostrar por página.",
    },
  },
  {
    element: '[data-tour="jobs-tabela"]',
    popover: {
      title: "Histórico de envios",
      description: "Cada linha é uma planilha enviada, com status e progresso. Clique no ícone de olho pra ver o resultado linha a linha.",
    },
  },
  {
    element: '[data-tour="jobs-paginacao"]',
    popover: {
      title: "Paginação",
      description: "Navegue entre as páginas do histórico completo.",
    },
  },
  {
    element: '[data-tour="jobs-novo-envio"]',
    popover: {
      title: "Novo envio",
      description: "Volta pra tela de Enviar Planilha a qualquer momento.",
    },
  },
];

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleDateString("pt-BR") + " " + d.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

export function JobListPage() {
  const [usuario, setUsuario] = useState("");
  const [status, setStatus] = useState<JobStatus | "">("");
  const [tamanhoPagina, setTamanhoPagina] = useState(10);
  const [pagina, setPagina] = useState(1);

  const { data, isLoading, error } = useJobList({
    usuario: usuario || undefined,
    status: status || undefined,
    pagina,
    tamanhoPagina,
  });

  const total = data?.total ?? 0;
  const totalPaginas = Math.max(1, Math.ceil(total / tamanhoPagina));
  const start = total === 0 ? 0 : (pagina - 1) * tamanhoPagina + 1;
  const end = Math.min(pagina * tamanhoPagina, total);

  useAutoTour(TOUR_KEYS.jobs, TOUR_STEPS, !!data);

  return (
    <div>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 24 }}>
        <div>
          <h1 className="page-title text-gradient">Acompanhamento</h1>
          <p style={{ color: "hsl(var(--muted-foreground))", fontSize: 14, margin: 0 }}>
            Acompanhe o processamento das planilhas enviadas.
          </p>
        </div>
        <Link to="/" className="btn-primary" data-tour="jobs-novo-envio" style={{ textDecoration: "none" }}>
          + Novo Envio
        </Link>
      </div>

      <div className="toolbar" data-tour="jobs-filtros">
        <div style={{ display: "flex", alignItems: "center", gap: 16, flexWrap: "wrap" }}>
          <div className="entries-control">
            <span>Mostrar</span>
            <select
              value={tamanhoPagina}
              onChange={(e) => {
                setTamanhoPagina(Number(e.target.value));
                setPagina(1);
              }}
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
            </select>
            <span>registros</span>
          </div>

          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as JobStatus | "");
              setPagina(1);
            }}
            style={{ border: "1px solid hsl(var(--border))", borderRadius: 6, padding: "6px 8px", fontSize: 14 }}
          >
            <option value="">Todos os status</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>

        <div className="search-box">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--muted-foreground))" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="11" cy="11" r="8" />
            <path d="m21 21-4.3-4.3" />
          </svg>
          <input
            type="text"
            value={usuario}
            onChange={(e) => {
              setUsuario(e.target.value);
              setPagina(1);
            }}
            placeholder="Buscar por usuário"
          />
        </div>
      </div>

      {isLoading && <p>Carregando...</p>}
      {error && <p style={{ color: "#d93025" }}>Falha ao carregar jobs.</p>}

      {data && (
        <div className="jobs-table" data-tour="jobs-tabela">
          <div className="jobs-table-inner">
            <div className="jobs-row-head">
              <span>Arquivo</span>
              <span>Usuário</span>
              <span>Status</span>
              <span>Progresso</span>
              <span>Criado</span>
              <span>Concluído</span>
              <span>Ações</span>
            </div>

            {data.jobs.map((job) => (
              <div className="jobs-row" key={job.jobId}>
                <div className="jobs-file-cell">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--muted-foreground))" strokeWidth="1.5" style={{ flex: "none" }}>
                    <path d="M14.5 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8.5L14.5 3z" />
                    <path d="M14 3v6h6" />
                  </svg>
                  <span>{job.nomeArquivoOriginal}</span>
                </div>
                <span className="jobs-muted-cell">{job.usuario}</span>
                <StatusBadge status={job.status} errorCount={job.linhasComErro} />
                <ProgressBar
                  processadas={job.linhasProcessadas}
                  total={job.totalLinhas}
                  failed={job.status === "Falhou"}
                />
                <span className="jobs-muted-cell" style={{ fontSize: 12 }}>{formatDate(job.dataCriacao)}</span>
                <span className="jobs-muted-cell" style={{ fontSize: 12 }}>{formatDate(job.dataFim)}</span>
                <Link to={`/acompanhamento/${job.jobId}`} className="btn-outline" style={{ width: "fit-content" }}>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                </Link>
              </div>
            ))}

            {data.jobs.length === 0 && <div className="empty-state">Nenhum job encontrado.</div>}
          </div>

          <div className="pagination-footer" data-tour="jobs-paginacao">
            <span style={{ fontSize: 13, color: "hsl(var(--muted-foreground))" }}>
              Mostrando {start} a {end} de {total}
            </span>
            <div className="pager">
              <button className="pager-btn" onClick={() => setPagina((p) => Math.max(1, p - 1))} disabled={pagina <= 1}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="m15 18-6-6 6-6" />
                </svg>
              </button>
              <span className="pager-page">{pagina}</span>
              <button
                className="pager-btn"
                onClick={() => setPagina((p) => Math.min(totalPaginas, p + 1))}
                disabled={pagina >= totalPaginas}
              >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="m9 18 6-6 6-6" />
                </svg>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
