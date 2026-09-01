namespace DuamApi.Models;

public enum JobStatus
{
    Pendente,
    EmProcessamento,
    Concluido,
    Falhou
}

public enum TipoInscricao
{
    Imobiliaria,
    Economica
}

public class Job
{
    public Guid Id { get; set; }

    public string Usuario { get; set; } = string.Empty;

    public TipoInscricao TipoInscricao { get; set; }

    public string NomeArquivoOriginal { get; set; } = string.Empty;

    public string CaminhoArquivo { get; set; } = string.Empty;

    public JobStatus Status { get; set; } = JobStatus.Pendente;

    public int? TotalLinhas { get; set; }

    public int LinhasProcessadas { get; set; }

    public string? MensagemErro { get; set; }

    public DateTime DataCriacao { get; set; }

    public DateTime? DataInicio { get; set; }

    public DateTime? DataFim { get; set; }

    public List<ResultadoLinhaRegistro> Resultados { get; set; } = new();
}
