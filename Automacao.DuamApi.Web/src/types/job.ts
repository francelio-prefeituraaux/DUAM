export type JobStatus = "Pendente" | "EmProcessamento" | "Concluido" | "Falhou";

export const TERMINAL_STATUSES: JobStatus[] = ["Concluido", "Falhou"];

export interface ResultadoLinhaResponse {
  linha: number;
  inscricao: string;
  sucesso: boolean;
  mensagem: string;
  dataProcessamento: string;
}

export interface JobStatusResponse {
  jobId: string;
  status: JobStatus;
  usuario: string;
  nomeArquivoOriginal: string;
  totalLinhas: number | null;
  linhasProcessadas: number;
  linhasComErro: number;
  dataCriacao: string;
  dataInicio: string | null;
  dataFim: string | null;
  mensagemErro: string | null;
  resultados: ResultadoLinhaResponse[];
}

export interface JobResumoResponse {
  jobId: string;
  status: JobStatus;
  usuario: string;
  nomeArquivoOriginal: string;
  totalLinhas: number | null;
  linhasProcessadas: number;
  linhasComErro: number;
  dataCriacao: string;
  dataInicio: string | null;
  dataFim: string | null;
}

export interface JobListaResponse {
  total: number;
  pagina: number;
  tamanhoPagina: number;
  jobs: JobResumoResponse[];
}

export interface JobEnqueuedResponse {
  jobId: string;
  status: JobStatus;
  statusUrl: string;
}

export interface JobListFilters {
  usuario?: string;
  status?: JobStatus | "";
  pagina?: number;
  tamanhoPagina?: number;
}
