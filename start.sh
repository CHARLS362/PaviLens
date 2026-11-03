#!/usr/bin/env bash
# PaveLens AI · starts the API (.NET) and the web app (Angular). Ctrl+C stops both.
# Usage, from the repository root:   ./start.sh      (or: bash start.sh)
set -euo pipefail
cd "$(dirname "$0")"
root="$(pwd)"

fail() { echo "  x $*" >&2; exit 1; }
env_value() { grep -E "^[[:space:]]*$1=" .env | tail -n 1 | cut -d= -f2- | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^["'\'']//' -e 's/["'\'']$//' || true; }

echo "========================================================="
echo "  PaveLens AI · Meta Global AI Developer Hackathon"
echo "========================================================="

echo "[1/4] Checking prerequisites..."
command -v dotnet >/dev/null || fail ".NET SDK not found. Install .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0"
dotnet --list-sdks | grep -q '^10\.' || fail ".NET 10 SDK required. https://dotnet.microsoft.com/download/dotnet/10.0"
command -v node >/dev/null || fail "Node.js not found. Install Node 22.22+ or 24 LTS: https://nodejs.org"
node_major="$(node --version | sed 's/^v//' | cut -d. -f1)"
[ "$node_major" -ge 22 ] || fail "Node.js 22.22+ or 24 required (found $(node --version))."
echo "  ok .NET $(dotnet --version) · Node $(node --version)"

echo "[2/4] Configuration (.env)..."
if [ ! -f .env ]; then
  cp .env.example .env
  echo "  ok .env created from .env.example (demo mode, no keys needed)"
else
  echo "  ok using existing .env"
fi
provider="$(env_value PAVELENS_AI_PROVIDER)"; provider="${provider:-Simulation}"
segmentation="$(env_value PAVELENS_SEGMENTATION_SERVICE)"; segmentation="${segmentation:-Roboflow}"
if [ "$provider" = "Meta" ]; then
  [ -n "$(env_value META_API_KEY)" ] || fail "PAVELENS_AI_PROVIDER=Meta needs META_API_KEY in .env"
  if [ "$segmentation" = "Roboflow" ]; then
    [ -n "$(env_value ROBOFLOW_API_KEY)" ] || fail "PAVELENS_SEGMENTATION_SERVICE=Roboflow needs ROBOFLOW_API_KEY in .env"
  fi
  echo "  ok real Meta models · segmentation: $segmentation"
else
  echo "  ok demo mode (Simulation). Set PAVELENS_AI_PROVIDER=Meta in .env to use the real models."
fi

echo "[3/4] Installing dependencies (first run only)..."
[ -d frontend/node_modules ] || (cd frontend && npm ci)
if [ "$provider" = "Meta" ] && [ "$segmentation" = "Meta" ] && [ ! -d backend/tools/sam31/node_modules ]; then
  (cd backend/tools/sam31 && npm ci)
fi
echo "  ok"

echo "[4/4] Starting the API and the web app..."
dotnet run --project backend/src/PaveLens.Api --launch-profile http &
api_pid=$!
trap 'kill "$api_pid" 2>/dev/null || true' EXIT INT TERM

echo ""
echo "  Web app      http://localhost:4200   (ready in ~30 s on the first build)"
echo "  API          http://localhost:5000"
echo "  API docs     http://localhost:5000/scalar"
echo "  AI status    http://localhost:5000/api/v1/ai/status"
echo "========================================================="
cd "$root/frontend" && npm start
