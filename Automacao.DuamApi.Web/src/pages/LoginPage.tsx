import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useLogin } from "../hooks/useLogin";
import { useLoginCaptcha } from "../hooks/useLoginCaptcha";
import { ApiError } from "../api/duamApi";

function MonitorIcon() {
  return (
    <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="3" width="20" height="14" rx="2" />
      <path d="M8 21h8" />
      <path d="M12 17v4" />
    </svg>
  );
}

function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <div style={{ textAlign: "center" }}>
      <div className="login-icon-badge">
        <MonitorIcon />
      </div>
      <div className="sidebar-brand-name text-gradient" style={{ fontSize: compact ? 24 : 30 }}>
        DUAM Monitor
      </div>
      <div style={{ fontSize: 13, color: compact ? "hsl(var(--muted-foreground))" : "hsl(215 20% 80%)" }}>
        SIG Prodataweb · Araguaína/TO
      </div>
    </div>
  );
}

export function LoginPage() {
  const navigate = useNavigate();
  const login = useLogin();
  const captcha = useLoginCaptcha();

  const [usuario, setUsuario] = useState("");
  const [senha, setSenha] = useState("");
  const [codigoCaptcha, setCodigoCaptcha] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const captchaNecessario = captcha.data?.captchaNecessario ?? false;

  function validate(): string | null {
    if (!usuario.trim()) return "Usuário é obrigatório.";
    if (!senha.trim()) return "Senha é obrigatória.";
    if (captchaNecessario && !codigoCaptcha.trim()) return "Código de verificação é obrigatório.";
    return null;
  }

  function recarregarCaptcha() {
    setCodigoCaptcha("");
    captcha.refetch();
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setValidationError(null);

    const error = validate();
    if (error) {
      setValidationError(error);
      return;
    }

    login.mutate(
      {
        usuario,
        senha,
        captchaToken: captchaNecessario ? (captcha.data?.token ?? undefined) : undefined,
        captchaCodigo: captchaNecessario ? codigoCaptcha : undefined,
      },
      {
        onSuccess: () => {
          setSenha("");
          navigate("/");
        },
        onError: () => {
          if (captchaNecessario) recarregarCaptcha();
        },
      },
    );
  }

  const errorMessage =
    validationError ??
    (login.error instanceof ApiError
      ? login.error.message
      : login.error
        ? "Falha ao conectar. Verifique a conexão com o servidor."
        : captcha.isError
          ? "Não foi possível carregar o código de verificação. Verifique a conexão com o servidor."
          : null);

  return (
    <div className="login-shell">
      <div className="login-panel-left">
        <svg className="login-panel-shapes" viewBox="0 0 500 800" preserveAspectRatio="xMidYMid slice" width="100%" height="100%">
          <circle cx="420" cy="90" r="180" fill="currentColor" fillOpacity="0.12" />
          <circle cx="70" cy="660" r="150" fill="currentColor" fillOpacity="0.1" />
          <circle cx="110" cy="260" r="55" fill="none" stroke="currentColor" strokeOpacity="0.18" strokeWidth="2" />
        </svg>

        <div className="login-panel-content">
          <Brand />
          <p className="login-panel-tagline">
            Envie planilhas de DUAM e acompanhe o processamento em tempo real, com segurança e agilidade.
          </p>
        </div>
      </div>

      <div className="login-panel-right">
        <div className="login-mobile-brand">
          <Brand compact />
        </div>

        <div style={{ width: "100%", maxWidth: 380 }}>
          <form
            onSubmit={handleSubmit}
            className="card login-card"
            style={{ width: "100%", boxSizing: "border-box", display: "flex", flexDirection: "column", gap: 18 }}
          >
            <div className="field">
              <label htmlFor="usuario">Usuário do portal DUAM</label>
              <div className="field-input-wrap minimal">
                <input
                  id="usuario"
                  type="text"
                  placeholder="usuario.portal"
                  value={usuario}
                  onChange={(e) => setUsuario(e.target.value)}
                  autoComplete="username"
                />
                <span className="field-input-icon right">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="2" y="4" width="20" height="16" rx="2" />
                    <path d="m2 6 10 7 10-7" />
                  </svg>
                </span>
              </div>
            </div>

            <div className="field">
              <label htmlFor="senha">Senha</label>
              <div className="field-input-wrap minimal">
                <input
                  id="senha"
                  type={showPassword ? "text" : "password"}
                  placeholder="••••••••"
                  value={senha}
                  onChange={(e) => setSenha(e.target.value)}
                  autoComplete="current-password"
                />
                <button
                  type="button"
                  className="field-password-toggle"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
                >
                  {showPassword ? (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  ) : (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M17.94 17.94A10.94 10.94 0 0 1 12 19c-6.5 0-10-7-10-7a18.5 18.5 0 0 1 4.22-5.06M9.9 4.24A9.12 9.12 0 0 1 12 4c6.5 0 10 7 10 7a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
                      <path d="M1 1l22 22" />
                    </svg>
                  )}
                </button>
              </div>
            </div>

            {captchaNecessario && (
              <div className="field">
                <label htmlFor="captchaCodigo">Código de verificação</label>
                <div className="captcha-wrapper">
                  <div className="captcha-box">
                    {captcha.data?.imagemBase64 ? (
                      <img
                        src={`data:image/png;base64,${captcha.data.imagemBase64}`}
                        alt="Código de verificação"
                        className="captcha-img"
                      />
                    ) : (
                      <div className="captcha-img captcha-img-loading" />
                    )}
                    <button
                      type="button"
                      className="captcha-refresh"
                      onClick={recarregarCaptcha}
                      title="Gerar novo código"
                      aria-label="Gerar novo código"
                    >
                      ↻
                    </button>
                  </div>
                  <div className="field-input-wrap minimal">
                    <input
                      id="captchaCodigo"
                      type="text"
                      placeholder="Digite o código da imagem"
                      value={codigoCaptcha}
                      onChange={(e) => setCodigoCaptcha(e.target.value)}
                      autoComplete="off"
                      autoCapitalize="off"
                      spellCheck={false}
                    />
                  </div>
                </div>
              </div>
            )}

            {errorMessage && (
              <div className="field-error-box" role="alert">
                {errorMessage}
              </div>
            )}

            <button
              type="submit"
              disabled={login.isPending || captcha.isLoading}
              className="btn-primary"
              style={{ marginTop: 6 }}
            >
              {login.isPending && <span className="btn-spinner" />}
              {login.isPending ? "Entrando..." : "Entrar"}
            </button>
          </form>

          <p className="login-support-note" style={{ marginTop: 20 }}>
            Esqueceu sua senha ou precisa de ajuda? Entre em contato com o suporte técnico.
          </p>
        </div>
      </div>
    </div>
  );
}
