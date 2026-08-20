using System.Net;
using Hangfire.Dashboard;

namespace DuamApi.Services;

/// <summary>
/// Filtro mínimo: só permite acesso ao /hangfire a partir de localhost.
/// Antes de expor o dashboard fora da rede local, substituir por autenticação
/// real (API key / login), conforme item 6 (segurança) de MELHORIAS.md.
/// </summary>
public class DuamHangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        return remoteIp != null && IPAddress.IsLoopback(remoteIp);
    }
}
