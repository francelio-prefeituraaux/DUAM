using Microsoft.AspNetCore.Http;

namespace DuamApi.Models;

public class DuamRequest
{
    public string Usuario { get; set; } = string.Empty;

    public string Senha { get; set; } = string.Empty;

    public IFormFile Planilha { get; set; } = default!;
}