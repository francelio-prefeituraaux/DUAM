import type {
  JobEnqueuedResponse,
  JobListaResponse,
  JobListFilters,
  JobStatusResponse,
} from "../types/job";
import type { LoginResponse } from "../types/auth";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function parseErrorMessage(response: Response): Promise<string> {
  const text = await response.text();
  return text || `Erro ${response.status}`;
}

export interface PostLoginInput {
  usuario: string;
  senha: string;
}

export async function postLogin(input: PostLoginInput): Promise<LoginResponse> {
  const response = await fetch(`${API_BASE_URL}/duam/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ usuario: input.usuario, senha: input.senha }),
  });

  if (!response.ok) {
    if (response.status === 401) {
      throw new ApiError("Usuário ou senha inválidos.", response.status);
    }
    throw new ApiError(await parseErrorMessage(response), response.status);
  }

  return response.json();
}

export interface PostProcessarInput {
  usuario: string;
  senha: string;
  planilha: File;
}

export async function postProcessar(
  input: PostProcessarInput,
): Promise<JobEnqueuedResponse> {
  const formData = new FormData();
  formData.append("Usuario", input.usuario);
  formData.append("Senha", input.senha);
  formData.append("Planilha", input.planilha);

  const response = await fetch(`${API_BASE_URL}/duam/processar`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response), response.status);
  }

  return response.json();
}

export async function getJob(jobId: string): Promise<JobStatusResponse> {
  const response = await fetch(`${API_BASE_URL}/duam/jobs/${jobId}`);

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response), response.status);
  }

  return response.json();
}

export async function listJobs(
  filters: JobListFilters,
): Promise<JobListaResponse> {
  const params = new URLSearchParams();

  if (filters.usuario) params.set("usuario", filters.usuario);
  if (filters.status) params.set("status", filters.status);
  params.set("pagina", String(filters.pagina ?? 1));
  params.set("tamanhoPagina", String(filters.tamanhoPagina ?? 20));

  const response = await fetch(`${API_BASE_URL}/duam/jobs?${params}`);

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response), response.status);
  }

  return response.json();
}
