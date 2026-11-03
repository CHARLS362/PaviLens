# Script interactivo para configurar de forma segura las claves y endpoints de Meta AI
# Las claves se almacenan en dotnet user-secrets (fuera del repositorio Git) para evitar fugas.

param (
    [string]$Provider = "",
    [string]$ReasoningBaseUrl = "",
    [string]$ReasoningApiKey = "",
    [string]$ReasoningModel = "muse-wonder",
    [string]$SegmentationBaseUrl = "",
    [string]$SegmentationApiKey = "",
    [string]$SegmentationModel = "sam-3.1"
)

$projectPath = "backend/src/PaveLens.Api"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  PaveLens AI - Configuración de Claves y Modelos Meta AI " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $projectPath)) {
    Write-Host "Error: No se encontró la ruta del proyecto $projectPath" -ForegroundColor Red
    exit 1
}

# 1. Modo de ejecución: Simulación o Meta Real
if ([string]::IsNullOrWhiteSpace($Provider)) {
    Write-Host "Selecciona el proveedor de IA:" -ForegroundColor Yellow
    Write-Host "  [1] Meta (Modelos reales: SAM 3.1 + Muse Wonder / Llama Vision)" -ForegroundColor White
    Write-Host "  [2] Simulation (Modo offline / desarrollo sin gastar créditos)" -ForegroundColor White
    $choice = Read-Host "Opción [1 o 2] (Por defecto: 1)"
    if ($choice -eq "2") {
        $Provider = "Simulation"
    } else {
        $Provider = "Meta"
    }
}

if ($Provider -eq "Simulation") {
    dotnet user-secrets set "Ai:Provider" "Simulation" --project $projectPath
    Write-Host "Configurado en modo: Simulation." -ForegroundColor Green
    Write-Host "Los análisis usarán los escenarios deterministas precargados." -ForegroundColor Gray
    exit 0
}

# 2. Configurar Razonamiento (Muse Wonder / Llama 3.2 Vision)
Write-Host ""
Write-Host "--- 1. Servicio de Razonamiento (Muse Wonder / Llama Vision) ---" -ForegroundColor Yellow
if ([string]::IsNullOrWhiteSpace($ReasoningBaseUrl)) {
    Write-Host "Introduce la URL base del endpoint compatible OpenAI:" -ForegroundColor White
    Write-Host "  Ejemplos:" -ForegroundColor Gray
    Write-Host "   - Together AI: https://api.together.xyz/v1/" -ForegroundColor Gray
    Write-Host "   - Groq:        https://api.groq.com/openai/v1/" -ForegroundColor Gray
    Write-Host "   - OpenRouter:  https://openrouter.ai/api/v1/" -ForegroundColor Gray
    Write-Host "   - Llama Stack: http://localhost:5001/v1/" -ForegroundColor Gray
    $ReasoningBaseUrl = Read-Host "URL Base (Enter para https://api.together.xyz/v1/)"
    if ([string]::IsNullOrWhiteSpace($ReasoningBaseUrl)) {
        $ReasoningBaseUrl = "https://api.together.xyz/v1/"
    }
}

if ([string]::IsNullOrWhiteSpace($ReasoningApiKey)) {
    $ReasoningApiKey = Read-Host "Introduce tu API Key de Meta / Inferencia (secreto)" -AsSecureString
    $ReasoningApiKey = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ReasoningApiKey))
}

if ([string]::IsNullOrWhiteSpace($ReasoningModel)) {
    $ReasoningModel = Read-Host "Nombre del modelo (Enter para 'meta-llama/Llama-3.2-11B-Vision-Instruct')"
    if ([string]::IsNullOrWhiteSpace($ReasoningModel)) {
        $ReasoningModel = "meta-llama/Llama-3.2-11B-Vision-Instruct"
    }
}

# 3. Configurar Segmentación (SAM 3.1)
Write-Host ""
Write-Host "--- 2. Servicio de Segmentación (Meta SAM 3.1) ---" -ForegroundColor Yellow
if ([string]::IsNullOrWhiteSpace($SegmentationBaseUrl)) {
    Write-Host "Introduce la URL del servicio de SAM 3.1 (sidecar FastAPI o nube):" -ForegroundColor White
    Write-Host "  Ejemplo local: http://localhost:8001/" -ForegroundColor Gray
    $SegmentationBaseUrl = Read-Host "URL Base SAM (Enter para http://localhost:8001/)"
    if ([string]::IsNullOrWhiteSpace($SegmentationBaseUrl)) {
        $SegmentationBaseUrl = "http://localhost:8001/"
    }
}

# Aplicar a dotnet user-secrets
Write-Host ""
Write-Host "Guardando configuración en dotnet user-secrets..." -ForegroundColor Yellow

dotnet user-secrets set "Ai:Provider" "Meta" --project $projectPath
dotnet user-secrets set "Ai:Meta:Reasoning:BaseUrl" $ReasoningBaseUrl --project $projectPath
dotnet user-secrets set "Ai:Meta:Reasoning:ApiKey" $ReasoningApiKey --project $projectPath
dotnet user-secrets set "Ai:Meta:Reasoning:Model" $ReasoningModel --project $projectPath
dotnet user-secrets set "Ai:Meta:Segmentation:BaseUrl" $SegmentationBaseUrl --project $projectPath
dotnet user-secrets set "Ai:Meta:Segmentation:Model" $SegmentationModel --project $projectPath

if (-not [string]::IsNullOrWhiteSpace($SegmentationApiKey)) {
    dotnet user-secrets set "Ai:Meta:Segmentation:ApiKey" $SegmentationApiKey --project $projectPath
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Configuración guardada exitosamente en User Secrets!     " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Tus claves quedan protegidas fuera de Git y listas para usar." -ForegroundColor White

