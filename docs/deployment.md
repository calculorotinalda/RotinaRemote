# RotinaRemote — Guia de Deployment

## Requisitos de Sistema
- Windows 10 / Windows 11 (64-bit)
- .NET 8.0 Runtime

## Compilação e Deploy Automático
Para gerar o executável nativo, a versão portátil `.zip` e o instalador oficial Inno Setup:

```powershell
.\scripts\build.ps1
```

Artefactos gerados:
- Executável Principal: `publish\RotinaRemote.exe`
- Versão Portátil: `Releases\RotinaRemote-Portable.zip`
- Instalador Windows: `installer\RotinaRemote-Setup.exe`
- Log de Build: `build.txt`
