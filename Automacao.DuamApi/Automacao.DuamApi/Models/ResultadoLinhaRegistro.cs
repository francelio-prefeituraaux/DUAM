namespace DuamApi.Models;

public class ResultadoLinhaRegistro
{
    public long Id { get; set; }

    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public int Linha { get; set; }

    public string Inscricao { get; set; } = string.Empty;

    public bool Sucesso { get; set; }

    public string Mensagem { get; set; } = string.Empty;

    public DateTime DataProcessamento { get; set; }
}
