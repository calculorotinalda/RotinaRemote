# RotinaRemote — Documentação de Arquitetura

## Visão Geral da Solução

O **RotinaRemote** é uma plataforma profissional de assistência e acesso remoto nativa para Windows, desenhada para ser rápida, segura e escalável.

## Estrutura de Componentes

1. **RotinaRemote.Client (WPF / MVVM):** Aplicação de utilizador final com visualização de ecrã remoto, diálogo de autorização e gestão de preferências.
2. **RotinaRemote.Core:** Regras de negócio, logger estruturado (`log.txt`), configuração (`config.json`) e modelos de dados.
3. **RotinaRemote.Protocol:** Codificação/descodificação de frames binários multiplexados (*Control, Video, Input, File, Clipboard*).
4. **RotinaRemote.Security:** Criptografia E2EE (X25519, AES-256-GCM), identificação única de hardware persistente (`identity.dat` via DPAPI).
5. **RotinaRemote.Network:** Gestão de sockets P2P TCP/UDP, cliente STUN NAT Traversal e cliente WebSockets de sinalização.
6. **RotinaRemote.Screen:** Captura de ecrã nativa com DXGI Desktop Duplication API (com fallback GDI+), compressão JPEG adaptativa.
7. **RotinaRemote.Input:** Simulação nativa de rato e teclado via Windows API `SendInput`.
8. **RotinaRemote.FileTransfer:** Motor de transferência por blocos de 64KB com SHA-256 integrity e suporte a resume.
9. **RotinaRemote.SignalingServer:** Servidor Kestrel WebSockets na porta 5000 para negociação P2P.
10. **RotinaRemote.RelayServer:** Servidor Relay de alto débito na porta 5001 para fallback em redes restritas.
