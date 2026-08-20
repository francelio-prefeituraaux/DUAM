# Guia de Deploy — Ubuntu Server

> ⚠️ **Superado**: o servidor de produção só é acessível via VPN, sem IP público — um runner hospedado pela GitHub não alcança essa rede, então o deploy automatizado descrito aqui (e em [CI_CD.md](CI_CD.md)) não é usado. O guia atual, com o passo a passo manual (API + frontend), está no [README.md da raiz do repositório](../../README.md). Este arquivo fica só como referência histórica de como o provisionamento do servidor (Docker, Nginx, firewall) foi pensado originalmente.

Guia passo a passo para colocar o Automacao.DuamApi no ar em um servidor Ubuntu Server, usando Docker Compose (API + MySQL) com Nginx como proxy reverso na frente.

Especificações de referência usadas neste guia: **4 vCPUs, 10 GB RAM, 120 GB de disco** — confortável para esta carga (API .NET + MySQL + algumas instâncias de Chrome headless por vez).

## Sumário

- [Arquitetura da implantação](#arquitetura-da-implantação)
- [Passo 1 — Preparar o servidor](#passo-1--preparar-o-servidor)
- [Passo 2 — Instalar Docker](#passo-2--instalar-docker)
- [Passo 3 — Obter o código](#passo-3--obter-o-código)
- [Passo 4 — Configurar o `.env`](#passo-4--configurar-o-env)
- [Passo 5 — Subir os containers](#passo-5--subir-os-containers)
- [Passo 6 — Nginx como proxy reverso](#passo-6--nginx-como-proxy-reverso)
- [Passo 7 — HTTPS com Certbot (opcional)](#passo-7--https-com-certbot-opcional)
- [Acessar o dashboard do Hangfire com segurança](#acessar-o-dashboard-do-hangfire-com-segurança)
- [Backups do MySQL](#backups-do-mysql)
- [Atualizações](#atualizações)
- [Dimensionamento de recursos](#dimensionamento-de-recursos)
- [Checklist de segurança](#checklist-de-segurança)

## Arquitetura da implantação

```
Internet
   │  HTTPS (443) / HTTP (80)
   ▼
Nginx (no host, fora do Docker)
   │  proxy_pass → 127.0.0.1:8080
   ▼
Container "api" (publica só em 127.0.0.1:8080)
   │  ConnectionStrings__DuamMySql=Server=mysql;...
   ▼
Container "mysql" (sem porta publicada — só acessível pela rede interna do compose)
```

Nada além de SSH (22) e Nginx (80/443) fica exposto publicamente. O MySQL e a porta da API não são alcançáveis diretamente da internet.

## Passo 1 — Preparar o servidor

```bash
sudo apt update && sudo apt upgrade -y

# Usuário de deploy dedicado (não usar root no dia a dia)
sudo adduser deploy
sudo usermod -aG sudo deploy
su - deploy

# Firewall: só SSH, HTTP e HTTPS
sudo apt install -y ufw
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```

Swap de segurança (recomendado mesmo com 10 GB de RAM, evita OOM-kill em picos):

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## Passo 2 — Instalar Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
newgrp docker

# Confirma que o plugin "compose" (docker compose, sem hífen) está disponível
docker compose version
```

## Passo 3 — Obter o código

```bash
sudo mkdir -p /opt/duamapi
sudo chown $USER:$USER /opt/duamapi
cd /opt/duamapi

git clone <URL_DO_REPOSITORIO> .
cd Automacao.DuamApi
```

> Se o repositório for privado (recomendado — ver [CI_CD.md](CI_CD.md)), o servidor precisa de uma credencial própria para clonar/dar `git pull` (deploy key). Isso está detalhado em [CI_CD.md](CI_CD.md#passo-3--deploy-key-para-o-servidor-clonarpull).

## Passo 4 — Configurar o `.env`

```bash
cp .env.example .env
nano .env
```

Preencha:

```
MYSQL_ROOT_PASSWORD=<senha forte e única>
MYSQL_PASSWORD=<outra senha forte e única>
FRONTEND_ORIGIN=https://<domínio do frontend, se houver>
WORKER_COUNT=2
```

O `.env` **nunca** deve ser commitado (já está no `.gitignore` do repositório).

## Passo 5 — Subir os containers

```bash
docker compose up -d --build
docker compose ps
docker compose logs -f api
```

As migrações do EF Core (`jobs`, `resultados_linha`) e o schema do Hangfire são aplicados automaticamente na inicialização da API — não é preciso rodar `dotnet ef` manualmente no servidor.

Teste local no próprio servidor:

```bash
curl -i http://127.0.0.1:8080/swagger/v1/swagger.json
```

## Passo 6 — Nginx como proxy reverso

```bash
sudo apt install -y nginx
sudo nano /etc/nginx/sites-available/duamapi
```

```nginx
server {
    listen 80;
    server_name <seu-dominio-ou-ip>;

    # Planilhas .xlsx podem passar de 1MB (limite padrão do Nginx)
    client_max_body_size 25m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # NÃO expor o dashboard do Hangfire publicamente — ver seção dedicada abaixo.
    location /hangfire {
        deny all;
        return 404;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/duamapi /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

## Passo 7 — HTTPS com Certbot (opcional)

Só se você tiver um domínio apontando (registro DNS tipo A) para o IP do servidor:

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d <seu-dominio>
```

O Certbot ajusta o `server_name`/`listen 443` automaticamente e configura a renovação (`certbot renew`) via timer do systemd.

Sem domínio, a API fica acessível só por HTTP/IP — aceitável para uso interno na rede da prefeitura, mas evite trafegar credenciais do SIG por uma rede não confiável nesse cenário.

## Acessar o dashboard do Hangfire com segurança

O filtro de autorização do dashboard (`DuamHangfireDashboardAuthorizationFilter`) só libera acesso a partir de `localhost` — e o Passo 6 bloqueia `/hangfire` no Nginx de propósito, para não depender de cabeçalhos de proxy para essa checagem. Para acessar o dashboard, abra um túnel SSH a partir da sua máquina:

```bash
ssh -L 8080:127.0.0.1:8080 deploy@<ip-do-servidor>
```

E acesse `http://127.0.0.1:8080/hangfire` no seu navegador local.

## Backups do MySQL

```bash
sudo mkdir -p /opt/duamapi/backups
sudo crontab -e
```

Adicione (backup diário às 3h, mantendo os últimos 14 dias):

```cron
0 3 * * * cd /opt/duamapi/Automacao.DuamApi && docker compose exec -T mysql sh -c 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" duam_api' | gzip > /opt/duamapi/backups/duam_api_$(date +\%Y\%m\%d).sql.gz && find /opt/duamapi/backups -name '*.sql.gz' -mtime +14 -delete
```

Considere copiar os backups para fora do servidor periodicamente (rsync/scp para outra máquina, ou storage externo).

## Atualizações

Manual:

```bash
cd /opt/duamapi/Automacao.DuamApi
git pull
docker compose up -d --build
docker image prune -f
```

Automatizado via GitHub Actions: ver [CI_CD.md](CI_CD.md) — o mesmo comando acima roda automaticamente a cada push na branch principal.

## Dimensionamento de recursos

Com os limites definidos em [`docker-compose.yml`](docker-compose.yml) (MySQL 1,5 GB, API 2 GB) e o sistema operacional + Nginx + Docker (~1–1,5 GB), o uso fica bem abaixo dos 10 GB disponíveis, mesmo com `WORKER_COUNT=2` (até 2 sessões de Chrome headless simultâneas). Há espaço para subir `WORKER_COUNT` se necessário — com cautela, pois processar DUAMs em paralelo agressivo pode ser interpretado como abuso pelo sistema do SIG (ver item 3 de [MELHORIAS.md](MELHORIAS.md)).

Quanto ao disco: as planilhas `.xlsx` enviadas são mantidas indefinidamente (decisão registrada em [PLANO_ASYNC_MYSQL.md](PLANO_ASYNC_MYSQL.md)). Com 120 GB livres isso não é um problema no curto/médio prazo, mas vale monitorar `df -h` periodicamente e, no futuro, implementar o expurgo automático já listado como melhoria pendente.

## Checklist de segurança

- [ ] `.env` com senhas fortes e não versionado.
- [ ] Firewall (`ufw`) liberando só 22/80/443.
- [ ] MySQL e a porta da API **não** publicadas para fora de `127.0.0.1` (já configurado no `docker-compose.yml`).
- [ ] `/hangfire` bloqueado no Nginx; acesso só via túnel SSH.
- [ ] HTTPS configurado, se houver domínio.
- [ ] Backups do MySQL agendados e testados (restaurar um backup pelo menos uma vez para validar).
- [ ] Acesso SSH ao servidor só por chave (desabilitar login por senha em `/etc/ssh/sshd_config`: `PasswordAuthentication no`).
