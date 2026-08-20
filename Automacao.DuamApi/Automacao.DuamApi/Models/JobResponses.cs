namespace DuamApi.Models;

public record JobEnqueuedResponse(Guid JobId, string Status, string StatusUrl);

public record ResultadoLinhaResponse(
    int Linha,
    string Inscricao,
    bool Sucesso,
    string Mensagem,
    DateTime DataProcessamento);

public record JobStatusResponse(
    Guid JobId,
    string Status,
    string Usuario,
    string NomeArquivoOriginal,
    int? TotalLinhas,
    int LinhasProcessadas,
    int LinhasComErro,
    DateTime DataCriacao,
    DateTime? DataInicio,
    DateTime? DataFim,
    string? MensagemErro,
    List<ResultadoLinhaResponse> Resultados);

public record JobResumoResponse(
    Guid JobId,
    string Status,
    string Usuario,
    string NomeArquivoOriginal,
    int? TotalLinhas,
    int LinhasProcessadas,
    int LinhasComErro,
    DateTime DataCriacao,
    DateTime? DataInicio,
    DateTime? DataFim);

public record JobListaResponse(
    int Total,
    int Pagina,
    int TamanhoPagina,
    List<JobResumoResponse> Jobs);
