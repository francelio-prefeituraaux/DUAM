import type { LoginResponse } from "../types/auth";

const USUARIO_CACHE_KEY = "duam_usuario_cache";
const SESSION_KEY = "duam_login_session";

export function getCachedUsuario(): string | null {
  return localStorage.getItem(USUARIO_CACHE_KEY);
}

function setCachedUsuario(usuario: string): void {
  localStorage.setItem(USUARIO_CACHE_KEY, usuario);
}

export function getSession(): LoginResponse | null {
  const raw = localStorage.getItem(SESSION_KEY);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as LoginResponse;
  } catch {
    return null;
  }
}

function setSession(session: LoginResponse): void {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session));
}

export function saveLogin(usuario: string, session: LoginResponse): void {
  setCachedUsuario(usuario);
  setSession(session);
}

export function clearAuth(): void {
  localStorage.removeItem(USUARIO_CACHE_KEY);
  localStorage.removeItem(SESSION_KEY);
}
