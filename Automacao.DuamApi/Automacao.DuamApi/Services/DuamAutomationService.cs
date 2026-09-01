 using ClosedXML.Excel;
using DuamApi.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;

namespace DuamApi.Services;

public class DuamAutomationService
{
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

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

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

    private async Task Login(
        IWebDriver driver,
        WebDriverWait wait,
        string usuario,
        string senha)
    {
        driver.Navigate().GoToUrl(
            "https://araguaina.prodataweb.inf.br/sig/index.html");

        var usuarioInput = wait.Until(
            d => d.FindElement(
                By.XPath("//input[@placeholder='Usuário']")));

        usuarioInput.SendKeys(usuario);

        var senhaInput = wait.Until(
            d => d.FindElement(
                By.XPath("//input[@placeholder='Senha']")));

        senhaInput.SendKeys(senha);

        var entrarButton = wait.Until(
            d => d.FindElement(
                By.XPath("//button[contains(text(), 'Entrar')]")));

        entrarButton.Click();

        await Task.Delay(5000);
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

        var campoData = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[1]/div/p/input")));

        campoData.Clear();

        campoData.SendKeys(data.ToString("dd/MM/yyyy"));

        await Task.Delay(3000);

        // =========================
        // ANO
        // =========================

        var campoAno = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[2]/div/p/input")));

        campoAno.Clear();

        campoAno.SendKeys(ano);

        await Task.Delay(1000);

        // =========================
        // MÊS
        // =========================

        var campoMes = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[1]/div[3]/div/p/input")));

        campoMes.Clear();

        campoMes.SendKeys(mes);

        await Task.Delay(3000);

        // =========================
        // RECEITA
        // =========================

        var campoReceita = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[2]/pd-autocomplete[1]/div/div/div/div[1]/pd-input-text//input")));

        campoReceita.Clear();

        campoReceita.SendKeys(receita);

        await Task.Delay(2000);

        campoReceita.SendKeys(Keys.Enter);

        await Task.Delay(2000);

        // =========================
        // OBSERVAÇÃO
        // =========================

        var campoObservacao = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[3]/pd-input-area/div/div/textarea")));

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

            var abaEconomico = wait.Until(
                d => d.FindElement(By.XPath(
                    "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/ul/li[3]/a")));

            abaEconomico.Click();

            await Task.Delay(3000);

            // =========================
            // INSCRIÇÃO
            // =========================

            var campoInscricao = wait.Until(
                d => d.FindElement(By.XPath(
                    "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/div/div[3]/div/div/pd-autocomplete/div/div/div/div[1]/pd-input-text/input")));

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

            var abaImobiliario = wait.Until(
                d => d.FindElement(By.XPath(
                    "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/ul/li[2]/a")));

            abaImobiliario.Click();

            await Task.Delay(3000);

            // =========================
            // INSCRIÇÃO IMOBILIÁRIA
            // =========================

            var campoInscricaoImobiliaria = wait.Until(
                d => d.FindElement(By.XPath(
                    "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/pd-tab/div/div/div/div[2]/div/div[1]/pd-autocomplete/div/div/div/div/pd-input-text/input")));

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

        var salvarDuamButton = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]")));

        JsClick(driver, salvarDuamButton);

        await Task.Delay(5000);

        // =========================
        // AGUARDAR BOTÃO EDITAR
        // =========================

        wait.Until(
            d => d.FindElement(By.XPath("//button[@nat='botaoEditar']")));

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

        var campoQuantidade = wait.Until(
          d => d.FindElement(By.XPath(
            "/html/body/div[1]/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[4]/pd-input-text[1]/div/div/input")));

        campoQuantidade.Clear();

        campoQuantidade.SendKeys("1");

        await Task.Delay(2000);

        // =========================
        // INSERIR VALOR
        // =========================

        var campoValor = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/div/div/pd-crud/div/div[2]/form/div[1]/pd-crud-body/div[4]/pd-input-text[2]/div/div/input")));

        campoValor.Clear();

        campoValor.SendKeys(valor);

        await Task.Delay(2000);

        campoValor.SendKeys(Keys.Tab);

        await Task.Delay(2000);

        // =========================
        // SALVAR E SAIR DO VALOR
        // =========================

        var salvarValorButton = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]")));

        JsClick(driver, salvarValorButton);

        await Task.Delay(3000);

        // =========================
        // SALVAR DUAM FINAL
        // =========================

        var salvarFinalButton = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[1]")));

        JsClick(driver, salvarFinalButton);

        await Task.Delay(3000);

        // =========================
        // LIMPAR FORMULÁRIO
        // =========================

        var limparFormularioButton = wait.Until(
            d => d.FindElement(By.XPath(
                "/html/body/div[1]/div/md-content/div/ui-view/div/pd-index-modulo/div/div/div/pd-crud/div/div[3]/nav/div/div/div[1]/div/button[2]")));

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