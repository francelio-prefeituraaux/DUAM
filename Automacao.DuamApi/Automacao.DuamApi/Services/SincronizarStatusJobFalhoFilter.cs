using DuamApi.Data;
using DuamApi.Models;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DuamApi.Services;

public class SincronizarStatusJobFalhoFilter : JobFilterAttribute, IApplyStateFilter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SincronizarStatusJobFalhoFilter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState)
            return;

        if (context.BackgroundJob.Job.Args.Count == 0 ||
            context.BackgroundJob.Job.Args[0] is not Guid jobId)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DuamDbContext>();

        var job = db.Jobs.Find(jobId);

        if (job == null)
            return;

        job.Status = JobStatus.Falhou;
        job.MensagemErro ??= "Processamento falhou após esgotar as tentativas automáticas.";
        job.DataFim = DateTime.UtcNow;

        db.SaveChanges();
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
