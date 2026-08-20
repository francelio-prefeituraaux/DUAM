using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DuamApi.Models;

namespace DuamApi.Services;

public class SigAuthService
{
    private const string BaseUrl = "https://araguaina.prodataweb.inf.br";
    private const string ClientId = "sig-frontend";
    private const string SignatureKey = "request-prodata-hash-code";

    private readonly HttpClient _httpClient;

    public SigAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SigLoginResult> ValidarLoginAsync(string usuario, string senha)
    {
        var client = _httpClient;

        var (sucesso, token, sessionCookie) = await ValidarCredenciaisAsync(client, usuario, senha);

        if (!sucesso || token == null)
            return new SigLoginResult(false, null, null);

        var login = await ObterLoginUsuarioAsync(client, token, sessionCookie);

        if (login == null)
            return new SigLoginResult(false, null, null);

        var nomeEmpresa = await ObterNomeEmpresaAsync(client, sessionCookie);

        return new SigLoginResult(true, login, nomeEmpresa);
    }

    private async Task<(bool sucesso, string? token, string? sessionCookie)> ValidarCredenciaisAsync(
        HttpClient client,
        string usuario,
        string senha)
    {
        using var request = CriarRequisicao(HttpMethod.Post, "/sig/rest/loginController/validarLogin", "login", null, null);

        var corpo = JsonSerializer.Serialize(new { usuario, senha });
        request.Content = new StringContent(corpo, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        var sessionCookie = ExtrairSessionCookie(response);

        if (!response.IsSuccessStatusCode)
            return (false, null, sessionCookie);

        var corpoResposta = await response.Content.ReadAsStringAsync();

        var token = ExtrairToken(corpoResposta);

        return (token != null, token, sessionCookie);
    }

    private async Task<string?> ObterLoginUsuarioAsync(HttpClient client, string token, string? sessionCookie)
    {
        using var request = CriarRequisicao(
            HttpMethod.Post,
            "/sig/rest/loginController/getPermissoesDoUsuarioLogado",
            "sig",
            token,
            sessionCookie,
            xModulo: "menu");

        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            return null;

        var corpo = await response.Content.ReadAsStringAsync();

        return ExtrairCampoTexto(corpo, "login");
    }

    private async Task<string?> ObterNomeEmpresaAsync(HttpClient client, string? sessionCookie)
    {
        using var request = CriarRequisicao(
            HttpMethod.Get,
            "/sig/rest/paramController/getDadosDaEmpresaParaLogin",
            "login",
            null,
            sessionCookie);

        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            return null;

        var corpo = await response.Content.ReadAsStringAsync();

        return ExtrairCampoTexto(corpo, "nomeEmpresa");
    }

    private static HttpRequestMessage CriarRequisicao(
        HttpMethod metodo,
        string caminho,
        string xId,
        string? xAuthToken,
        string? sessionCookie,
        string? xModulo = null)
    {
        var request = new HttpRequestMessage(metodo, BaseUrl + caminho);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var assinatura = CalcularAssinatura($"{ClientId}|{timestamp}");

        request.Headers.Add("Accept", "application/json, text/plain, */*");
        request.Headers.Add("x-client-id", ClientId);
        request.Headers.Add("x-timestamp", timestamp);
        request.Headers.Add("x-request-signature", assinatura);
        request.Headers.Add("x-id", xId);
        request.Headers.Add("x-origin", BaseUrl);

        request.Headers.Add(
            "x-url",
            xId == "login" ? $"{BaseUrl}/sig/index.html" : $"{BaseUrl}/sig/app.html#/menu");

        if (xModulo != null)
            request.Headers.Add("x-modulo", xModulo);

        if (xAuthToken != null)
            request.Headers.Add("x-auth-token", xAuthToken);

        if (sessionCookie != null)
            request.Headers.Add("Cookie", sessionCookie);

        return request;
    }

    private static string CalcularAssinatura(string mensagem)
    {
        var chave = Encoding.UTF8.GetBytes(SignatureKey);
        var dados = Encoding.UTF8.GetBytes(mensagem);

        using var hmac = new HMACSHA256(chave);
        var hash = hmac.ComputeHash(dados);

        return Convert.ToBase64String(hash);
    }

    private static string? ExtrairSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        return cookies
            .Select(c => c.Split(';')[0])
            .FirstOrDefault(c => c.StartsWith("JSESSIONID="));
    }

    private static string? ExtrairToken(string corpo)
    {
        var texto = corpo?.Trim();

        if (string.IsNullOrEmpty(texto))
            return null;

        if (texto.StartsWith('"') && texto.EndsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(texto);
            }
            catch (JsonException)
            {
            }
        }

        return texto.Count(c => c == '.') == 2 ? texto : null;
    }

    private static string? ExtrairCampoTexto(string corpoJson, string nomeCampo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpoJson);

            return documento.RootElement.TryGetProperty(nomeCampo, out var elemento)
                ? elemento.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
