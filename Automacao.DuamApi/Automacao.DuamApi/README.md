# Automacao.DuamApi

API em ASP.NET Core (Minimal API) que automatiza o lançamento de DUAMs (Documento Único de Arrecadação Municipal) no sistema SIG da Prodataweb (Araguaína/TO), a partir de uma planilha Excel. A automação do preenchimento do formulário web é feita via Selenium WebDriver (Chrome headless). O processamento é assíncrono (fila via Hangfire) e os jobs/resultados são persistidos em MySQL.

## Sumário

- [Visão geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Pré-requisitos](#pré-requisitos)
- [Como rodar o projeto](#como-rodar-o-projeto)
- [Uso da API](#uso-da-api)
- [Formato da planilha de entrada](#formato-da-planilha-de-entrada)
- [Configuração](#configuração)
- [Observações e limitações](#observações-e-limitações)

## Visão geral

O projeto expõe endpoints HTTP que recebem:

- Usuário e senha de acesso ao sistema SIG (Prodataweb);
- Uma planilha `.xlsx` com uma linha por DUAM a ser lançado.

O `POST /duam/processar` enfileira o processamento (via [Hangfire](https://www.hangfire.io/)) e retorna imediatamente um `jobId`. Em background, a API abre uma sessão de navegador Chrome (headless), efetua login no SIG e, para cada linha da planilha, navega até a tela de lançamento de DUAM e preenche automaticamente os campos do formulário (data de vencimento, ano, mês, receita, observação, inscrição e valor), salvando o registro. O progresso e o resultado linha a linha ficam disponíveis a qualquer momento via `GET /duam/jobs/{jobId}`.

## Arquitetura

- **Tipo de aplicação**: API HTTP (ASP.NET Core Minimal API) com processamento assíncrono em background e persistência em MySQL.
- **Padrão**: endpoints enfileiram trabalho (`IBackgroundJobClient`), um `IDuamJobProcessor` executado pelo Hangfire Server orquestra a automação (`DuamAutomationService`) e persiste o progresso via EF Core.
- **Fila / processamento em background**: [Hangfire](https://www.hangfire.io/) (`Hangfire.AspNetCore` + `Hangfire.Storage.MySql`), com a fila persistida no próprio MySQL — sobrevive a restart da API e reenfileira jobs interrompidos automaticamente (retry configurável). Dashboard de monitoramento em `/hangfire`.
- **Persistência**: [EF Core](https://learn.microsoft.com/ef/core/) + [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql), tabelas `jobs` e `resultados_linha` (ver [`Data/DuamDbContext.cs`](Data/DuamDbContext.cs)).
- **Credenciais do SIG**: a senha nunca é persistida em texto plano — é criptografada com [ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction) antes de ser enfileirada como argumento do job, e descriptografada só no momento do login.
- **Automação web**: [Selenium WebDriver](https://www.selenium.dev/) controlando o Google Chrome em modo headless via `ChromeDriver`.
- **Leitura de planilha**: [ClosedXML](https://github.com/ClosedXML/ClosedXML) para ler o arquivo `.xlsx` enviado.
- **Documentação/teste da API**: Swagger/OpenAPI ([Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)), exposto na raiz (`/`) da aplicação.

### Fluxo de execução

```
Cliente HTTP
   │  POST /duam/processar (multipart/form-data: usuario, senha, planilha)
   ▼
Program.cs (Minimal API)
   │  valida campos obrigatórios e extensão .xlsx
   │  salva a planilha em disco (mantida após o job concluir)
   │  cria o Job (status=Pendente) no MySQL
   │  criptografa a senha (Data Protection) e enfileira o job (Hangfire)
   ▼
202 Accepted { jobId, statusUrl }

Hangfire Server (background)
   │  IDuamJobProcessor.ProcessarAsync(jobId, senhaCriptografada)
   │      descriptografa a senha; pula linhas já processadas com sucesso (retry)
   │      DuamAutomationService.ProcessarAsync(...) → Selenium, linha a linha
   │      persiste cada ResultadoLinha e o progresso do Job (MySQL)
   ▼
GET /duam/jobs/{jobId} → status, progresso e resultados (parciais ou finais)
```

### Componentes principais

| Componente | Responsabilidade |
|---|---|
| [`Program.cs`](Program.cs) | Bootstrap da aplicação, configuração de EF Core/Hangfire/Data Protection/Swagger e definição dos endpoints. |
| [`Models/DuamRequest.cs`](Models/DuamRequest.cs) | DTO de entrada do endpoint (usuário, senha, arquivo da planilha). |
| [`Models/ResultadoLinha.cs`](Models/ResultadoLinha.cs) | DTO usado internamente pelo Selenium ao processar cada linha da planilha. |
| [`Models/Job.cs`](Models/Job.cs) | Entidade persistida representando um job de processamento. |
| [`Models/ResultadoLinhaRegistro.cs`](Models/ResultadoLinhaRegistro.cs) | Entidade persistida com o resultado de cada linha processada. |
| [`Models/JobResponses.cs`](Models/JobResponses.cs) | DTOs de resposta dos endpoints `GET /duam/jobs*`. |
| [`Data/DuamDbContext.cs`](Data/DuamDbContext.cs) | `DbContext` do EF Core (mapeamento das tabelas `jobs` e `resultados_linha`). |
| [`Data/Migrations/`](Data/Migrations) | Migrações do EF Core. |
| [`Services/DuamAutomationService.cs`](Services/DuamAutomationService.cs) | Lógica de automação: login no SIG e preenchimento do formulário de DUAM via Selenium, leitura da planilha via ClosedXML, com callback de progresso por linha. |
| [`Services/DuamJobProcessor.cs`](Services/DuamJobProcessor.cs) | Job do Hangfire: descriptografa a senha, orquestra `DuamAutomationService` e persiste progresso/resultado. |
| [`Services/SincronizarStatusJobFalhoFilter.cs`](Services/SincronizarStatusJobFalhoFilter.cs) | Filtro global do Hangfire: reflete no MySQL quando um job esgota as tentativas automáticas (`Falhou`). |
| [`Services/DuamHangfireDashboardAuthorizationFilter.cs`](Services/DuamHangfireDashboardAuthorizationFilter.cs) | Autorização mínima do dashboard `/hangfire` (só localhost — ver [Observações e limitações](#observações-e-limitações)). |
| `Drivers/` | Executável do ChromeDriver (Windows local) e licenças; em Docker, o binário Linux é baixado no build da imagem. |

## Estrutura do projeto

```
Automacao.DuamApi/
├── Data/
│   ├── DuamDbContext.cs           # DbContext (EF Core)
│   ├── DuamDbContextFactory.cs    # Factory de design-time (dotnet ef migrations)
│   └── Migrations/                # Migrações do EF Core
├── Drivers/                       # ChromeDriver (Windows) e licenças
├── Models/
│   ├── ArmazenamentoOptions.cs
│   ├── DuamRequest.cs
│   ├── Job.cs
│   ├── JobResponses.cs
│   ├── ResultadoLinha.cs
│   └── ResultadoLinhaRegistro.cs
├── Services/
│   ├── DuamAutomationService.cs
│   ├── DuamHangfireDashboardAuthorizationFilter.cs
│   ├── DuamJobProcessor.cs
│   ├── IDuamJobProcessor.cs
│   └── SincronizarStatusJobFalhoFilter.cs
├── Properties/
│   └── launchSettings.json        # Perfis de execução (http/https)
├── Program.cs                     # Configuração da aplicação e endpoints
├── appsettings.json               # Configuração base
├── appsettings.Development.json   # Configuração de desenvolvimento (inclui ConnectionStrings local)
├── Dockerfile                     # Build multi-stage + Chrome/chromedriver Linux
├── docker-compose.yml             # api + mysql + volumes
├── .env.example                   # Modelo de variáveis para o docker-compose
├── Automacao.DuamApi.csproj       # Definição do projeto e dependências
└── Automacao.DuamApi.http         # Exemplos de requisição HTTP para testes
```

## Pré-requisitos

- [.NET SDK](https://dotnet.microsoft.com/) compatível com `net10.0` (conforme definido em `Automacao.DuamApi.csproj`).
- Um servidor **MySQL** acessível (local, Docker ou já existente) — usado tanto para persistência de domínio (`jobs`/`resultados_linha`) quanto como storage do Hangfire.
- Google Chrome instalado na máquina que executa a API (Windows local) **ou** Docker (o `Dockerfile` já instala Chrome + chromedriver Linux).
- Acesso à internet/rede para alcançar `https://araguaina.prodataweb.inf.br`.
- Credenciais válidas de usuário e senha do sistema SIG.

## Como rodar o projeto

### Opção A — Docker Compose (recomendado)

Sobe a API e o MySQL juntos, com Chrome já embutido na imagem:

```bash
cp .env.example .env
```

Edite o `.env` com senhas próprias e depois:

```bash
docker compose up --build
```

Aplique a migração das tabelas de domínio contra o MySQL exposto pelo compose (`localhost:3306`, ver credenciais no `.env`):

```bash
dotnet tool install --global dotnet-ef
ConnectionStrings__DuamMySql="Server=localhost;Port=3306;Database=duam_api;User=duam_api;Password=<MYSQL_PASSWORD do .env>;" dotnet ef database update
```

A API fica em `http://localhost:8080/` (Swagger) e o dashboard do Hangfire em `http://localhost:8080/hangfire`.

### Opção B — Local (sem Docker)

1. Suba um MySQL local (ex.: `docker run -e MYSQL_ROOT_PASSWORD=root -p 3306:3306 mysql:8`) e configure `ConnectionStrings:DuamMySql` em `appsettings.Development.json` ou via `dotnet user-secrets`.

2. Restaurar as dependências:

```bash
dotnet restore
```

3. Executar a aplicação (perfil HTTP, porta `5043`) — em ambiente `Development`, as migrações são aplicadas automaticamente na inicialização:

```bash
dotnet run
```

4. Acessar o Swagger (raiz da aplicação) e o dashboard do Hangfire:

```
http://localhost:5043/
http://localhost:5043/hangfire
```

> O perfil `https` também está disponível (`https://localhost:7224`), definido em `Properties/launchSettings.json`.

## Uso da API

### `POST /duam/processar`

Enfileira o processamento de uma planilha. Recebe dados via `multipart/form-data`:

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `Usuario` | string | Sim | Usuário de login no SIG. |
| `Senha` | string | Sim | Senha de login no SIG. |
| `Planilha` | arquivo (.xlsx) | Sim | Planilha Excel com os DUAMs a lançar. |

**Validações realizadas pelo endpoint:**
- Usuário e senha não podem ser vazios.
- Planilha é obrigatória.
- Somente arquivos com extensão `.xlsx` são aceitos.

**Resposta (`202 Accepted`):**

```json
{
  "jobId": "b6a2e6d0-...-...",
  "status": "Pendente",
  "statusUrl": "/duam/jobs/b6a2e6d0-...-..."
}
```

### `GET /duam/jobs/{jobId}`

Consulta o status, progresso e resultado (parcial ou final) de um job.

```json
{
  "jobId": "b6a2e6d0-...-...",
  "status": "EmProcessamento",
  "usuario": "meu.usuario",
  "nomeArquivoOriginal": "planilha.xlsx",
  "totalLinhas": 40,
  "linhasProcessadas": 12,
  "dataCriacao": "2026-08-03T10:00:00Z",
  "dataInicio": "2026-08-03T10:00:05Z",
  "dataFim": null,
  "mensagemErro": null,
  "resultados": [
    { "linha": 2, "inscricao": "123456", "sucesso": true, "mensagem": "Processado com sucesso", "dataProcessamento": "2026-08-03T10:00:20Z" }
  ]
}
```

`status` pode ser `Pendente`, `EmProcessamento`, `Concluido` ou `Falhou`. `404 Not Found` se o `jobId` não existir.

### `GET /duam/jobs`

Lista o histórico de jobs, paginado, com filtros opcionais `usuario` e `status` (`?usuario=...&status=Concluido&pagina=1&tamanhoPagina=20`).

### Exemplo via `curl`

```bash
curl -X POST http://localhost:5043/duam/processar \
  -F "Usuario=meu.usuario" \
  -F "Senha=minha.senha" \
  -F "Planilha=@caminho/para/planilha.xlsx;type=application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
```

## Formato da planilha de entrada

A primeira linha é tratada como cabeçalho e ignorada. A partir da segunda linha, as colunas esperadas são:

| Coluna | Conteúdo |
|---|---|
| A | Data de vencimento (data) |
| B | Ano |
| C | Mês |
| D | Receita |
| E | Observação |
| F | Inscrição |
| G | Valor |

## Configuração

- `ConnectionStrings:DuamMySql`: connection string do MySQL, usada tanto pelo EF Core quanto pelo storage do Hangfire. **Não** versionar credenciais reais — usar `dotnet user-secrets` em desenvolvimento local e a variável de ambiente `ConnectionStrings__DuamMySql` em produção/Docker.
- `Armazenamento:DiretorioPlanilhas`: diretório onde as planilhas enviadas são armazenadas (mantidas indefinidamente após o job, para auditoria/reprocessamento). Se vazio/nulo, usa `Path.GetTempPath()` (comportamento padrão fora de Docker). No `docker-compose.yml` aponta para o volume `planilhas_data`.
- `FilaDuam:WorkerCount`: quantidade de workers do Hangfire Server processando jobs em paralelo (default `2`).
- `Properties/launchSettings.json`: define as portas/URLs de execução local (`http://localhost:5043` e `https://localhost:7224`).
- Usuário e senha do SIG **não** são configurados via `appsettings` — são recebidos em tempo de requisição pelo próprio endpoint, e a senha é criptografada (Data Protection) antes de ser enfileirada.

## Observações e limitações

- **Seletores por XPath absoluto**: o preenchimento do formulário depende de XPaths fixos da tela do SIG. Qualquer mudança na interface do sistema Prodataweb pode quebrar a automação.
- **Esperas fixas (`Task.Delay`)**: diversas etapas usam delays fixos (2–5s) em vez de esperas condicionais, o que pode tornar o processo mais lento que o necessário ou ainda falhar em cenários de rede/sistema mais lentos.
- **Dashboard do Hangfire com autorização mínima**: `DuamHangfireDashboardAuthorizationFilter` só libera acesso a partir de `localhost`. Antes de expor `/hangfire` fora da rede local, trocar por autenticação real (API key/login).
- **Storage do Hangfire em pacote pré-lançamento**: `Hangfire.Storage.MySql` está na versão `2.1.0-beta` (é o pacote mantido mais atual compatível com o Hangfire.Core usado aqui — o `Hangfire.MySqlStorage` "estável" está desatualizado e depende de uma versão muito antiga do Hangfire.Core). Validar em homologação antes de produção.
- **API sem autenticação própria**: qualquer chamador na rede pode disparar `/duam/processar` ou ler `/duam/jobs`. Adicionar autenticação (API key/JWT) é uma melhoria pendente.
- **Chaves do Data Protection**: por padrão ficam persistidas em disco na mesma máquina/container. Se a API rodar em múltiplas instâncias ou o container for recriado sem key ring compartilhado, jobs pendentes com senha cifrada por uma instância podem falhar ao descriptografar em outra — nesse caso o job cai como `Falhou` e precisa ser reenviado.
- **Build Docker não validado neste ambiente**: o `Dockerfile`/`docker-compose.yml` foram escritos e revisados, mas não foi possível rodar `docker build`/`docker compose up` de ponta a ponta neste ambiente (Docker Desktop não estava acessível). Validar localmente antes do primeiro deploy.
