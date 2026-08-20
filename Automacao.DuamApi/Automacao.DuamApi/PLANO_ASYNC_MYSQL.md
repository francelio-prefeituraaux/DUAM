# Plano de Implementação — Execução Assíncrona + Fila de Processamento + Persistência (MySQL)

Plano técnico para os itens **1 (Execução assíncrona e fila de processamento)** e **2 (Persistência)** de [MELHORIAS.md](MELHORIAS.md), combinados em uma única entrega: o endpoint passa a enfileirar o processamento em background e persistir jobs/resultados em **MySQL**.

Paralelismo entre workers (item 3) fica fora de escopo aqui — a arquitetura já é desenhada para permitir isso depois sem retrabalho (ver [Extensões futuras](#extensões-futuras)).

## Sumário

- [Decisões de design](#decisões-de-design)
- [Modelo de dados (MySQL)](#modelo-de-dados-mysql)
- [Arquitetura da fila](#arquitetura-da-fila)
- [Contrato da API (novo)](#contrato-da-api-novo)
- [Recuperação após restart](#recuperação-após-restart)
- [Pacotes NuGet necessários](#pacotes-nuget-necessários)
- [Fases de implementação](#fases-de-implementação)
- [Configuração (appsettings)](#configuração-appsettings)
- [Docker (Dockerfile + docker-compose)](#docker-dockerfile--docker-compose)
- [Plano de teste manual](#plano-de-teste-manual)
- [Extensões futuras](#extensões-futuras)

## Decisões de design

| Decisão | Escolha | Justificativa |
|---|---|---|
| Fila / processamento em background | **Hangfire** (`Hangfire.AspNetCore` + `Hangfire.MySqlStorage`), usando o próprio MySQL como storage da fila | Fila persistida em banco (sobrevive a restart da API), dashboard de monitoramento pronto (`/hangfire`), retry automático configurável — substitui a necessidade de um `Channel`/`BackgroundService` customizado. |
| Provider MySQL para EF Core | [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql) | Provider mais maduro e usado da comunidade para MySQL com EF Core. |
| Persistência de credenciais (usuário/senha) | Senha é passada como argumento do job do Hangfire, mas **criptografada com ASP.NET Core Data Protection** antes de enfileirar e descriptografada só no momento do login. Nunca aparece em texto plano no dashboard do Hangfire nem em log. | O Hangfire precisa serializar os argumentos do job para persisti-los no MySQL — diferente de uma fila em memória, não dá para simplesmente "não persistir" a senha. Cifrar é o meio-termo: a fila fica durável, mas a senha nunca fica em texto plano em disco/banco. |
| Retenção dos jobs no storage do Hangfire | Expiração agressiva de jobs concluídos (`SucceededJobExpirationTimeout` curto, ex.: 1 hora) | Minimiza o tempo em que a senha cifrada permanece nas tabelas internas do Hangfire, mesmo criptografada. |
| Chaves do Data Protection | Persistidas em disco (padrão do ASP.NET Core) para sobreviver a restart do processo na mesma máquina; se a API rodar em múltiplas instâncias/containers, configurar um key ring compartilhado (ex.: persistido no próprio MySQL). | Sem isso, um restart (ou uma segunda instância) não consegue descriptografar senhas cifradas por outra instância/execução anterior. |
| Consequência de restart durante o processamento | O Hangfire detecta automaticamente (heartbeat/distributed lock) que o servidor que processava o job caiu e **reenfileira o job automaticamente** para retry, dentro do limite de tentativas configurado. Combinado com o registro incremental de `resultados_linha`, o retry pode pular linhas já processadas com sucesso. Se as tentativas se esgotarem, o job fica `Failed` no Hangfire e refletimos isso como `jobs.status = Falhou`. | Elimina a necessidade de uma rotina manual de "marcar como Interrompido" — o próprio Hangfire cobre esse caso, com a vantagem extra de retomar do ponto onde parou em vez de simplesmente desistir. |
| Substitui ou mantém o endpoint síncrono atual? | **Substitui.** `POST /duam/processar` passa a ser assíncrono (retorna `202 Accepted` + `jobId`) em vez de manter as duas versões. | Manter os dois aumenta a complexidade sem benefício real; é uma quebra de contrato assumida e documentada no README. |
| Migração do schema MySQL | `dotnet ef migrations` + `Database.Migrate()` no startup (ambiente de desenvolvimento) para as tabelas de domínio (`jobs`, `resultados_linha`); o storage do Hangfire cria/migra suas próprias tabelas automaticamente na primeira execução. Em produção, gerar script SQL e aplicar via pipeline/DBA. | Evita depender de migração automática silenciosa em produção; as tabelas do Hangfire seguem o mecanismo próprio da lib. |
| Armazenamento do arquivo `.xlsx` enviado | Mantido em disco, path referenciado na tabela `jobs`. **O arquivo é mantido após o job concluir, tanto em sucesso quanto em falha.** Diretório configurável via `Armazenamento:DiretorioPlanilhas` (default `Path.GetTempPath()` como hoje, para dev local sem Docker). | Permite auditoria e reprocessamento manual posterior sem exigir reenvio do arquivo pelo cliente. Precisa ser configurável (não fixo no temp do SO) porque, em container, esse diretório precisa ser um volume Docker para o arquivo sobreviver à recriação do container — ver [Docker (Dockerfile + docker-compose)](#docker-dockerfile--docker-compose). |
| Ambiente de execução em container | Chrome/Chromium instalado na **mesma imagem** da API (Linux), com `chromedriver` Linux correspondente — Selenium continua controlando um browser local, como hoje. | Evita reescrever `DuamAutomationService` para usar `RemoteWebDriver` nesta fase. Rodar o browser em um container Selenium separado (`selenium/standalone-chrome`) fica registrado como possível evolução futura (ver [Extensões futuras](#extensões-futuras)). |

## Modelo de dados (MySQL)

### Tabela `jobs`

| Coluna | Tipo | Observações |
|---|---|---|
| `id` | `CHAR(36)` | PK, GUID gerado na criação do job. |
| `usuario` | `VARCHAR(100)` | Usuário do SIG que disparou o job (**sem senha**). |
| `nome_arquivo_original` | `VARCHAR(255)` | Nome do `.xlsx` enviado. |
| `caminho_arquivo_temp` | `VARCHAR(500)` | Caminho do arquivo temporário no disco. |
| `status` | `VARCHAR(20)` | `Pendente`, `EmProcessamento`, `Concluido`, `Falhou`, `Interrompido` (enum mapeado como string). |
| `total_linhas` | `INT NULL` | Preenchido após a leitura da planilha. |
| `linhas_processadas` | `INT NOT NULL DEFAULT 0` | Contador de progresso, atualizado linha a linha. |
| `mensagem_erro` | `TEXT NULL` | Erro de nível de job (ex.: falha de login, planilha corrompida). |
| `data_criacao` | `DATETIME` | Quando o job foi enfileirado. |
| `data_inicio` | `DATETIME NULL` | Quando o worker começou a processar. |
| `data_fim` | `DATETIME NULL` | Quando terminou (sucesso, falha ou interrupção). |

### Tabela `resultados_linha`

| Coluna | Tipo | Observações |
|---|---|---|
| `id` | `BIGINT AUTO_INCREMENT` | PK. |
| `job_id` | `CHAR(36)` | FK → `jobs.id`. |
| `linha` | `INT` | Número da linha na planilha. |
| `inscricao` | `VARCHAR(50)` | |
| `sucesso` | `TINYINT(1)` | |
| `mensagem` | `TEXT` | |
| `data_processamento` | `DATETIME` | Quando essa linha foi processada — permite acompanhar progresso em tempo real via `GET /duam/jobs/{id}`. |

Índice recomendado: `resultados_linha(job_id)` e `jobs(status, data_criacao)` para consultas de histórico/painel.

## Arquitetura da fila

```
POST /duam/processar
   │  valida entrada, salva planilha em temp (mantida em disco após o job — ver decisão acima)
   │  cria Job (status=Pendente) no MySQL
   │  senhaCriptografada = dataProtector.Protect(senha)
   │  BackgroundJob.Enqueue<IDuamJobProcessor>(p => p.ProcessarAsync(jobId, senhaCriptografada))
   ▼
202 Accepted { jobId, statusUrl: /duam/jobs/{jobId} }

Hangfire Server (AddHangfireServer(), WorkerCount configurável)
   │  invoca IDuamJobProcessor.ProcessarAsync(jobId, senhaCriptografada)
   │      senha = dataProtector.Unprotect(senhaCriptografada)
   │      job.Status = EmProcessamento; job.DataInicio ??= now; SaveChanges
   │      lê resultados_linha já persistidos com sucesso, para pular em caso de retry
   │      DuamAutomationService.ProcessarAsync(..., onLinhaProcessada: async r => {
   │          persistir ResultadoLinha; job.LinhasProcessadas++; SaveChanges
   │      })
   │      job.Status = Concluido | Falhou; job.DataFim = now; SaveChanges
   │      (arquivo NÃO é apagado — permanece em disco para auditoria/reprocessamento)
   ▼
GET /duam/jobs/{jobId} → status atual + progresso + resultados já persistidos
```

Pontos de atenção na implementação:

- `IDuamJobProcessor` é resolvido pelo `JobActivator`/DI do ASP.NET Core configurado junto com `AddHangfireServer()` — cada execução já ganha um escopo de DI novo, então `DbContext`/`DuamAutomationService` podem continuar `Scoped` normalmente, sem gerenciamento manual de `IServiceScope` como seria necessário com um `BackgroundService` customizado.
- Decorar `ProcessarAsync` com `[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 300 })]` (ou equivalente), para que falhas transitórias (queda de rede, timeout do Selenium) sejam reprocessadas automaticamente.
- Como um retry reexecuta o método do zero, `ProcessarAsync` deve, ao iniciar, checar em `resultados_linha` quais linhas do job já têm `sucesso = true` e pulá-las — assim um retry não relança DUAMs já lançados com sucesso.
- `DuamAutomationService.ProcessarAsync` precisa ser alterado para aceitar um callback/`Func<ResultadoLinha, Task>` (ou `IProgress<ResultadoLinha>`) chamado a cada linha processada, em vez de só devolver a lista completa no final — isso viabiliza tanto o progresso incremental quanto o "pular linha já processada" do retry.
- Proteger o dashboard do Hangfire (`/hangfire`) com um `IDashboardAuthorizationFilter` antes de expor em produção — por padrão o dashboard fica acessível sem autenticação.

## Contrato da API (novo)

### `POST /duam/processar`

Mesma entrada de hoje (`multipart/form-data`: `Usuario`, `Senha`, `Planilha`), mesmas validações (campos obrigatórios, extensão `.xlsx`).

**Resposta: `202 Accepted`**
```json
{
  "jobId": "b6a2e6d0-...-...",
  "status": "Pendente",
  "statusUrl": "/duam/jobs/b6a2e6d0-...-..."
}
```

### `GET /duam/jobs/{jobId}`

**Resposta: `200 OK`**
```json
{
  "jobId": "b6a2e6d0-...-...",
  "status": "EmProcessamento",
  "totalLinhas": 40,
  "linhasProcessadas": 12,
  "dataCriacao": "2026-08-03T10:00:00Z",
  "dataInicio": "2026-08-03T10:00:05Z",
  "dataFim": null,
  "mensagemErro": null,
  "resultados": [
    { "linha": 2, "inscricao": "123456", "sucesso": true, "mensagem": "Processado com sucesso" }
  ]
}
```
`404 Not Found` se o `jobId` não existir.

### `GET /duam/jobs` (histórico, opcional nesta fase)

Lista paginada de jobs, com filtros por `usuario`, `status` e intervalo de datas — útil para auditoria. Pode ser adiado para uma iteração seguinte sem bloquear o restante do plano.

## Recuperação após restart

Com Hangfire, isso passa a ser majoritariamente automático:

1. Jobs ainda não iniciados (`Enqueued`) no storage do Hangfire permanecem na fila normalmente após um restart da API — nada a fazer.
2. Jobs que estavam `Processing` quando o servidor caiu são detectados pelo próprio Hangfire (expiração do heartbeat/lock do worker) e **reenfileirados automaticamente** para nova tentativa, respeitando o `[AutomaticRetry]` configurado.
3. Graças ao registro incremental em `resultados_linha`, o retry retoma da linha seguinte à última processada com sucesso, em vez de recomeçar a planilha do zero.
4. Se as tentativas se esgotarem (job cai em `Failed` no Hangfire), um filtro do Hangfire (`IElectStateFilter`/`IApplyStateFilter`) deve refletir isso em `jobs.status = Falhou` com a mensagem de erro correspondente — a tabela `jobs` é a fonte de verdade exposta pela nossa API, então precisa ficar sincronizada com o estado final do Hangfire.
5. Ponto de atenção real: se as chaves do Data Protection não sobreviverem ao restart (ex.: troca de instância/container sem key ring compartilhado), a descriptografia da senha falha — o job cai como `Falhou` com uma mensagem clara, e o cliente precisa reenviar via `POST /duam/processar`. Ver linha "Chaves do Data Protection" na tabela de decisões.

## Pacotes NuGet necessários

```
Pomelo.EntityFrameworkCore.MySql
Microsoft.EntityFrameworkCore.Design      (para dotnet-ef, ferramenta de migração)
Hangfire.AspNetCore
Hangfire.MySqlStorage                     (storage do Hangfire sobre MySQL)
```

`Automacao.DuamApi.csproj` já tem `ClosedXML`, `Selenium.*` e `Swashbuckle.AspNetCore` — nenhuma alteração necessária neles. O Data Protection (`Microsoft.AspNetCore.DataProtection`) já vem com o SDK Web, sem pacote adicional.

## Fases de implementação

### Fase 0 — Infraestrutura de banco
- Definir string de conexão MySQL (local: container Docker `mysql:8`; produção: instância existente da Sefaz/prefeitura, a confirmar).
- Adicionar pacotes NuGet (seção acima).
- Criar `Data/DuamDbContext.cs` com `DbSet<Job>` e `DbSet<ResultadoLinhaEntity>`, mapeamento das tabelas/colunas descritas acima (via Fluent API em `OnModelCreating`).
- Gerar migração inicial: `dotnet ef migrations add InitialCreate`.

### Fase 1 — Modelos e camada de persistência
- Criar entidades `Job` e `ResultadoLinhaEntity` em `Models/` (distintas do atual `ResultadoLinha`, que continua sendo o DTO de retorno/uso interno do Selenium — ou unificá-los, a avaliar durante a implementação).
- Registrar `DbContext` em `Program.cs`:
  ```csharp
  builder.Services.AddDbContext<DuamDbContext>(options =>
      options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
  ```

### Fase 2 — Configurar Hangfire
- Adicionar `Hangfire.AspNetCore` + `Hangfire.MySqlStorage`; registrar em `Program.cs`:
  ```csharp
  builder.Services.AddHangfire(config => config
      .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions())));
  builder.Services.AddHangfireServer(options => options.WorkerCount = 2); // futuro: aumentar para paralelismo
  ```
- Expor o dashboard (`app.UseHangfireDashboard("/hangfire")`) atrás de um `IDashboardAuthorizationFilter` (ver item 6 de [MELHORIAS.md](MELHORIAS.md) — segurança).
- Configurar `SucceededJobExpirationTimeout` curto para reduzir o tempo que argumentos cifrados ficam no storage.
- Configurar `AddDataProtection()` com persistência de chaves adequada ao ambiente (disco local para instância única; key ring compartilhado se houver múltiplas instâncias).

### Fase 3 — Job de processamento (`IDuamJobProcessor`)
- Criar `IDuamJobProcessor`/`DuamJobProcessor`, com o método `ProcessarAsync(Guid jobId, string senhaCriptografada)` decorado com `[AutomaticRetry(...)]`, implementando o fluxo do diagrama da seção [Arquitetura da fila](#arquitetura-da-fila) (descriptografar senha, checar linhas já processadas, chamar `DuamAutomationService`, persistir progresso).
- Não é mais necessário um `BackgroundService`/`IHostedService` customizado para consumo de fila — o `AddHangfireServer()` cobre esse papel.

### Fase 4 — Ajustar `DuamAutomationService`
- Alterar `ProcessarAsync` para receber um callback de progresso por linha, permitindo persistência incremental em vez de só devolver a lista completa ao final.
- Lógica interna de automação Selenium **não muda** nesta fase (isso é o item 4 do backlog de melhorias).

### Fase 5 — Endpoints da API
- Reescrever `POST /duam/processar` para criar o `Job`, enfileirar e retornar `202`.
- Adicionar `GET /duam/jobs/{jobId}`.
- (Opcional nesta entrega) `GET /duam/jobs`.
- Atualizar `Automacao.DuamApi.http` e a descrição do Swagger com os novos contratos.

### Fase 6 — Documentação
- Atualizar [README.md](README.md) (seção "Uso da API") com o novo fluxo assíncrono e a existência do dashboard `/hangfire`.
- Marcar itens 1 e 2 como concluídos em [MELHORIAS.md](MELHORIAS.md).
- Documentar a política atual de retenção do arquivo `.xlsx` enviado (mantido indefinidamente em disco após o job) e sinalizar como possível melhoria futura a criação de um expurgo automático.

### Fase 7 — Containerização
- Tornar `Armazenamento:DiretorioPlanilhas` configurável em `DuamAutomationService`/no endpoint (hoje fixo em `Path.GetTempPath()`), com fallback para o valor atual quando não configurado.
- Criar `Dockerfile` (build multi-stage + Chrome/chromedriver Linux) e `docker-compose.yml` (api + mysql + volumes), conforme [Docker (Dockerfile + docker-compose)](#docker-dockerfile--docker-compose).
- Criar `.env.example` (sem valores reais) e adicionar `.env` ao `.gitignore`.
- Validar o fluxo completo com `docker compose up --build`, seguindo o [Plano de teste manual](#plano-de-teste-manual).

## Configuração (appsettings)

```json
{
  "ConnectionStrings": {
    "DuamMySql": "Server=localhost;Port=3306;Database=duam_api;User=duam_api;Password=__use_secret__;"
  },
  "FilaDuam": {
    "CapacidadeFila": 50
  },
  "Armazenamento": {
    "DiretorioPlanilhas": null
  }
}
```

> A senha da conexão MySQL não deve ficar em `appsettings.json` versionado — usar `dotnet user-secrets` em desenvolvimento e variável de ambiente/secret manager em produção. `Armazenamento:DiretorioPlanilhas` nulo/vazio mantém o comportamento atual (`Path.GetTempPath()`); em Docker, é sobrescrito via variável de ambiente apontando para o volume montado (ver seção seguinte).

## Docker (Dockerfile + docker-compose)

Objetivo: subir API + MySQL com um único `docker compose up`, com o Chrome necessário para o Selenium já embutido na imagem da API e com os dados (banco e planilhas) persistidos em volumes — coerente com a decisão de manter o `.xlsx` em disco após o job concluir.

### `Dockerfile`

```dockerfile
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Automacao.DuamApi.csproj .
RUN dotnet restore Automacao.DuamApi.csproj

COPY . .
RUN dotnet publish Automacao.DuamApi.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Google Chrome (headless) + dependências mínimas para o Selenium
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget gnupg unzip jq ca-certificates \
    && wget -q -O - https://dl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/google-chrome.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends google-chrome-stable \
    && rm -rf /var/lib/apt/lists/*

# chromedriver Linux compatível com a versão do Chrome recém-instalada
# (baixado em build-time via "Chrome for Testing" — evita fixar uma versão manualmente
# e divergir da versão real do Chrome instalado acima)
RUN CHROME_VERSION=$(google-chrome --version | grep -oP '\d+\.\d+\.\d+\.\d+') \
    && DRIVER_URL=$(wget -qO- "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json" \
         | jq -r --arg v "$CHROME_VERSION" '.versions[] | select(.version==$v) | .downloads.chromedriver[] | select(.platform=="linux64") | .url') \
    && wget -q -O /tmp/chromedriver.zip "$DRIVER_URL" \
    && unzip -q /tmp/chromedriver.zip -d /tmp \
    && mkdir -p /app/Drivers \
    && mv /tmp/chromedriver-linux64/chromedriver /app/Drivers/chromedriver \
    && chmod +x /app/Drivers/chromedriver \
    && rm -rf /tmp/chromedriver*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Automacao.DuamApi.dll"]
```

> `DuamAutomationService.cs` já resolve o driver via `Path.Combine(AppContext.BaseDirectory, "Drivers")` e já usa `--headless=new`, `--no-sandbox` e `--disable-dev-shm-usage` — nenhum ajuste de código é necessário para rodar em container; só precisa encontrar um `chromedriver` Linux (sem `.exe`) nessa pasta em vez do `chromedriver.exe` usado localmente no Windows.

### `docker-compose.yml`

```yaml
services:
  mysql:
    image: mysql:8.4
    restart: unless-stopped
    environment:
      MYSQL_DATABASE: duam_api
      MYSQL_USER: duam_api
      MYSQL_PASSWORD: ${MYSQL_PASSWORD:?informe MYSQL_PASSWORD no .env}
      MYSQL_ROOT_PASSWORD: ${MYSQL_ROOT_PASSWORD:?informe MYSQL_ROOT_PASSWORD no .env}
    ports:
      - "3306:3306"
    volumes:
      - mysql_data:/var/lib/mysql
    healthcheck:
      test: ["CMD", "mysqladmin", "ping", "-h", "localhost", "-u", "root", "-p${MYSQL_ROOT_PASSWORD}"]
      interval: 5s
      timeout: 5s
      retries: 10

  api:
    build:
      context: .
      dockerfile: Dockerfile
    restart: unless-stopped
    depends_on:
      mysql:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DuamMySql: "Server=mysql;Port=3306;Database=duam_api;User=duam_api;Password=${MYSQL_PASSWORD};"
      Armazenamento__DiretorioPlanilhas: /data/planilhas
    ports:
      - "8080:8080"
    volumes:
      - planilhas_data:/data/planilhas

volumes:
  mysql_data:
  planilhas_data:
```

### `.env` (não versionado — adicionar ao `.gitignore`)

```
MYSQL_ROOT_PASSWORD=troque-esta-senha
MYSQL_PASSWORD=troque-esta-senha-tambem
```

### Observações

- `depends_on: condition: service_healthy` garante que a API só sobe depois do MySQL responder — evita falha de conexão na inicialização/migração.
- `mysql_data` e `planilhas_data` são volumes nomeados: sobrevivem a `docker compose down` (sem `-v`) e a rebuilds da imagem da API, preservando o histórico de jobs e os arquivos `.xlsx` mantidos após conclusão.
- O dashboard do Hangfire (`/hangfire`) fica acessível em `http://localhost:8080/hangfire` — lembrar de aplicar o `IDashboardAuthorizationFilter` (Fase 2) antes de expor essa porta fora da rede local.
- Para desenvolvimento sem Docker (Windows, como hoje), nada muda: `dotnet run` continua usando `Drivers/chromedriver.exe` e `Path.GetTempPath()` por padrão.

## Plano de teste manual

1. Copiar `.env.example` para `.env`, preencher as senhas, e subir tudo com `docker compose up --build` — validar que o MySQL fica saudável e a API sobe sem erro de conexão.
2. Aplicar a migração das tabelas de domínio (`jobs`, `resultados_linha`) contra o MySQL do compose; confirmar que o Hangfire cria suas próprias tabelas de storage na primeira execução.
3. `POST /duam/processar` com uma planilha pequena (2–3 linhas) → validar retorno `202` com `jobId`.
4. Consultar `GET /duam/jobs/{jobId}` repetidamente → validar transição `Pendente` → `EmProcessamento` (com `linhasProcessadas` incrementando) → `Concluido`.
5. Acessar o dashboard `/hangfire` e conferir que o job aparece com o estado correspondente (`Processing`/`Succeeded`).
6. Conferir no MySQL que `jobs` e `resultados_linha` foram gravados corretamente e que o arquivo `.xlsx` (`caminho_arquivo_temp`) continua presente no volume `planilhas_data` após a conclusão.
7. Forçar um erro proposital (ex.: senha inválida) → validar `status = Falhou` e `mensagemErro` preenchido.
8. Derrubar o container da API no meio de um processamento (`docker compose restart api`) → validar que o Hangfire reenfileira o job automaticamente e que o retry pula as linhas já concluídas com sucesso antes da queda.
9. Rodar `docker compose down` (sem `-v`) e subir de novo → confirmar que os dados de `jobs`/`resultados_linha` e os arquivos `.xlsx` continuam disponíveis (volumes preservados).
10. Enviar planilha com extensão inválida / sem usuário / sem senha → validar que as respostas de erro `400` continuam iguais às de hoje (isso não muda).

## Extensões futuras

- **Paralelismo (item 3 do MELHORIAS.md)**: com Hangfire, basta aumentar `WorkerCount` em `AddHangfireServer()` para processar múltiplos jobs simultaneamente — não é mais necessário implementar um semáforo/consumidor customizado. Ainda assim, vale limitar esse número com cuidado para não sobrecarregar o SIG/Prodataweb (mesma ressalva já registrada em MELHORIAS.md).
- **Múltiplas instâncias da API**: como a fila já é persistida no MySQL via Hangfire, rodar mais de uma instância da API (cada uma com seu próprio `AddHangfireServer()`) já funciona nativamente para escalar o processamento — só é preciso garantir o key ring compartilhado do Data Protection (ver tabela de decisões) para que qualquer instância consiga descriptografar a senha de qualquer job.
- **Notificação de conclusão** (webhook/e-mail) ao terminar um job, evitando polling constante do cliente em `GET /duam/jobs/{id}`.
- **Expurgo do arquivo `.xlsx` enviado**: rotina periódica (ex.: um recurring job do próprio Hangfire) para apagar arquivos de jobs concluídos há mais de N dias, já que a decisão atual é mantê-los indefinidamente após a conclusão.
- **Selenium Grid separado**: mover o Chrome para um container dedicado (`selenium/standalone-chrome` ou `selenium/hub` + `selenium/node-chrome` para múltiplos nós) e trocar `ChromeDriver` local por `RemoteWebDriver` na API — desacopla o ciclo de vida do browser do ciclo de vida da API e facilita escalar o paralelismo (item 3) adicionando mais nós, em vez de instalar mais Chrome dentro da própria imagem da API.
