namespace DuamApi.Models;

public record LoginRequest(string Usuario, string Senha, string? CaptchaToken = null, string? CaptchaCodigo = null);

public record LoginResponse(string Login, string? NomeEmpresa);

public record CaptchaChallengeResponse(bool CaptchaNecessario, string? Token, string? ImagemBase64);
