# Deploy — Automacao.DuamApi.Web

> ⚠️ **Superado**: o deploy via GitHub Actions descrito aqui (`.github/workflows/ci-cd.yml`) não é usado — o servidor só é acessível por VPN, sem IP público, e um runner hospedado pela GitHub não alcança essa rede. O workflow foi desativado (renomeado pra `ci-cd.yml.disabled`). O processo atual é manual, documentado no [README.md da raiz do repositório](../README.md).

Frontend estático (build do Vite), servido pelo **mesmo Nginx do host** já documentado no [DEPLOY.md do Automacao.DuamApi](../Automacao.DuamApi/Automacao.DuamApi/DEPLOY.md) — não sobe container próprio em produção. O `Dockerfile`/`docker-compose.yml` deste repositório servem só para rodar tudo junto localmente em desenvolvimento.

## Arquitetura

```
Internet
   │  HTTPS (443) / HTTP (80)
   ▼
Nginx (no host)
   ├─ location /api/  → proxy_pass 127.0.0.1:8080  (Automacao.DuamApi)
   └─ location /      → arquivos estáticos em /var/www/duamapi-web  (este projeto)
```

Se o frontend ficar num domínio/subdomínio separado da API, use um `server_name` próprio em vez de dividir por `location` — ajuste conforme o DNS real usado.

## CI/CD ([.github/workflows/ci-cd.yml](.github/workflows/ci-cd.yml))

Mesmo formato do backend (`build` → `docker-build-check` → `deploy`, só roda em push na `master`), adaptado:

1. `build` — `npm ci` + `npm run build` (typecheck + Vite), publica `dist/` como artefato do workflow.
2. `docker-build-check` — só valida que o `Dockerfile` local ainda builda (usado em dev via `docker compose`, não em produção).
3. `deploy` — baixa o artefato, limpa `/var/www/duamapi-web` no servidor via SSH e copia os arquivos novos via SCP.

### Variáveis e Secrets necessários no GitHub

| Nome | Tipo | Valor |
|---|---|---|
| `VITE_API_BASE_URL` | Variable (`vars`) | URL pública da API em produção (ex.: `https://seu-dominio/api`). É embutida no build estático — mudar exige rebuildar e redeployar. |
| `SSH_HOST` | Secret | IP/domínio do servidor (mesmo do backend, se for o mesmo host) |
| `SSH_USER` | Secret | usuário de deploy (ex.: `deploy`) |
| `SSH_PRIVATE_KEY` | Secret | chave privada dedicada ao CI (não reaproveitar a pessoal) |
| `SSH_PORT` | Secret (opcional) | só se não for a 22 |

Siga os mesmos Passos 4 e 5 do [CI_CD.md do backend](../Automacao.DuamApi/Automacao.DuamApi/CI_CD.md) para gerar a chave e cadastrar os secrets — pode reusar a mesma chave/usuário se for o mesmo servidor, ou gerar um par dedicado se preferir isolar o acesso.

## Passo único de preparação no servidor (antes do primeiro deploy)

```bash
sudo mkdir -p /var/www/duamapi-web
sudo chown deploy:deploy /var/www/duamapi-web
```

Bloco do Nginx (`/etc/nginx/sites-available/duamapi`, mesmo arquivo do backend — adicionar antes do `location /` da API):

```nginx
location / {
    root /var/www/duamapi-web;
    try_files $uri $uri/ /index.html;  # fallback de SPA (React Router)
}
```

```bash
sudo nginx -t
sudo systemctl reload nginx
```

## Importante: CORS

A API só aceita requisições da origem configurada em `Cors:AllowedOrigins` ([Program.cs](../Automacao.DuamApi/Automacao.DuamApi/Program.cs) do backend, variável `FRONTEND_ORIGIN` no `.env` dele). Ao definir o domínio final deste frontend, atualize essa variável no `.env` do backend e reinicie o container `api` — senão o navegador bloqueia as chamadas por CORS mesmo com tudo no ar.
