# Script de automação de build principal para o RotinaRemote
param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RootDir = Split-Path -Parent $ScriptDir
Set-Location $RootDir

$BuildLogPath = Join-Path $RootDir "build.txt"
$PublishDir = Join-Path $RootDir "publish"
$ReleasesDir = Join-Path $RootDir "Releases"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " RotinaRemote Build Automation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$LogBuilder = [System.Text.StringBuilder]::new()
[void]$LogBuilder.AppendLine("========================================")
[void]$LogBuilder.AppendLine("RotinaRemote Build")
[void]$LogBuilder.AppendLine("========================================")
[void]$LogBuilder.AppendLine("Date:")
[void]$LogBuilder.AppendLine((Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
[void]$LogBuilder.AppendLine("")
[void]$LogBuilder.AppendLine("Configuration:")
[void]$LogBuilder.AppendLine($Configuration)
[void]$LogBuilder.AppendLine("")
[void]$LogBuilder.AppendLine(".NET:")
[void]$LogBuilder.AppendLine("8.x / 9.x")
[void]$LogBuilder.AppendLine("")

function Exec-Step {
    param ([string]$StepName, [string]$Command, [string[]]$CommandArgs)
    Write-Host "==> Executando $StepName..." -ForegroundColor Yellow
    [void]$script:LogBuilder.AppendLine("--- ${StepName} ---")
    
    $output = & $Command $CommandArgs 2>&1
    foreach ($line in $output) {
        Write-Host $line
        [void]$script:LogBuilder.AppendLine($line.ToString())
    }
    
    if ($LASTEXITCODE -ne 0) {
        [void]$script:LogBuilder.AppendLine("")
        [void]$script:LogBuilder.AppendLine("${StepName} - FAILED (Exit Code: $LASTEXITCODE)")
        [void]$script:LogBuilder.AppendLine("")
        [void]$script:LogBuilder.AppendLine("Result:")
        [void]$script:LogBuilder.AppendLine("FAILED")
        $script:LogBuilder.ToString() | Out-File -FilePath $script:BuildLogPath -Encoding utf8
        throw "${StepName} falhou com o código de saída $LASTEXITCODE."
    } else {
        [void]$script:LogBuilder.AppendLine("")
        [void]$script:LogBuilder.AppendLine("${StepName} - SUCCESS")
        [void]$script:LogBuilder.AppendLine("")
    }
}

try {
    # 1. Validar .NET SDK
    Write-Host "[1/7] Validando .NET SDK..." -ForegroundColor Yellow
    $dotnetVersion = & dotnet --version
    Write-Host "     .NET SDK Detectado: $dotnetVersion" -ForegroundColor Green

    # 2. Gerar e sincronizar ícones a partir de icon.png
    Write-Host "[2/8] Sincronizando ícones a partir de icon.png..." -ForegroundColor Yellow
    $PngIcon = Join-Path $RootDir "icon.png"
    $IcoFile = Join-Path $RootDir "icon.ico"
    $ClientIco = Join-Path $RootDir "src\RotinaRemote.Client\icon.ico"
    if (Test-Path $PngIcon) {
        $ConvertScript = Join-Path $RootDir "scripts\convert-icon.ps1"
        if (Test-Path $ConvertScript) {
            & powershell -ExecutionPolicy Bypass -File $ConvertScript -PngPath $PngIcon -IcoPath $IcoFile
        }
        if (Test-Path $IcoFile) {
            Copy-Item -Path $IcoFile -Destination $ClientIco -Force
            Write-Host "     Ícone icon.ico gerado e sincronizado com sucesso!" -ForegroundColor Green
        }
    }

    # 3. Limpar builds anteriores
    Write-Host "[3/8] Limpando diretórios de output..." -ForegroundColor Yellow
    $SingleFileDir = Join-Path $RootDir "publish-singlefile"
    if (Test-Path $PublishDir) { Remove-Item -Path $PublishDir -Recurse -Force }
    if (Test-Path $SingleFileDir) { Remove-Item -Path $SingleFileDir -Recurse -Force }
    if (Test-Path $ReleasesDir) { Remove-Item -Path $ReleasesDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $SingleFileDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null

    # 4. Restaurar dependências
    Exec-Step "Restore" "dotnet" @("restore", "RotinaRemote.sln")

    # 5. Compilar solução
    Exec-Step "Build" "dotnet" @("build", "RotinaRemote.sln", "-c", $Configuration, "--no-restore")

    # 6. Executar testes unitários
    Exec-Step "Test" "dotnet" @("test", "tests/RotinaRemote.UnitTests/RotinaRemote.UnitTests.csproj", "-c", $Configuration, "--no-build")

    # 7. Publicar cliente WPF (Self-Contained para Windows x64)
    Exec-Step "Publish" "dotnet" @("publish", "src/RotinaRemote.Client/RotinaRemote.Client.csproj", "-c", $Configuration, "-r", "win-x64", "--self-contained", "true", "-o", $PublishDir)
    [void]$LogBuilder.AppendLine("EXE:")
    [void]$LogBuilder.AppendLine((Join-Path $PublishDir "RotinaRemote.exe"))
    [void]$LogBuilder.AppendLine("")

    # 7b. Publicar executável portátil Single-File (.exe único independente)
    Write-Host "==> Compilando versão portátil Single-File..." -ForegroundColor Yellow
    Exec-Step "PublishSingleFile" "dotnet" @("publish", "src/RotinaRemote.Client/RotinaRemote.Client.csproj", "-c", $Configuration, "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true", "-o", $SingleFileDir)
    
    $SingleSource = Join-Path $SingleFileDir "RotinaRemote.exe"
    $SingleDest = Join-Path $ReleasesDir "RotinaRemote-SingleFile.exe"
    $SinglePortableDest = Join-Path $ReleasesDir "RotinaRemote-Portable.exe"
    if (Test-Path $SingleSource) {
        Copy-Item -Path $SingleSource -Destination $SingleDest -Force
        Copy-Item -Path $SingleSource -Destination $SinglePortableDest -Force
        $DesktopPortable = "C:\Users\alll\Desktop\RotinaRemote-Portable.exe"
        if (Test-Path $DesktopPortable) {
            Copy-Item -Path $SingleSource -Destination $DesktopPortable -Force
            Write-Host "     Executável sincronizado no Ambiente de Trabalho: $DesktopPortable" -ForegroundColor Green
        }
        Write-Host "     Executável Portable Single-File criado em: $SinglePortableDest" -ForegroundColor Green
        [void]$LogBuilder.AppendLine("SingleFile EXE:")
        [void]$LogBuilder.AppendLine($SinglePortableDest)
    }

    # Criar versão portátil (.zip)
    $ZipPath = Join-Path $ReleasesDir "RotinaRemote-Portable.zip"
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
    Write-Host "     Versão portátil em ZIP criada em: $ZipPath" -ForegroundColor Green

    # 8. Inno Setup (Se disponível)
    $InnoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $InnoCompiler) {
        Exec-Step "Installer" $InnoCompiler @("$RootDir\installer\RotinaRemote.iss")
        $SetupSource = Join-Path $RootDir "installer\Output\RotinaRemote-Setup.exe"
        $SetupDest = Join-Path $ReleasesDir "RotinaRemote-Setup.exe"
        if (Test-Path $SetupSource) {
            Copy-Item -Path $SetupSource -Destination $SetupDest -Force
            Copy-Item -Path $SetupSource -Destination (Join-Path $RootDir "installer\RotinaRemote-Setup.exe") -Force
        }
        [void]$LogBuilder.AppendLine("Installer:")
        [void]$LogBuilder.AppendLine("installer\RotinaRemote-Setup.exe")
    } else {
        Write-Host "     ISCC.exe não encontrado em $InnoCompiler. Pulo de compilação do Setup." -ForegroundColor Warning
        [void]$LogBuilder.AppendLine("Installer:")
        [void]$LogBuilder.AppendLine("SKIPPED (Inno Setup not installed on system)")
    }

    [void]$LogBuilder.AppendLine("")
    [void]$LogBuilder.AppendLine("Result:")
    [void]$LogBuilder.AppendLine("SUCCESS")

    $LogBuilder.ToString() | Out-File -FilePath $BuildLogPath -Encoding utf8

    Write-Host "========================================" -ForegroundColor Green
    Write-Host " BUILD CONCLUÍDA COM SUCESSO!" -ForegroundColor Green
    Write-Host " Log detalhado gravado em: $BuildLogPath" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FALHA NO BUILD: $_" -ForegroundColor Red
    exit 1
}
