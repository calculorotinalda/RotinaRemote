# RotinaRemote — Especificação do Protocolo

## Enquadramento de Pacotes (Framing Format)

```text
+-------------------+-------------------+-------------------+------------------------+
| Magic Byte (2B)   | Channel ID (1B)   | Payload Len (4B)  | Encrypted Payload (NB) |
| 0x52 0x52 ("RR")  | 0:Ctrl, 1:Video.. | UInt32 BigEndian  | AES-256-GCM + Tag      |
+-------------------+-------------------+-------------------+------------------------+
```

## Identificação dos Canais
- `0x00`: Control Channel (Handshake, Autenticação, Heartbeat, Permissões)
- `0x01`: Video Channel (Frames de ecrã comprimidos)
- `0x02`: Input Channel (Eventos de Rato e Teclado)
- `0x03`: File Channel (Upload/Download de ficheiros por blocos)
- `0x04`: Clipboard Channel (Sincronização da área de transferência)
