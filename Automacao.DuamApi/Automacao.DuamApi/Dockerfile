# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Automacao.DuamApi.csproj .
RUN dotnet restore Automacao.DuamApi.csproj

COPY . .
RUN dotnet publish Automacao.DuamApi.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Google Chrome (headless) + dependências mínimas para o Selenium
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget gnupg unzip jq ca-certificates \
    && wget -q -O - https://dl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/google-chrome.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends google-chrome-stable \
    && rm -rf /var/lib/apt/lists/*

# chromedriver Linux compatível com a versão do Chrome recém-instalada
# (baixado em build-time via "Chrome for Testing" — evita fixar uma versão
# manualmente e divergir da versão real do Chrome instalado acima)
# Casa pela versão major: o canal stable do apt costuma estar em um patch
# mais novo do que o último espelhado na lista "known-good" do Chrome for
# Testing, e chromedriver é compatível entre patches da mesma major.
RUN CHROME_MAJOR=$(google-chrome --version | grep -oP '\d+' | head -1) \
    && DRIVER_URL=$(wget -qO- "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json" \
         | jq -r --arg v "$CHROME_MAJOR." '[.versions[] | select(.version | startswith($v)) | select(.downloads.chromedriver != null)] | last | .downloads.chromedriver[] | select(.platform=="linux64") | .url') \
    && test -n "$DRIVER_URL" || (echo "Nenhum chromedriver encontrado para Chrome major $CHROME_MAJOR" && exit 1) \
    && wget -q -O /tmp/chromedriver.zip "$DRIVER_URL" \
    && unzip -q /tmp/chromedriver.zip -d /tmp \
    && mkdir -p /app/Drivers \
    && mv /tmp/chromedriver-linux64/chromedriver /app/Drivers/chromedriver \
    && chmod +x /app/Drivers/chromedriver \
    && rm -rf /tmp/chromedriver*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Automacao.DuamApi.dll"]
