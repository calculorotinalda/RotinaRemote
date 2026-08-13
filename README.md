# RotinaRemote — Plataforma Profissional de Acesso Remoto para Windows

O **RotinaRemote** é uma solução completa, profissional e nativa em **C# / .NET 8 LTS** para assistência e controlo remoto através da Internet e redes locais.

## Funcionalidades Principais
- 💻 **Cliente Nativo Windows (WPF):** Interface moderna com tema escuro premium, suporte a DPI adaptativo e System Tray.
- 🆔 **ID de Dispositivo de 9 dígitos:** Gerado com base no hardware local e encriptado via DPAPI (`identity.dat`).
- 🔐 **Segurança E2EE Avançada:** Troca de chaves ECDH e cifra AES-256-GCM em todos os canais de comunicação.
- 🎥 **Streaming de Ecrã de Alta Performance:** Captura DXGI Desktop Duplication API (com fallback GDI+) e compressão adaptativa JPEG.
- 🖱️ **Controlo Real de Rato e Teclado:** Injeção de eventos com coordenadas normalizadas via Win32 `SendInput`.
- 📁 **Transferência de Ficheiros:** Upload/Download por chunks de 64KB com validação SHA-256 e suporte a pausa/retoma.
- 🌐 **NAT Traversal STUN & Servidores de Sinalização/Relay:** Conexão direta P2P com fallback automático para Relay em redes empresariais.
- 📋 **Sincronização de Clipboard & Diagnóstico Integrado:** Ferramentas de verificação de rede e exportação de relatório `RotinaRemote-Diagnostic.txt`.

## Estrutura do Projeto
```text
RotinaRemote/
├── RotinaRemote.sln
├── src/
│   ├── RotinaRemote.Client/          (Aplicação WPF)
│   ├── RotinaRemote.Core/            (Regras de negócio e Logger)
│   ├── RotinaRemote.Protocol/        (Framing e serialização de rede)
│   ├── RotinaRemote.Security/        (Criptografia e Device ID)
│   ├── RotinaRemote.Network/         (Sockets P2P e cliente STUN)
│   ├── RotinaRemote.Screen/          (Captura de ecrã DXGI/GDI)
│   ├── RotinaRemote.Input/           (Injeção Win32 SendInput)
│   ├── RotinaRemote.FileTransfer/    (Motor de ficheiros)
│   ├── RotinaRemote.SignalingServer/ (Servidor Kestrel WebSockets)
│   └── RotinaRemote.RelayServer/     (Servidor Relay TCP)
├── tests/
│   └── RotinaRemote.UnitTests/       (Suíte de testes xUnit)
├── installer/
│   └── RotinaRemote.iss              (Script Inno Setup)
└── scripts/
    └── build.ps1                     (Script de automação)
```

## Como Executar o Build
Abra a consola do PowerShell e execute:
```powershell
.\scripts\build.ps1
```
# RotinaRemote
