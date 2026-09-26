# Script de desinstalação completa e limpeza de pastas e serviço do RotinaRemote
# Executar como Administrador no Windows PowerShell

$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Desinstalador e Limpeza Total do RotinaRemote" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Terminar processos do cliente ou serviço
Write-Host "==> [1/4] A terminar processos em execução..." -ForegroundColor Yellow
try {
    Stop-Process -Name "RotinaRemote" -Force -ErrorAction SilentlyContinue
    Stop-Process -Name "RotinaRemote-Portable" -Force -ErrorAction SilentlyContinue
    Stop-Process -Name "RotinaRemote-SingleFile" -Force -ErrorAction SilentlyContinue
    Write-Host "    Processos terminados com sucesso." -ForegroundColor Green
} catch {
    Write-Host "    Aviso ao terminar processos: $_" -ForegroundColor DarkYellow
}

# 2. Parar e apagar o serviço nativo do Windows (RotinaRemoteService)
Write-Host "==> [2/4] A parar e apagar o Serviço Windows (RotinaRemoteService)..." -ForegroundColor Yellow
try {
    $serviceQuery = & sc.exe query RotinaRemoteService 2>&1
    if ($serviceQuery -notmatch "1060" -and $serviceQuery -notmatch "FAILED 1060") {
        & sc.exe stop RotinaRemoteService | Out-Null
        Start-Sleep -Seconds 1
        & sc.exe delete RotinaRemoteService | Out-Null
        Write-Host "    Serviço RotinaRemoteService parado e eliminado com sucesso." -ForegroundColor Green
    } else {
        Write-Host "    Serviço RotinaRemoteService não se encontra instalado no sistema." -ForegroundColor Cyan
    }
} catch {
    Write-Host "    Erro ao processar serviço Windows: $_" -ForegroundColor Red
}

# 3. Remover diretórios e pastas do Windows (Program Files, AppData)
Write-Host "==> [3/4] A remover pastas de instalação do Windows..." -ForegroundColor Yellow
$foldersToRemove = @(
    "C:\Program Files\RotinaRemote",
    "C:\Program Files (x86)\RotinaRemote",
    "$env:LOCALAPPDATA\RotinaRemote",
    "$env:APPDATA\RotinaRemote",
    "$env:ProgramData\RotinaRemote"
)

foreach ($folder in $foldersToRemove) {
    if (Test-Path $folder) {
        try {
            Remove-Item -Path $folder -Recurse -Force -ErrorAction Stop
            Write-Host "    Removida pasta: $folder" -ForegroundColor Green
        } catch {
            Write-Host "    Aviso: não foi possível remover completamente $folder ($_)" -ForegroundColor DarkYellow
        }
    }
}

# 4. Limpar atalhos do Ambiente de Trabalho e Menu Iniciar
Write-Host "==> [4/4] A limpar atalhos de arranque..." -ForegroundColor Yellow
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "RotinaRemote.lnk"
$programsShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "RotinaRemote.lnk"
if (Test-Path $desktopShortcut) { Remove-Item $desktopShortcut -Force -ErrorAction SilentlyContinue }
if (Test-Path $programsShortcut) { Remove-Item $programsShortcut -Force -ErrorAction SilentlyContinue }

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " RotinaRemote e respetivo serviço desinstalados com sucesso!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
