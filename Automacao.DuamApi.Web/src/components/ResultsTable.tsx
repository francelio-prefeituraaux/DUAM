import type { ResultadoLinhaResponse } from "../types/job";

export function ResultsTable({ resultados }: { resultados: ResultadoLinhaResponse[] }) {
  if (resultados.length === 0) {
    return <div className="empty-state">Nenhum resultado disponível ainda.</div>;
  }

  const ordenados = [...resultados].sort((a, b) => b.linha - a.linha);

  return (
    <>
      <div className="results-row-head">
        <span>Linha</span>
        <span>Inscrição</span>
        <span>Status</span>
        <span>Mensagem</span>
        <span>Processado em</span>
      </div>
      {ordenados.map((r) => (
        <div className="results-row" key={r.linha}>
          <span className="jobs-muted-cell">{r.linha}</span>
          <span>{r.inscricao}</span>
          <span className={`result-icon ${r.sucesso ? "ok" : "fail"}`}>
            {r.sucesso ? "✓" : "✕"}
          </span>
          <span className="jobs-muted-cell">{r.mensagem}</span>
          <span className="jobs-muted-cell" style={{ fontSize: 12 }}>
            {new Date(r.dataProcessamento).toLocaleString("pt-BR")}
          </span>
        </div>
      ))}
    </>
  );
}
