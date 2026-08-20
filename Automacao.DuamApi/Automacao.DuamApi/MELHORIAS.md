# Propostas de Melhoria — Automacao.DuamApi

Este documento lista melhorias sugeridas para o projeto, organizadas por área. O objetivo principal é sair do modelo atual — **requisição síncrona que bloqueia até todo o processamento da planilha terminar** — para um modelo assíncrono, persistente e mais resiliente, além de ganhos pontuais de performance e qualidade de código.

## Sumário

- [1. Execução assíncrona e fila de processamento](#1-execução-assíncrona-e-fila-de-processamento)
- [2. Persistência](#2-persistência)
- [3. Paralelismo](#3-paralelismo)
- [4. Performance da automação Selenium](#4-performance-da-automação-selenium)
- [5. Resiliência e tratamento de erros](#5-resiliência-e-tratamento-de-erros)
- [6. Segurança](#6-segurança)
- [7. Observabilidade](#7-observabilidade)
- [8. Configuração](#8-configuração)
- [9. Testes](#9-testes)
- [10. Infraestrutura e deploy](#10-infraestrutura-e-deploy)
- [Priorização sugerida](#priorização-sugerida)

---

## 1. Execução assíncrona e fila de processamento

> ✅ **Implementado.** `POST /duam/processar` agora enfileira o job via Hangfire e retorna `202 Accepted` + `jobId`; `GET /duam/jobs/{jobId}` expõe status/progresso/resultado e `GET /duam/jobs` lista o histórico. Fila persistida em MySQL (`Hangfire.Storage.MySql`), com retry automático em falha. Detalhes e trade-offs em [PLANO_ASYNC_MYSQL.md](PLANO_ASYNC_MYSQL.md).

**Problema atual (antes desta entrega):** `POST /duam/processar` executa toda a automação (login + N linhas da planilha) de forma síncrona dentro da própria requisição HTTP. Para planilhas grandes, isso significa requisições de minutos/horas, sujeitas a timeout de cliente/proxy/load balancer, e nenhuma visibilidade de progresso.

**Melhorias propostas:**

- **Endpoint assíncrono baseado em job**: o `POST /duam/processar` passa a apenas validar a entrada, salvar a planilha e enfileirar um job, retornando imediatamente `202 Accepted` com um `jobId`.
- **Fila em memória com `BackgroundService`**: para um primeiro passo simples, usar `System.Threading.Channels.Channel<T>` + um `IHostedService`/`BackgroundService` consumindo a fila, processando um job por vez (ou N em paralelo, ver seção 3).
- **Fila persistente/distribuída** (evolução): se o volume justificar, migrar para uma solução que sobrevive a reinícios da API — [Hangfire](https://www.hangfire.io/) (fácil de integrar com SQL Server/PostgreSQL e já traz dashboard de jobs) ou uma fila real (RabbitMQ/Azure Service Bus) se houver múltiplas instâncias/workers.
- **Endpoint de consulta de status**: `GET /duam/jobs/{jobId}` retornando status (`Pendente`, `EmProcessamento`, `Concluido`, `Falhou`), progresso (linha atual / total) e o relatório final (`List<ResultadoLinha>`) quando concluído.
- **Notificação de conclusão** (opcional): webhook configurável ou e-mail ao final do processamento, já que jobs podem demorar.

```
POST /duam/processar        → 202 Accepted { jobId }
GET  /duam/jobs/{jobId}     → status + progresso + resultado parcial/final
```

## 2. Persistência

> ✅ **Implementado.** EF Core + MySQL (`Data/DuamDbContext.cs`), com as tabelas `jobs` e `resultados_linha` (entidades [`Job`](Models/Job.cs) e [`ResultadoLinhaRegistro`](Models/ResultadoLinhaRegistro.cs)). Histórico consultável via `GET /duam/jobs`. Idempotência parcial: em um retry do Hangfire, linhas já processadas com sucesso são puladas. Planilha original é mantida em disco após o job (não apagada). Detalhes em [PLANO_ASYNC_MYSQL.md](PLANO_ASYNC_MYSQL.md).

**Problema atual (antes desta entrega):** não havia banco de dados. O resultado do processamento só existe na resposta HTTP daquela requisição — se o cliente perder a resposta, o histórico se perde.

**Melhorias propostas:**

- **Adicionar EF Core** com um banco leve para começar (SQLite) ou o banco já usado pela Sefaz/prefeitura (SQL Server/PostgreSQL), com as entidades:
  - `Job` (Id, DataCriacao, Usuario, StatusJob, NomeArquivoPlanilha, DataInicio, DataFim)
  - `ResultadoLinha` associado ao `Job` (Linha, Inscricao, Sucesso, Mensagem), permitindo consulta histórica de lançamentos.
- **Histórico consultável**: endpoint `GET /duam/jobs` para listar execuções anteriores, com filtros por usuário/data/status — útil para auditoria de quais DUAMs já foram lançados.
- **Idempotência**: registrar a combinação (inscrição, ano, mês, receita) já processada com sucesso, permitindo detectar e evitar reprocessamento duplicado de uma mesma linha em reenvios da planilha.
- **Armazenamento da planilha original**: guardar o arquivo (ou hash) associado ao job, para rastreabilidade e possível reprocessamento apenas das linhas que falharam.

## 3. Paralelismo

**Problema atual:** uma única instância do Chrome processa todas as linhas da planilha sequencialmente, uma de cada vez, dentro da mesma requisição.

**Melhorias propostas:**

- **Pool de workers Selenium**: usar `SemaphoreSlim` para limitar quantas instâncias de `ChromeDriver` podem rodar simultaneamente (ex.: 3–5, configurável), processando múltiplas linhas da planilha em paralelo dentro do mesmo job.
- **Paralelismo entre jobs**: com a fila (seção 1), permitir que o `BackgroundService` processe mais de um job simultaneamente, respeitando o mesmo limite global de instâncias de Chrome (para não sobrecarregar a máquina nem o sistema do SIG).
- **Cuidado com o servidor de destino**: paralelismo agressivo pode ser interpretado como abuso pelo SIG/Prodataweb (rate limiting, bloqueio de IP/usuário). Recomenda-se paralelismo moderado e configurável, com possibilidade de desligar (`grau de paralelismo = 1`) se necessário.
- **Particionamento por sessão de login**: como o login é por usuário, cada worker paralelo precisa da sua própria sessão de browser logada — não é possível compartilhar uma única sessão do Selenium entre threads.

## 4. Performance da automação Selenium

**Problema atual:** o código usa `Task.Delay` fixo (1–5 segundos) em praticamente todas as etapas, em vez de esperas condicionais. Isso é o principal gargalo de tempo total de execução e também uma fonte de flakiness (falha esporádica quando a tela demora mais que o delay).

**Melhorias propostas:**

- **Substituir `Task.Delay` por `WebDriverWait` com `ExpectedConditions`**: esperar por condições reais (elemento visível, clicável, atributo mudou, elemento anterior desapareceu) em vez de tempo fixo. Isso tende a reduzir bastante o tempo médio por linha, além de tornar a automação mais robusta a variações de latência do sistema.
- **Reaproveitar a sessão do browser entre linhas de um mesmo job** (já é o caso atual) e **entre jobs consecutivos do mesmo usuário**, quando viável, evitando repetir o login a cada job.
- **Reduzir carregamento desnecessário**: desabilitar carregamento de imagens (`profile.managed_default_content_settings.images: 2`) e recursos não essenciais no `ChromeOptions`, já que a automação não depende de renderização visual.
- **Métricas de tempo por etapa**: instrumentar `LancarDuam` para medir quanto tempo cada campo/etapa consome, permitindo identificar os maiores ofensores antes de otimizar "no escuro".

## 5. Resiliência e tratamento de erros

**Problema atual:** falha em uma linha é capturada e registrada, mas não há retry; XPaths absolutos e rígidos quebram facilmente com qualquer mudança na tela do SIG; não há evidência (screenshot/HTML) do estado da tela no momento da falha.

**Melhorias propostas:**

- **Retry com backoff** (via [Polly](https://github.com/App-vNext/Polly)) para falhas transitórias (timeout de elemento, erro de rede) antes de marcar a linha como falha definitiva.
- **Screenshot + HTML da página no momento da falha**, salvos junto ao resultado da linha, para facilitar diagnóstico sem precisar reproduzir manualmente.
- **XPaths mais resilientes**: preferir seletores relativos por atributo estável (`name`, `id`, `nat=`, `placeholder`) em vez de caminhos absolutos completos (`/html/body/div[1]/div/...`), que quebram com qualquer mudança de layout.
- **Timeout configurável por etapa**, em vez de um único `WebDriverWait` genérico de 10s reaproveitado para tudo.
- **Circuit breaker**: se muitas linhas seguidas falharem (ex.: sistema do SIG fora do ar ou tela mudou), interromper o job cedo em vez de continuar tentando (e falhando) linha a linha até o fim da planilha.

## 6. Segurança

**Problema atual:** usuário e senha do SIG trafegam em texto puro no corpo da requisição a cada chamada; o endpoint da API não tem autenticação própria; não há rate limiting.

**Melhorias propostas:**

- **Autenticação na própria API** (ex.: API Key ou JWT) para controlar quem pode acionar a automação, já que hoje qualquer chamador na rede pode disparar o processo.
- **Não persistir credenciais em texto plano**: se credenciais forem armazenadas para reprocessamento/retry, usar criptografia (ex.: `IDataProtector` do ASP.NET Core) ou um cofre de segredos (Azure Key Vault, etc.).
- **HTTPS obrigatório em produção** (`app.UseHttpsRedirection()` — hoje não está configurado no `Program.cs`).
- **Rate limiting** no endpoint (`Microsoft.AspNetCore.RateLimiting`), evitando que a API seja usada para múltiplos disparos simultâneos que sobrecarreguem o SIG.
- **Validação de arquivo mais rigorosa**: além da extensão `.xlsx`, validar tamanho máximo do arquivo e, idealmente, o conteúdo real (magic bytes) para evitar upload de arquivos malformados/maliciosos disfarçados de `.xlsx`.
- **Log sem dados sensíveis**: garantir que senha nunca seja logada, nem em mensagens de exceção.

## 7. Observabilidade

**Problema atual:** logging padrão do ASP.NET Core apenas; nenhum log estruturado do progresso da automação; nenhuma métrica ou health check.

**Melhorias propostas:**

- **Logging estruturado** (Serilog) com correlação por `jobId`, permitindo acompanhar todo o ciclo de um job nos logs.
- **Health check** (`/health`) reportando, por exemplo, se o ChromeDriver consegue subir uma instância — útil para monitoramento em produção.
- **Métricas**: quantidade de jobs processados, taxa de sucesso/falha por linha, tempo médio por linha/job — expostas via `/metrics` (Prometheus) ou enviadas a uma ferramenta de APM.
- **Log de progresso em tempo real**: já com a fila (seção 1), publicar eventos de progresso (ex.: via SignalR) para acompanhamento em tempo real no cliente, sem precisar fazer polling constante.

## 8. Configuração

**Problema atual:** URL do SIG, XPaths, timeouts e delays estão *hardcoded* em `DuamAutomationService.cs`. Trocar de ambiente (homologação/produção) ou ajustar tempos exige recompilar.

**Melhorias propostas:**

- **Extrair para `appsettings.json` via `IOptions<T>`**: URL base do SIG, timeout padrão do `WebDriverWait`, grau de paralelismo, diretório de upload temporário, tamanho máximo de arquivo.
- **Perfis por ambiente**: `appsettings.Homologacao.json`, `appsettings.Producao.json` com URLs distintas, já que sistemas municipais costumam ter ambiente de testes.
- **Feature flag para modo headless/visível**, útil para debug local sem precisar alterar código.

## 9. Testes

**Problema atual:** não há testes automatizados no projeto.

**Melhorias propostas:**

- **Testes unitários** para a lógica de leitura/validação da planilha (parsing de datas, valores, campos obrigatórios) — extraindo essa lógica do `DuamAutomationService` para uma classe testável isoladamente do Selenium.
- **Testes de integração da API** (`WebApplicationFactory`) cobrindo as validações do endpoint (arquivo ausente, extensão inválida, campos obrigatórios).
- **Testes E2E do fluxo Selenium** contra um ambiente de homologação do SIG (se disponível), executados separadamente do build normal (ex.: pipeline noturna), dado o custo/fragilidade desse tipo de teste.

## 10. Infraestrutura e deploy

**Melhorias propostas:**

- **Containerizar com Docker** ✅ implementado — [`Dockerfile`](Dockerfile) + [`docker-compose.yml`](docker-compose.yml) (api + mysql + volumes), Chrome + ChromeDriver Linux instalados/baixados no build da imagem. Ver [PLANO_ASYNC_MYSQL.md](PLANO_ASYNC_MYSQL.md#docker-dockerfile--docker-compose). Build ainda não validado de ponta a ponta (Docker Desktop indisponível no ambiente em que foi implementado — validar antes do primeiro deploy).
- **Separar Worker da API** (arquitetura futura): um serviço Web (API + fila) e um serviço Worker dedicado a rodar o Selenium, escalável independentemente (mais réplicas de worker = mais paralelismo, sem afetar a API).
- **CI**: pipeline de build/test automatizado a cada push, hoje inexistente.

---

## Priorização sugerida

| Prioridade | Item | Motivo |
|---|---|---|
| ✅ Concluído | Execução assíncrona (job + endpoint de status) | Implementado com Hangfire — ver seção 1 |
| ✅ Concluído | Persistência (histórico de jobs/resultados) | Implementado com EF Core + MySQL — ver seção 2 |
| ✅ Concluído | Containerização (Docker) | `Dockerfile` + `docker-compose.yml` — build não validado de ponta a ponta neste ambiente |
| Alta | Substituir `Task.Delay` por `WebDriverWait` condicional | Ganho de performance imediato, baixo risco |
| Alta | Autenticação da API + HTTPS | API/dashboard do Hangfire hoje só têm proteção mínima (localhost) |
| Média | Paralelismo controlado (`WorkerCount` do Hangfire / semáforo) | Reduz tempo total, mas exige cuidado com o SIG |
| Média | Retry + screenshot em falha | Retry automático do Hangfire já existe; screenshot na falha ainda não |
| Baixa | Métricas/observabilidade avançada | Importante em escala, menos urgente no estágio atual |
| Baixa | Separar Worker da API (processo dedicado) | Evolução arquitetural, não bloqueante hoje |
