import { NavLink, useNavigate } from "react-router-dom";
import { clearAuth } from "../lib/authStorage";
import { TOUR_KEYS, resetTour } from "../lib/tourStorage";

export function Sidebar() {
  const navigate = useNavigate();

  function handleSair() {
    clearAuth();
    navigate("/login");
  }

  function handleAjuda() {
    resetTour(TOUR_KEYS.upload);
    resetTour(TOUR_KEYS.jobs);
    window.location.reload();
  }

  return (
    <div className="sidebar">
      <div className="sidebar-brand" data-tour="sidebar-brand">
        <div className="sidebar-brand-name text-gradient">DUAM Monitor</div>
        <div className="sidebar-brand-sub">SIG Prodataweb · Araguaína/TO</div>
      </div>

      <nav className="sidebar-nav">
        <NavLink
          to="/"
          end
          data-tour="nav-upload"
          className={({ isActive }) => `nav-item${isActive ? " active" : ""}`}
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 3v12" />
            <path d="m7 8 5-5 5 5" />
            <path d="M5 21h14a2 2 0 0 0 2-2v-5a2 2 0 0 0-2-2h-3" />
            <path d="M5 21a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h3" />
          </svg>
          <span>Enviar Planilha</span>
        </NavLink>

        <NavLink
          to="/acompanhamento"
          data-tour="nav-jobs"
          className={({ isActive }) => `nav-item${isActive ? " active" : ""}`}
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M4 6h16" />
            <path d="M4 12h16" />
            <path d="M4 18h16" />
          </svg>
          <span>Acompanhamento</span>
        </NavLink>

        <div className="nav-divider" />

        <div className="nav-item-static" onClick={handleAjuda} style={{ cursor: "pointer" }}>
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3" />
            <path d="M12 17h.01" />
          </svg>
          <span>Ajuda</span>
        </div>

        <div className="nav-item-static" onClick={handleSair} style={{ cursor: "pointer" }}>
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
            <path d="M16 17l5-5-5-5" />
            <path d="M21 12H9" />
          </svg>
          <span>Sair</span>
        </div>
      </nav>
    </div>
  );
}
