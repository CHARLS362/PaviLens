# PaveLens AI - starts the API (.NET) and the web app (Angular) in two windows.
# Usage, from the repository root:   .\start.ps1
# If PowerShell blocks scripts:       powershell -ExecutionPolicy Bypass -File .\start.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Fail($message) {
    Write-Host "  x $message" -ForegroundColor Red
    exit 1
}

function Read-DotEnv($path) {
    $values = @{}
    foreach ($line in Get-Content $path) {
        $line = $line.Trim()
        if ($line -eq '' -or $line.StartsWith('#') -or -not $line.Contains('=')) { continue }
        $key, $value = $line.Split('=', 2)
        $values[$key.Trim()] = $value.Trim().Trim('"').Trim("'")
    }
    return $values
}

Write-Host '=========================================================' -ForegroundColor Cyan
Write-Host '  PaveLens AI - Meta Global AI Developer Hackathon' -ForegroundColor Cyan
Write-Host '=========================================================' -ForegroundColor Cyan

# 1. Prerequisites
Write-Host '[1/4] Checking prerequisites...' -ForegroundColor Yellow
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail '.NET SDK not found. Install .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0' }
$sdks = dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) { Fail ".NET 10 SDK required (found: $($sdks -join ', ')). https://dotnet.microsoft.com/download/dotnet/10.0" }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Fail 'Node.js not found. Install Node 22.22+ or 24 LTS: https://nodejs.org' }
$nodeMajor = [int]((node --version).TrimStart('v').Split('.')[0])
if ($nodeMajor -lt 22) { Fail "Node.js 22.22+ or 24 required (found $(node --version))." }
Write-Host "  ok .NET $((dotnet --version)) - Node $(node --version)" -ForegroundColor Green

# 2. Configuration
Write-Host '[2/4] Configuration (.env)...' -ForegroundColor Yellow
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $root '.env.example') $envFile
    Write-Host '  ok .env created from .env.example (demo mode, no keys needed)' -ForegroundColor Green
} else {
    Write-Host '  ok using existing .env' -ForegroundColor Green
}
$config = Read-DotEnv $envFile
$provider = if ($config['PAVELENS_AI_PROVIDER']) { $config['PAVELENS_AI_PROVIDER'] } else { 'Auto' }
$segmentation = if ($config['PAVELENS_SEGMENTATION_SERVICE']) { $config['PAVELENS_SEGMENTATION_SERVICE'] } else { 'Auto' }
# Auto: a Meta key alone turns on the real models, with SAM 3.1 on Meta unless a Roboflow key is set.
if ($provider -eq 'Auto') { $provider = if ($config['META_API_KEY']) { 'Meta' } else { 'Simulation' } }
if ($segmentation -eq 'Auto') { $segmentation = if ($config['ROBOFLOW_API_KEY']) { 'Roboflow' } else { 'Meta' } }
if ($provider -eq 'Meta') {
    if (-not $config['META_API_KEY']) { Fail 'PAVELENS_AI_PROVIDER=Meta needs META_API_KEY in .env' }
    if ($segmentation -eq 'Roboflow' -and -not $config['ROBOFLOW_API_KEY']) { Fail 'PAVELENS_SEGMENTATION_SERVICE=Roboflow needs ROBOFLOW_API_KEY in .env' }
    Write-Host "  ok real Meta models - segmentation: $segmentation" -ForegroundColor Green
} else {
    Write-Host '  ok demo mode (Simulation). Put your META_API_KEY in .env to use the real models.' -ForegroundColor Green
}

# 3. Dependencies
Write-Host '[3/4] Installing dependencies (first run only)...' -ForegroundColor Yellow
if (-not (Test-Path (Join-Path $root 'frontend/node_modules'))) {
    Push-Location (Join-Path $root 'frontend'); npm ci; $code = $LASTEXITCODE; Pop-Location
    if ($code -ne 0) { Fail 'npm ci failed in frontend/' }
}
if ($provider -eq 'Meta' -and $segmentation -eq 'Meta' -and -not (Test-Path (Join-Path $root 'backend/tools/sam31/node_modules'))) {
    Push-Location (Join-Path $root 'backend/tools/sam31'); npm ci; $code = $LASTEXITCODE; Pop-Location
    if ($code -ne 0) { Fail 'npm ci failed in backend/tools/sam31/' }
}
Write-Host '  ok' -ForegroundColor Green

# 4. Start
Write-Host '[4/4] Starting the API and the web app in new windows...' -ForegroundColor Yellow
Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command', 'dotnet run --project backend/src/PaveLens.Api --launch-profile http'
Start-Process powershell -WorkingDirectory (Join-Path $root 'frontend') -ArgumentList '-NoExit', '-Command', 'npm start'

Write-Host ''
Write-Host '  Web app      http://localhost:4200   (ready in ~30 s on the first build)' -ForegroundColor White
Write-Host '  API          http://localhost:5000' -ForegroundColor White
Write-Host '  API docs     http://localhost:5000/scalar' -ForegroundColor White
Write-Host '  AI status    http://localhost:5000/api/v1/ai/status' -ForegroundColor White
Write-Host '=========================================================' -ForegroundColor Cyan
