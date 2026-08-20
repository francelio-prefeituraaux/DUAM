# CI/CD via GitHub Actions

> ⚠️ **Superado**: descartamos o deploy automatizado via GitHub Actions — o servidor só é acessível por VPN (sem IP público), e um runner hospedado pela GitHub não tem rota até ele (ver decisão registrada no histórico do projeto). O processo atual é manual, documentado no [README.md da raiz do repositório](../../README.md). Este arquivo fica só como referência, caso um runner self-hosted dentro da VPN venha a ser configurado no futuro — nesse caso o `.github/workflows/ci-cd.yml` da API precisaria ser recriado (foi removido junto com o `.git` antigo deste projeto).

Guia para migrar o repositório do Azure DevOps para o GitHub e automatizar build + deploy no servidor Ubuntu (ver [DEPLOY.md](DEPLOY.md)) via GitHub Actions.

## Situação atual e decisão

Hoje o `origin` deste repositório aponta para o Azure DevOps (`dev.azure.com/coliseuio/PortalPrefeituraAraguaina`), **não** para o GitHub — por isso GitHub Actions não roda automaticamente sobre o código como ele está agora.

> ⚠️ **Nota de segurança**: a URL do remote atual (`git remote -v`) contém um token de acesso (PAT) do Azure DevOps embutido em texto puro. Depois de migrar para o GitHub, **revogue esse PAT no Azure DevOps** (User settings → Personal access tokens) — ele fica salvo em `.git/config` e não deve continuar válido sem necessidade.

Decisão adotada: **migração definitiva** — o GitHub passa a ser a única origem do código; o Azure DevOps deixa de ser usado.

O workflow já foi criado em [`.github/workflows/ci-cd.yml`](../.github/workflows/ci-cd.yml) (na raiz do repositório) com três jobs:

1. `build` — restaura e compila o projeto .NET em toda branch/PR.
2. `docker-build-check` — valida que o `Dockerfile` ainda builda (Chrome + chromedriver incluídos), sem publicar a imagem.
3. `deploy` — só roda em push na branch `master`, depois que os dois jobs acima passarem: conecta via SSH no servidor e executa `git pull` + `docker compose up -d --build`.

## Passo 1 — Criar o repositório no GitHub

1. Acesse `https://github.com/new`.
2. Nome sugerido: `Automacao.DuamApi` (ou o que preferir).
3. Visibilidade: **Privado** (é um sistema interno da prefeitura).
4. **Não** inicialize com README/gitignore/license — o repositório local já tem tudo isso.
5. Crie e copie a URL (SSH, ex.: `git@github.com:<sua-conta>/Automacao.DuamApi.git`).

## Passo 2 — Trocar o remote e enviar o código

A partir da raiz do repositório local (`C:\Projetos\Sefaz\Nova pasta\Automacao.DuamApi`):

```bash
# Guarda o remote antigo com outro nome, só por precaução, e adiciona o do GitHub
git remote rename origin azuredevops-antigo
git remote add origin git@github.com:<sua-conta>/Automacao.DuamApi.git

# Confirma
git remote -v

# Envia todas as branches e tags
git push -u origin master
git push origin feat/SECTI-6   # ou a branch em que você estiver trabalhando
git push origin --tags
```

Depois de confirmar que está tudo certo no GitHub:

```bash
git remote remove azuredevops-antigo
```

> Este passo (`git push`) é quem efetivamente publica o código — confira a URL do repositório antes de rodar.

## Passo 3 — Deploy key para o servidor clonar/pull

Como o repositório é privado, o **servidor** também precisa de credencial própria para `git clone`/`git pull` (isso é separado da chave que o GitHub Actions usa para entrar no servidor — ver Passo 4).

No servidor, como o usuário `deploy`:

```bash
ssh-keygen -t ed25519 -C "deploy-key-duamapi-servidor" -f ~/.ssh/duamapi_deploy_key -N ""
cat ~/.ssh/duamapi_deploy_key.pub
```

No GitHub: **Settings do repositório → Deploy keys → Add deploy key**, cole a chave pública, deixe **sem** permissão de escrita (read-only).

Configure o Git no servidor para usar essa chave só para este repositório:

```bash
cat >> ~/.ssh/config <<'EOF'
Host github.com-duamapi
    HostName github.com
    User git
    IdentityFile ~/.ssh/duamapi_deploy_key
    IdentitiesOnly yes
EOF
```

E clone usando esse host alternativo:

```bash
git clone git@github.com-duamapi:<sua-conta>/Automacao.DuamApi.git /opt/duamapi
```

(Se o repositório já tiver sido clonado com outra URL antes de configurar a deploy key, ajuste com `git remote set-url origin git@github.com-duamapi:<sua-conta>/Automacao.DuamApi.git`.)

## Passo 4 — Chave SSH para o GitHub Actions acessar o servidor

Gere um par **dedicado ao CI** (não reaproveite sua chave pessoal nem a deploy key do Passo 3) — pode ser na sua máquina local:

```bash
ssh-keygen -t ed25519 -C "github-actions-duamapi" -f ./gh_actions_duamapi -N ""
```

- Chave **pública** (`gh_actions_duamapi.pub`) → cole em `~/.ssh/authorized_keys` do usuário `deploy` no servidor.
- Chave **privada** (`gh_actions_duamapi`) → vai para os Secrets do GitHub (Passo 5) e depois é apagada da sua máquina.

```bash
# no servidor, como usuário deploy
echo "<conteúdo da chave pública>" >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
```

## Passo 5 — Cadastrar os Secrets no GitHub

No repositório: **Settings → Secrets and variables → Actions → New repository secret**.

| Secret | Valor |
|---|---|
| `SSH_HOST` | IP ou domínio do servidor |
| `SSH_USER` | `deploy` |
| `SSH_PRIVATE_KEY` | Conteúdo completo da chave **privada** gerada no Passo 4 |
| `SSH_PORT` | Só se a porta SSH não for a 22 |

## Passo 6 — (Opcional) Exigir aprovação manual antes do deploy

O job `deploy` do workflow usa `environment: production`. Em **Settings → Environments → New environment → `production`**, você pode marcar **Required reviewers** — assim, todo deploy fica pendente até alguém aprovar manualmente na aba Actions, mesmo que o push já tenha sido feito.

## Fluxo resultante

```
git push (branch feature) ──► build + docker-build-check
                                (feedback rápido em toda branch/PR)

git push/merge → master ──► build + docker-build-check
                                        │ (se passarem)
                                        ▼
                              deploy (SSH) ──► servidor:
                                                 git pull
                                                 docker compose up -d --build
                                                 docker image prune -f
```

Downtime esperado por deploy: poucos segundos (só o container `api` é recriado; o `mysql` permanece de pé).

## Evolução futura (opcional): build no CI + registry

Hoje o `deploy` builda a imagem **no próprio servidor** (`docker compose up -d --build`), reaproveitando os mesmos passos do [DEPLOY.md](DEPLOY.md). É simples e suficiente para este projeto. Se o build passar a demorar demais ou competir por CPU/RAM com a API em produção, a evolução natural é:

1. `docker/build-push-action` builda a imagem dentro do GitHub Actions e publica no GitHub Container Registry (`ghcr.io`).
2. O passo de deploy no servidor troca `docker compose up -d --build` por `docker compose pull && docker compose up -d` — sem gastar CPU do servidor compilando.

Isso exige um `docker-compose.yml` de produção separado (referenciando `image: ghcr.io/...` em vez de `build:`) e um `docker login ghcr.io` prévio no servidor. Vale a pena revisitar se o servidor começar a sentir o build a cada deploy — não é necessário agora.
