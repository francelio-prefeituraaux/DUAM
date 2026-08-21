#!/usr/bin/env bash
# Builda o frontend e copia o resultado pra pasta que o Nginx serve.
# Roda NO SERVIDOR, de dentro desta pasta (Automacao.DuamApi.Web já clonada).
# Uso: ./deploy.sh
#
# Configuração vem de deploy.env.local (copie de deploy.env.example e preencha).
set -euo pipefail

cd "$(dirname "$0")"

CONFIG_FILE="deploy.env.local"

if [ ! -f "$CONFIG_FILE" ]; then
  echo "Erro: $CONFIG_FILE não encontrado." >&2
  echo "Copie deploy.env.example para $CONFIG_FILE e preencha com os valores reais." >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$CONFIG_FILE"

: "${VITE_API_BASE_URL:?defina VITE_API_BASE_URL em $CONFIG_FILE}"
: "${TARGET_DIR:?defina TARGET_DIR em $CONFIG_FILE}"

if ! command -v npm >/dev/null 2>&1; then
  echo "Erro: npm não encontrado neste servidor. Instale o Node.js primeiro (ver README.md)." >&2
  exit 1
fi

echo "==> Instalando dependências"
npm ci

echo "==> Build (VITE_API_BASE_URL=$VITE_API_BASE_URL)"
VITE_API_BASE_URL="$VITE_API_BASE_URL" npm run build

if [ ! -d "dist" ] || [ -z "$(ls -A dist 2>/dev/null)" ]; then
  echo "Erro: pasta dist/ vazia ou inexistente após o build." >&2
  exit 1
fi

echo "==> Copiando dist/ para $TARGET_DIR"
mkdir -p "$TARGET_DIR"
rsync -a --delete dist/ "$TARGET_DIR/"

echo "==> Deploy do frontend concluído."
