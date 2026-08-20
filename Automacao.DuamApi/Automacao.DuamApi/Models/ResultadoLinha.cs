namespace DuamApi.Models;

public class ResultadoLinha
{
    public int Linha { get; set; }

    public string Inscricao { get; set; } = "";

    public bool Sucesso { get; set; }

    public string Mensagem { get; set; } = "";
}