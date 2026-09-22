# RotinaRemote — Modelo de Segurança

## Identidade do Dispositivo
Cada instalação gera um **ID de 9 dígitos** (ex: `482 731 905`) derivado de um hash SHA-256 de identificadores físicos do hardware local (Motherboard, CPU, Nome do Computador) e guardado de forma encriptada usando **Windows DPAPI** (`identity.dat`).

## Criptografia Ponta-a-Ponta (E2EE)
- **Troca de Chaves:** Diffie-Hellman na curva elíptica NIST P-256 / X25519 em cada nova sessão.
- **Cifra Simétrica:** AES-256-GCM com Nonce de 96 bits e Tag de Autenticação de 128 bits.
- **Integridade:** Validação SHA-256 em ficheiros e payloads.
- **Proteção contra Replay:** Contadores de sequência incrementais por frame.
