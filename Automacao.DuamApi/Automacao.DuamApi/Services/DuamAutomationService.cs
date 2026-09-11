 using ClosedXML.Excel;
using DuamApi.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

namespace DuamApi.Services;

public class DuamAutomationService
{
    private IWebDriver? _debugDriver;

    public async Task<List<ResultadoLinha>> ProcessarAsync(
        string usuario,
        string senha,
        string planilhaPath,
        TipoInscricao tipoInscricao,
        ISet<int>? linhasJaProcessadas = null,
        Func<ResultadoLinha, Task>? onLinhaProcessada = null)
    {
        var resultados = new List<ResultadoLinha>();

        var driverPath = Path.Combine(AppContext.BaseDirectory,"Drivers");

        var options = new ChromeOptions();

        options.AddArgument("--start-maximized");

        // Servidor
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");

        var service = ChromeDriverService.CreateDefaultService(Path.Combine(AppContext.BaseDirectory, "Drivers"));

        service.HideCommandPromptWindow = true;

        using var driver = new ChromeDriver(
            service,
            options);

        _debugDriver = driver;

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));

        await Login(driver, wait, usuario, senha);

        using var workbook = new XLWorkbook(planilhaPath);

        var ws = workbook.Worksheet(1);

        var rows = ws.RangeUsed().RowsUsed().Skip(1);

        int linha = 2;

        foreach (var row in rows)
        {
            if (linhasJaProcessadas != null && linhasJaProcessadas.Contains(linha))
            {
                linha++;
                continue;
            }

            var resultado = new ResultadoLinha();

            try
            {
                resultado.Linha = linha;

                var data = row.Cell("A").GetDateTime();

                var ano = row.Cell("B").GetString();

                var mes = row.Cell("C").GetString();

                var receita = row.Cell("D").GetString();

                var observacao = row.Cell("E").GetString();

                var inscricao = row.Cell("F").GetString();

                var valor = row.Cell("G").GetString();

                resultado.Inscricao = inscricao;

                await LancarDuam(
                    driver,
                    wait,
                    data,
                    ano,
                    mes,
                    receita,
                    observacao,
                    inscricao,
                    valor,
                    tipoInscricao);

                resultado.Sucesso = true;
                resultado.Mensagem = "Processado com sucesso";
            }
            catch (Exception ex)
            {
                resultado.Sucesso = false;
                resultado.Mensagem = ex.Message;
            }

            resultados.Add(resultado);

            if (onLinhaProcessada != null)
                await onLinhaProcessada(resultado);

            linha++;
        }

        driver.Quit();

        return resultados;
    }

    private string? CapturarScreenshot(string contexto)
    {
        try
        {
            if (_debugDriver == null) return null;

            var dir = Path.Combine(AppContext.BaseDirectory, "debug-screenshots");
            Directory.CreateDirectory(dir);

            var nomeArquivo = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{Regex.Replace(contexto, "[^a-zA-Z0-9]+", "-")}.png";
            var caminho = Path.Combine(dir, nomeArquivo);

            ((ITakesScreenshot)_debugDriver).GetScreenshot().SaveAsFile(caminho);

            return caminho;
        }
        catch
        {
            return null;
        }
    }

    private IWebElement WaitForElement(WebDriverWait wait, string xpath, string descricao)
    {
        try
        {
            return wait.Until(d => d.FindElement(By.XPath(xpath)));
        }
        catch (WebDriverTimeoutException ex)
        {
            var screenshot = CapturarScreenshot(descricao);
            var sufixo = screenshot != null ? $" Screenshot: {screenshot}" : "";

            throw new TimeoutException(
                $"Timeout aguardando '{descricao}' (elemento não apareceu em {wait.Timeout.TotalSeconds:0}s).{sufixo}",
                ex);
        }
    }

    private async Task Login(
        IWebDriver driver,
        WebDriverWait wait,
        string usuario,
        string senha)
    {
        driver.Navigate().GoToUrl(
            "https://araguaina.prodataweb.inf.br/sig/index.html");

        var usuarioInput = WaitForElement(wait, "//input[@placeholder='Usuário']", "Campo Usuário (login)");

        usuarioInput.SendKeys(usuario);

        var senhaInput = WaitForElement(wait, "//input[@placeholder='Senha']", "Campo Senha (login)");

        senhaInput.SendKeys(senha);

        var entrarButton = WaitForElement(wait, "//button[contains(text(), 'Entrar')]", "Botão Entrar (login)");

        entrarButton.Click();

        await Task.Delay(5000);

        try
        {
            wait.Until(d => !d.Url.Contains("/sig/index.html"));
        }
        catch (WebDriverTimeoutException ex)
        {
            var screenshot = CapturarScreenshot("falha-login");
            var sufixo = screenshot != null ? $" Screenshot: {screenshot}" : "";

            throw new TimeoutException(
                "Falha no login: a página continuou na tela de login após clicar em 'Entrar' " +
                $"(usuário/senha inválidos, captcha, ou portal indisponível).{sufixo}",
                ex);
        }
    }

    private async Task LancarDuam(
      IWebDriver driver,
      WebDriverWait wait,
      DateTime data,
      string ano,
      string mes,
      string receita,
      string observacao,
      string inscricao,
      string valor,
      TipoInscricao tipoInscricao)
    {
        driver.Navigate().GoToUrl(
            "https://araguaina.prodataweb.inf.br/sig/app.html#/arrecadacao/duam");

        await Task.Delay(5000);

        // =========================
        // DATA DE VENCIMENTO
        // =========================

        var campoData = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[1]/div/p/input",
            "Campo Data de Vencimento");

        campoData.Clear();

        campoData.SendKeys(data.ToString("dd/MM/yyyy"));

        await Task.Delay(3000);

        // =========================
        // ANO
        // =========================

        var campoAno = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[2]/div/p/input",
            "Campo Ano");

        campoAno.Clear();

        campoAno.SendKeys(ano);

        await Task.Delay(1000);

        // =========================
        // MÊS
        // =========================

        var campoMes = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[3]/div/p/input",
            "Campo Mês");

        campoMes.Clear();

        campoMes.SendKeys(mes);

        await Task.Delay(3000);

        // =========================
        // RECEITA
        // =========================

        var campoReceita = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[2]/pd-autocomplete[1]/div/div/div/div[1]/pd-input-text//input",
            "Campo Receita");

        campoReceita.Clear();

        campoReceita.SendKeys(receita);

        await Task.Delay(2000);

        campoReceita.SendKeys(Keys.Enter);

        await Task.Delay(2000);

        // =========================
        // OBSERVAÇÃO
        // =========================

        var campoObservacao = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[3]/pd-input-area/div/div/textarea",
            "Campo Observação");

        campoObservacao.Clear();

        campoObservacao.SendKeys(observacao);

        await Task.Delay(2000);

        campoObservacao.SendKeys(Keys.Enter);

        await Task.Delay(2000);

        if (tipoInscricao == TipoInscricao.Economica)
        {
            // =========================
            // ABA CADASTRO ECONÔMICO
            // =========================

            var abaEconomico = WaitForElement(wait,
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/ul/li[3]/a",
                "Aba Cadastro Econômico");

            abaEconomico.Click();

            await Task.Delay(3000);

            // =========================
            // INSCRIÇÃO
            // =========================

            var campoInscricao = WaitForElement(wait,
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/div/div[3]/div/div/pd-autocomplete/div/div/div/div[1]/pd-input-text/input",
                "Campo Inscrição (Cadastro Econômico)");

            campoInscricao.Clear();

            campoInscricao.SendKeys(inscricao);

            await Task.Delay(2000);

            campoInscricao.SendKeys(Keys.Enter);

            await Task.Delay(3000);

            // TAB para fechar autocomplete
            campoInscricao.SendKeys(Keys.Tab);

            await Task.Delay(2000);
        }
        else
        {
            // =========================
            // ABA CADASTRO IMOBILIÁRIO
            // =========================

            var abaImobiliario = WaitForElement(wait,
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/ul/li[2]/a",
                "Aba Cadastro Imobiliário");

            abaImobiliario.Click();

            await Task.Delay(3000);

            // =========================
            // INSCRIÇÃO IMOBILIÁRIA
            // =========================

            var campoInscricaoImobiliaria = WaitForElement(wait,
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/div/div[2]/div/div[1]/pd-autocomplete/div/div/div/div/pd-input-text/input",
                "Campo Inscrição (Cadastro Imobiliário)");

            campoInscricaoImobiliaria.Clear();

            campoInscricaoImobiliaria.SendKeys(inscricao);

            await Task.Delay(2000);

            campoInscricaoImobiliaria.SendKeys(Keys.Enter);

            await Task.Delay(3000);

            // TAB para fechar autocomplete
            campoInscricaoImobiliaria.SendKeys(Keys.Tab);

            await Task.Delay(2000);
        }

        // =========================
        // SALVAR DUAM
        // =========================

        var salvarDuamButton = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]",
            "Botão Salvar DUAM");

        JsClick(driver, salvarDuamButton);

        await Task.Delay(5000);

        // =========================
        // AGUARDAR BOTÃO EDITAR
        // =========================

        WaitForElement(wait, "//button[@nat='botaoEditar']", "Botão Editar (após salvar DUAM)");

        // =========================
        // EDITAR VALOR
        // =========================

        driver.ExecuteJavaScript(@"
        var btn = document.querySelector(""button[nat='botaoEditar']"");

        if (!btn)
            throw new Error('botaoEditar não encontrado');

        var rect = btn.getBoundingClientRect();

        var cx = rect.left + rect.width / 2;

        var cy = rect.top + rect.height / 2;

        var bloqueados = [];

        var el = document.elementFromPoint(cx, cy);

        var tentativas = 15;

        while (el && el !== btn && tentativas-- > 0) {

            bloqueados.push([el, el.style.pointerEvents]);

            el.style.pointerEvents = 'none';

            el = document.elementFromPoint(cx, cy);
        }

        btn.click();

        bloqueados.forEach(function(par) {

            par[0].style.pointerEvents = par[1];
        });
    ");

        await Task.Delay(3000);

        // =========================
        // INSERIR Quantidade
        // =========================

        var campoQuantidade = WaitForElement(wait,
            "/html/body/div[1]/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[4]/pd-input-text[1]/div/div/input",
            "Campo Quantidade");

        campoQuantidade.Clear();

        campoQuantidade.SendKeys("1");

        await Task.Delay(2000);

        // =========================
        // INSERIR VALOR
        // =========================

        var campoValor = WaitForElement(wait,
            "/html/body/div[1]/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[4]/pd-input-text[2]/div/div/input",
            "Campo Valor");

        campoValor.Clear();

        campoValor.SendKeys(valor);

        await Task.Delay(2000);

        campoValor.SendKeys(Keys.Tab);

        await Task.Delay(2000);

        // =========================
        // SALVAR E SAIR DO VALOR
        // =========================

        var salvarValorButton = WaitForElement(wait,
            "/html/body/div[1]/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]",
            "Botão Salvar (valor)");

        JsClick(driver, salvarValorButton);

        await Task.Delay(3000);

        // =========================
        // SALVAR DUAM FINAL
        // =========================

        var salvarFinalButton = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]",
            "Botão Salvar DUAM (final)");

        JsClick(driver, salvarFinalButton);

        await Task.Delay(3000);

        // =========================
        // LIMPAR FORMULÁRIO
        // =========================

        var limparFormularioButton = WaitForElement(wait,
            "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[2]",
            "Botão Limpar Formulário");

        JsClick(driver, limparFormularioButton);

        await Task.Delay(2000);
    }

    private void JsClick(IWebDriver driver, IWebElement element)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(@"
        arguments[0].dispatchEvent(
            new MouseEvent('click', {
                bubbles: true,
                cancelable: true,
                view: window
            })
        );
    ", element);
    }
}