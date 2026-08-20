namespace DuamApi.Models;

public record LoginRequest(string Usuario, string Senha);

public record LoginResponse(string Login, string? NomeEmpresa);
