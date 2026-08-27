import { getSession } from "../lib/authStorage";

export function Topbar() {
  const session = getSession();

  if (!session) return null;

  return (
    <div className="topbar" data-tour="topbar">
      <div className="topbar-item">
        <span className="topbar-label">Usuário logado</span>
        <span className="topbar-login">{session.login}</span>
      </div>
      {session.nomeEmpresa && (
        <div className="topbar-item">
          <span className="topbar-label">Empresa</span>
          <span className="topbar-empresa">{session.nomeEmpresa}</span>
        </div>
      )}
    </div>
  );
}
