using ClosedXML.Excel;
using DuamApi.Data;
using DuamApi.Models;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DuamApi.Services;

public class DuamJobProcessor : IDuamJobProcessor
{
    public const string DataProtectionPurpose = "DuamApi.SenhaJob";

    private readonly DuamDbContext _db;
    private readonly DuamAutomationService _automationService;
    private readonly IDataProtector _dataProtector;

    public DuamJobProcessor(
        DuamDbContext db,
        DuamAutomationService automationService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _automationService = automationService;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 300 })]
    public async Task ProcessarAsync(Guid jobId, string senhaCriptografada)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null)
            return;

        string senha;

        try
        {
            senha = _dataProtector.Unprotect(senhaCriptografada);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Falhou;
            job.MensagemErro =
                "Não foi possível descriptografar as credenciais do job " +
                "(provável rotação das chaves de Data Protection): " +
                $"{ex.Message}. Reenvie a planilha.";
            job.DataFim = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return;
        }

        job.Status = JobStatus.EmProcessamento;
        job.DataInicio ??= DateTime.UtcNow;
        job.MensagemErro = null;

        if (job.TotalLinhas == null)
        {
            using var workbook = new XLWorkbook(job.CaminhoArquivo);
            var ws = workbook.Worksheet(1);
            job.TotalLinhas = ws.RangeUsed().RowsUsed().Skip(1).Count();
        }

        await _db.SaveChangesAsync();

        var linhasJaProcessadas = await _db.ResultadosLinha
            .Where(r => r.JobId == jobId && r.Sucesso)
            .Select(r => r.Linha)
            .ToListAsync();

        job.LinhasProcessadas = linhasJaProcessadas.Count;
        await _db.SaveChangesAsync();

        try
        {
            await _automationService.ProcessarAsync(
                job.Usuario,
                senha,
                job.CaminhoArquivo,
                job.TipoInscricao,
                linhasJaProcessadas.ToHashSet(),
                async resultado =>
                {
                    _db.ResultadosLinha.Add(new ResultadoLinhaRegistro
                    {
                        JobId = jobId,
                        Linha = resultado.Linha,
                        Inscricao = resultado.Inscricao,
                        Sucesso = resultado.Sucesso,
                        Mensagem = resultado.Mensagem,
                        DataProcessamento = DateTime.UtcNow
                    });

                    job.LinhasProcessadas++;

                    await _db.SaveChangesAsync();
                });

            job.Status = JobStatus.Concluido;
            job.DataFim = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            job.MensagemErro = ex.Message;

            await _db.SaveChangesAsync();

            throw;
        }
    }
}
