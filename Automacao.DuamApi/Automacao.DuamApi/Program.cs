using DuamApi.Data;
using DuamApi.Models;
using DuamApi.Services;
using Hangfire;
using Hangfire.Storage.MySql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DuamMySql")
    ?? throw new InvalidOperationException(
        "Connection string 'DuamMySql' não configurada. Defina ConnectionStrings:DuamMySql " +
        "(appsettings, user-secrets ou variável de ambiente ConnectionStrings__DuamMySql).");

builder.Services.AddEndpointsApiExplorer();

var corsAllowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DUAM Automation API",
        Version = "v1",
        Description = "API de automação de lançamento DUAM com Selenium"
    });
});

builder.Services.AddScoped<DuamAutomationService>();
builder.Services.AddScoped<IDuamJobProcessor, DuamJobProcessor>();

builder.Services.AddHttpClient<SigAuthService>();

builder.Services.Configure<ArmazenamentoOptions>(
    builder.Configuration.GetSection("Armazenamento"));

builder.Services.AddDbContext<DuamDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddDataProtection();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseStorage(new MySqlStorage(
        connectionString,
        new MySqlStorageOptions { PrepareSchemaIfNecessary = true })));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = builder.Configuration.GetValue("FilaDuam:WorkerCount", 2);
});

var app = builder.Build();

GlobalJobFilters.Filters.Add(
    new SincronizarStatusJobFalhoFilter(app.Services.GetRequiredService<IServiceScopeFactory>()));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DuamDbContext>();
    db.Database.Migrate();
}

app.UseCors("Frontend");

app.UseSwagger();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint(
        "/swagger/v1/swagger.json",
        "DUAM Automation API v1");

    options.RoutePrefix = string.Empty;
});

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new DuamHangfireDashboardAuthorizationFilter() }
});

app.MapGet("/duam/login/captcha", async (SigAuthService sigAuthService) =>
{
    var desafio = await sigAuthService.ObterCaptchaAsync();

    return Results.Ok(desafio);
})
.WithName("ObterCaptchaLoginDuam");

app.MapPost("/duam/login", async (LoginRequest request, SigAuthService sigAuthService) =>
{
    if (string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Senha))
        return Results.BadRequest("Usuário e senha são obrigatórios.");

    var resultado = await sigAuthService.ValidarLoginAsync(
        request.Usuario, request.Senha, request.CaptchaToken, request.CaptchaCodigo);

    if (!resultado.Sucesso || resultado.Login == null)
        return Results.Unauthorized();

    return Results.Ok(new LoginResponse(resultado.Login, resultado.NomeEmpresa));
})
.WithName("LoginDuam");

app.MapPost("/duam/processar", async (
    [FromForm] DuamRequest request,
    DuamDbContext db,
    IBackgroundJobClient backgroundJobClient,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<ArmazenamentoOptions> armazenamentoOptions) =>
{
    if (string.IsNullOrWhiteSpace(request.Usuario))
        return Results.BadRequest("Usuário obrigatório.");

    if (string.IsNullOrWhiteSpace(request.Senha))
        return Results.BadRequest("Senha obrigatória.");

    if (request.Planilha == null || request.Planilha.Length == 0)
        return Results.BadRequest("Planilha obrigatória.");

    if (!Enum.TryParse<TipoInscricao>(request.TipoInscricao, true, out var tipoInscricaoEnum))
        return Results.BadRequest("Tipo de inscrição inválido.");

    var extensao = Path.GetExtension(request.Planilha.FileName);

    if (extensao.ToLower() != ".xlsx")
        return Results.BadRequest("Somente arquivos .xlsx");

    var jobId = Guid.NewGuid();

    var diretorioPlanilhas = string.IsNullOrWhiteSpace(armazenamentoOptions.Value.DiretorioPlanilhas)
        ? Path.GetTempPath()
        : armazenamentoOptions.Value.DiretorioPlanilhas;

    Directory.CreateDirectory(diretorioPlanilhas);

    var caminhoArquivo = Path.Combine(diretorioPlanilhas, $"{jobId}.xlsx");

    using (var stream = File.Create(caminhoArquivo))
    {
        await request.Planilha.CopyToAsync(stream);
    }

    var job = new Job
    {
        Id = jobId,
        Usuario = request.Usuario,
        TipoInscricao = tipoInscricaoEnum,
        NomeArquivoOriginal = request.Planilha.FileName,
        CaminhoArquivo = caminhoArquivo,
        Status = JobStatus.Pendente,
        DataCriacao = DateTime.UtcNow
    };

    db.Jobs.Add(job);
    await db.SaveChangesAsync();

    var dataProtector = dataProtectionProvider.CreateProtector(DuamJobProcessor.DataProtectionPurpose);
    var senhaCriptografada = dataProtector.Protect(request.Senha);

    backgroundJobClient.Enqueue<IDuamJobProcessor>(p => p.ProcessarAsync(job.Id, senhaCriptografada));

    var statusUrl = $"/duam/jobs/{job.Id}";

    return Results.Accepted(
        statusUrl,
        new JobEnqueuedResponse(job.Id, job.Status.ToString(), statusUrl));
})
.DisableAntiforgery()
.Accepts<DuamRequest>("multipart/form-data")
.WithName("ProcessarDuam");

app.MapGet("/duam/jobs/{jobId:guid}", async (Guid jobId, DuamDbContext db) =>
{
    var job = await db.Jobs
        .Include(j => j.Resultados)
        .FirstOrDefaultAsync(j => j.Id == jobId);

    if (job == null)
        return Results.NotFound();

    var resultados = job.Resultados
        .OrderBy(r => r.Linha)
        .ThenBy(r => r.DataProcessamento)
        .Select(r => new ResultadoLinhaResponse(
            r.Linha, r.Inscricao, r.Sucesso, r.Mensagem, r.DataProcessamento))
        .ToList();

    var response = new JobStatusResponse(
        job.Id,
        job.Status.ToString(),
        job.Usuario,
        job.NomeArquivoOriginal,
        job.TotalLinhas,
        job.LinhasProcessadas,
        job.Resultados.Count(r => !r.Sucesso),
        job.DataCriacao,
        job.DataInicio,
        job.DataFim,
        job.MensagemErro,
        resultados);

    return Results.Ok(response);
})
.WithName("ObterStatusJobDuam");

app.MapGet("/duam/jobs", async (
    DuamDbContext db,
    string? usuario,
    string? status,
    int pagina = 1,
    int tamanhoPagina = 20) =>
{
    pagina = Math.Max(pagina, 1);
    tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 100);

    var query = db.Jobs.AsQueryable();

    if (!string.IsNullOrWhiteSpace(usuario))
        query = query.Where(j => j.Usuario == usuario);

    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var statusEnum))
        query = query.Where(j => j.Status == statusEnum);

    var total = await query.CountAsync();

    var jobs = await query
        .OrderByDescending(j => j.DataCriacao)
        .Skip((pagina - 1) * tamanhoPagina)
        .Take(tamanhoPagina)
        .Select(j => new JobResumoResponse(
            j.Id,
            j.Status.ToString(),
            j.Usuario,
            j.NomeArquivoOriginal,
            j.TotalLinhas,
            j.LinhasProcessadas,
            j.Resultados.Count(r => !r.Sucesso),
            j.DataCriacao,
            j.DataInicio,
            j.DataFim))
        .ToListAsync();

    return Results.Ok(new JobListaResponse(total, pagina, tamanhoPagina, jobs));
})
.WithName("ListarJobsDuam");

app.Run();
