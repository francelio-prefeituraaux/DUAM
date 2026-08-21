# DUAM Monitor — Deploy manual

Guia único, passo a passo, pra colocar o sistema (API + frontend) no ar no servidor Ubuntu.

**Por que manual e não via GitHub Actions**: o servidor só é alcançável por VPN, sem IP público roteável. Um runner hospedado pela própria GitHub (`ubuntu-latest`) não tem rota até a VPN, então o deploy automático via SSH não funciona sem infraestrutura extra (self-hosted runner, cliente de VPN no runner, etc. — ver conversa/decisão registrada). Optamos por deploy manual até que valha a pena resolver isso.

Como o acesso é só por VPN, **todos os comandos abaixo assumem que você já está conectado na VPN** antes de rodar qualquer `ssh`/`scp`/`rsync`.

## Arquitetura

```
Você (VPN) ──SSH──► Servidor Ubuntu
                        │
                        ├─ Nginx (host, fora do Docker)
                        │    ├─ location /api/  → proxy_pass 127.0.0.1:8080
                        │    └─ location /      → arquivos estáticos em /var/www/duamapi-web
                        │
                        └─ Docker Compose
                             ├─ container "api"   (publica só em 127.0.0.1:8080)
                             └─ container "mysql" (sem porta publicada, só rede interna do compose)
```

MySQL fica em Docker (decisão registrada — mesma rede do compose que a API, sem precisar abrir porta pro host nem lidar com roteamento container→host).

## 1. Preparar o servidor (uma vez só)

Usuário dedicado pra deploy (não usar sua conta pessoal pra rodar os containers):

```bash
sudo adduser deploy
sudo usermod -aG sudo deploy
sudo usermod -aG docker deploy   # necessário pra rodar `docker compose` sem sudo
```

> Estar no grupo `docker` equivale a acesso root na máquina — só o usuário `deploy` deve ter isso, não contas de uso geral.

Firewall (se ainda não fez):

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

Docker (se ainda não fez):

```bash
curl -fsSL https://get.docker.com | sudo sh
```

Swap de segurança (evita OOM-kill em pico, mesmo com bastante RAM):

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## 2. Trazer o código pro servidor

O repositório é único (monorepo) — `francelio-prefeituraaux/DUAM` no GitHub, com a API e o frontend juntos. O servidor precisa da própria credencial pra clonar (deploy key, só leitura, separada da sua chave pessoal):

```bash
ssh-keygen -t ed25519 -C "deploy-key-duamapi-servidor" -f ~/.ssh/duamapi_deploy_key -N ""
cat ~/.ssh/duamapi_deploy_key.pub
```

Cadastre essa chave pública no repositório: **Settings → Deploy keys → Add deploy key** (deixe "Allow write access" desmarcado — só leitura já basta pra clonar/atualizar).

```bash
cat >> ~/.ssh/config <<'EOF'

Host github.com-duamapi
    HostName github.com
    User git
    IdentityFile ~/.ssh/duamapi_deploy_key
    IdentitiesOnly yes
EOF
```

```bash
sudo mkdir -p /opt/duamapi
sudo chown deploy:deploy /opt/duamapi
su - deploy
git clone git@github.com-duamapi:francelio-prefeituraaux/DUAM.git /opt/duamapi
cd /opt/duamapi/Automacao.DuamApi/Automacao.DuamApi
```

> A partir daqui, todo comando `docker compose`/`git pull` deste guia roda de dentro de `/opt/duamapi/Automacao.DuamApi/Automacao.DuamApi` (onde está o `docker-compose.yml` de produção), a menos que indicado o contrário.

## 3. Configurar o `.env` da API

```bash
cp .env.example .env
nano .env
```

Preencha:

```
MYSQL_ROOT_PASSWORD=<senha forte e única>
MYSQL_PASSWORD=<outra senha forte e única>
FRONTEND_ORIGIN=https://<domínio ou IP do frontend>
WORKER_COUNT=2
```

## 4. Subir API + MySQL

```bash
docker compose up -d --build
docker compose ps
```

Verificar que a API subiu de verdade (migrations do EF Core aplicam automaticamente no startup):

```bash
curl -i http://127.0.0.1:8080/swagger/v1/swagger.json
```

Deve responder `200`. Se não responder, veja os logs antes de seguir:

```bash
docker compose logs --tail=100 api
```

## 5. Build e deploy do frontend

O frontend é estático em produção (o Nginx do host só serve os arquivos), mas o **build roda no próprio servidor**, a partir do código já clonado — por isso precisa de Node.js instalado lá (único requisito extra além do Docker).

Instalar Node (uma vez só, se ainda não tiver):

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs
node -v && npm -v
```

Preparar a pasta que o Nginx vai servir (uma vez só):

```bash
sudo mkdir -p /var/www/duamapi-web
sudo chown deploy:deploy /var/www/duamapi-web
```

Configurar o script (uma vez só):

```bash
cd /opt/duamapi/Automacao.DuamApi.Web
cp deploy.env.example deploy.env.local
nano deploy.env.local   # preenche VITE_API_BASE_URL e TARGET_DIR
```

Buildar e copiar (script [deploy.sh](Automacao.DuamApi.Web/deploy.sh)):

```bash
./deploy.sh
```

Rode esse mesmo comando de novo sempre que quiser reenviar o frontend (roda `npm ci`, `npm run build` e copia `dist/` pra `TARGET_DIR`).

## 6. Nginx (proxy pra API + estáticos do frontend)

```bash
sudo apt install -y nginx
sudo nano /etc/nginx/sites-available/duamapi
```

```nginx
server {
    listen 80;
    server_name <seu-dominio-ou-ip>;
    client_max_body_size 25m;

    location /hangfire { deny all; return 404; }

    location /api/ {
        proxy_pass http://127.0.0.1:8080/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        root /var/www/duamapi-web;
        try_files $uri $uri/ /index.html;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/duamapi /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

## 7. HTTPS (opcional, se tiver domínio)

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d <seu-dominio>
```

## 8. Backup do MySQL (cron diário)

```bash
sudo mkdir -p /opt/duamapi/backups
sudo chown deploy:deploy /opt/duamapi/backups
crontab -e
```

```cron
0 3 * * * cd /opt/duamapi/Automacao.DuamApi/Automacao.DuamApi && docker compose exec -T mysql sh -c 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" duam_api' | gzip > /opt/duamapi/backups/duam_api_$(date +\%Y\%m\%d).sql.gz && find /opt/duamapi/backups -name '*.sql.gz' -mtime +14 -delete
```

## 9. Acessar o dashboard do Hangfire

Bloqueado no Nginx de propósito (`/hangfire` retorna 404). Acesse por túnel SSH:

```bash
ssh -L 8080:127.0.0.1:8080 deploy@<ip-do-servidor>
```

E abra `http://127.0.0.1:8080/hangfire` no seu navegador local.

## 10. Processo de atualização (toda vez que houver código novo)

### API

```bash
ssh deploy@<host-do-servidor>
cd /opt/duamapi

# Backup antes de aplicar qualquer migration nova
mkdir -p /opt/duamapi/backups
(cd Automacao.DuamApi/Automacao.DuamApi && docker compose exec -T mysql sh -c 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" duam_api') | gzip > /opt/duamapi/backups/pre-deploy_$(date +%Y%m%d%H%M%S).sql.gz

git pull origin master   # puxa API + frontend juntos, é um repo só
cd Automacao.DuamApi/Automacao.DuamApi
docker compose up -d --build

# Confirma que subiu antes de considerar concluído
curl -i http://127.0.0.1:8080/swagger/v1/swagger.json

docker image prune -f
```

Se o `curl` não responder `200`: **não** apague o backup que acabou de gerar — ele é a sua rede de segurança pra reverter a migration se necessário (`docker compose logs api` pra investigar antes de mais nada).

### Frontend

Mesma sessão SSH, já com o `git pull` feito acima (é o mesmo repositório, não precisa puxar de novo):

```bash
cd /opt/duamapi/Automacao.DuamApi.Web
./deploy.sh
```

## Checklist de segurança

- [ ] `.env` com senhas fortes, nunca commitado.
- [ ] Firewall só com 22/80/443 liberados.
- [ ] MySQL e a porta da API não publicadas além de `127.0.0.1` (já garantido pelo `docker-compose.yml`).
- [ ] `/hangfire` bloqueado no Nginx; acesso só via túnel SSH.
- [ ] Só o usuário `deploy` está no grupo `docker`.
- [ ] Acesso SSH ao servidor só por chave (`PasswordAuthentication no` em `/etc/ssh/sshd_config`).
- [ ] Backup testado pelo menos uma vez (restaurar de verdade, não só gerar o arquivo).
