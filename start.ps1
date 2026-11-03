# Start PaveLens AI Monorepo (Backend .NET + Frontend Angular)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  PaveLens AI - Meta Global AI Developer Hackathon        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Write-Host "[1/2] Iniciando Backend .NET en http://localhost:5000..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project backend/src/PaveLens.Api --launch-profile http"

Start-Sleep -Seconds 3

Write-Host "[2/2] Iniciando Frontend Angular en http://localhost:4200..." -ForegroundColor Green
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd frontend; npm start"

Write-Host ""
Write-Host "Servicios iniciados en ventanas independientes:" -ForegroundColor White
Write-Host "  - Frontend: http://localhost:4200" -ForegroundColor White
Write-Host "  - Backend API: http://localhost:5000" -ForegroundColor White
Write-Host "  - Referencia API (Scalar): http://localhost:5000/scalar" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

