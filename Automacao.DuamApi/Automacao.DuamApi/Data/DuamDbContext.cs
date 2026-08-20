using DuamApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DuamApi.Data;

public class DuamDbContext : DbContext
{
    public DuamDbContext(DbContextOptions<DuamDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<ResultadoLinhaRegistro> ResultadosLinha => Set<ResultadoLinhaRegistro>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("jobs");

            entity.HasKey(j => j.Id);

            entity.Property(j => j.Id)
                .HasColumnName("id")
                .HasColumnType("char(36)");

            entity.Property(j => j.Usuario)
                .HasColumnName("usuario")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(j => j.NomeArquivoOriginal)
                .HasColumnName("nome_arquivo_original")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(j => j.CaminhoArquivo)
                .HasColumnName("caminho_arquivo_temp")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(j => j.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(j => j.TotalLinhas)
                .HasColumnName("total_linhas");

            entity.Property(j => j.LinhasProcessadas)
                .HasColumnName("linhas_processadas")
                .HasDefaultValue(0);

            entity.Property(j => j.MensagemErro)
                .HasColumnName("mensagem_erro")
                .HasColumnType("text");

            entity.Property(j => j.DataCriacao)
                .HasColumnName("data_criacao");

            entity.Property(j => j.DataInicio)
                .HasColumnName("data_inicio");

            entity.Property(j => j.DataFim)
                .HasColumnName("data_fim");

            entity.HasIndex(j => new { j.Status, j.DataCriacao });

            entity.HasMany(j => j.Resultados)
                .WithOne(r => r.Job!)
                .HasForeignKey(r => r.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResultadoLinhaRegistro>(entity =>
        {
            entity.ToTable("resultados_linha");

            entity.HasKey(r => r.Id);

            entity.Property(r => r.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(r => r.JobId)
                .HasColumnName("job_id")
                .HasColumnType("char(36)");

            entity.Property(r => r.Linha)
                .HasColumnName("linha");

            entity.Property(r => r.Inscricao)
                .HasColumnName("inscricao")
                .HasMaxLength(50);

            entity.Property(r => r.Sucesso)
                .HasColumnName("sucesso");

            entity.Property(r => r.Mensagem)
                .HasColumnName("mensagem")
                .HasColumnType("text");

            entity.Property(r => r.DataProcessamento)
                .HasColumnName("data_processamento");

            entity.HasIndex(r => r.JobId);
        });
    }
}
