import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useUploadDuam } from "../hooks/useUploadDuam";
import { ApiError } from "../api/duamApi";
import { getCachedUsuario } from "../lib/authStorage";

export function UploadPage() {
  const navigate = useNavigate();
  const upload = useUploadDuam();

  const [usuario, setUsuario] = useState(() => getCachedUsuario() ?? "");
  const [senha, setSenha] = useState("");
  const [planilha, setPlanilha] = useState<File | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  function validate(): string | null {
    if (!usuario.trim()) return "Usuário é obrigatório.";
    if (!senha.trim()) return "Senha é obrigatória.";
    if (!planilha) return "Selecione uma planilha .xlsx.";
    if (!planilha.name.toLowerCase().endsWith(".xlsx")) return "Apenas arquivos .xlsx são aceitos.";
    return null;
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setValidationError(null);

    const error = validate();
    if (error) {
      setValidationError(error);
      return;
    }

    upload.mutate(
      { usuario, senha, planilha: planilha! },
      {
        onSuccess: (response) => {
          setSenha("");
          navigate(`/jobs/${response.jobId}`);
        },
      },
    );
  }

  const errorMessage =
    validationError ??
    (upload.error instanceof ApiError
      ? upload.error.message
      : upload.error
        ? "Falha ao enviar. Verifique a conexão com o servidor."
        : null);

  return (
    <div>
      <h1 className="page-title text-gradient">Enviar Planilha</h1>
      <p className="page-subtitle">
        Envie usuário, senha do portal DUAM e a planilha .xlsx para processamento assíncrono.
      </p>

      <form onSubmit={handleSubmit} className="card" style={{ maxWidth: 520, display: "flex", flexDirection: "column", gap: 18 }}>
        <div className="field">
          <label htmlFor="usuario">Usuário do portal DUAM</label>
          <input
            id="usuario"
            type="text"
            placeholder="usuario.portal"
            value={usuario}
            onChange={(e) => setUsuario(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="field">
          <label htmlFor="senha">Senha</label>
          <input
            id="senha"
            type="password"
            placeholder="••••••••"
            value={senha}
            onChange={(e) => setSenha(e.target.value)}
            autoComplete="current-password"
          />
          <span className="field-hint">Usada uma única vez no envio; não é armazenada.</span>
        </div>

        <div className="field">
          <label htmlFor="planilha">Planilha (.xlsx)</label>
          <div className="file-drop">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--muted-foreground))" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M14.5 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8.5L14.5 3z" />
              <path d="M14 3v6h6" />
            </svg>
            {planilha ? (
              <span style={{ fontSize: 13, fontWeight: 600 }}>{planilha.name}</span>
            ) : (
              <span style={{ fontSize: 13, color: "hsl(var(--muted-foreground))" }}>
                Arraste o arquivo ou clique para selecionar
              </span>
            )}
            <input
              id="planilha"
              type="file"
              accept=".xlsx"
              onChange={(e) => setPlanilha(e.target.files?.[0] ?? null)}
              style={{ fontSize: 12 }}
            />
          </div>
        </div>

        {errorMessage && <span className="field-error">{errorMessage}</span>}

        <button type="submit" disabled={upload.isPending} className="btn-primary" style={{ marginTop: 6 }}>
          {upload.isPending ? "Enviando..." : "Enviar planilha"}
        </button>
      </form>
    </div>
  );
}
