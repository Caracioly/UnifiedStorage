# Unified Storage (Valheim 0.221.12)

Mod de inventario unificado estilo "magic storage" sem crafting.

## Recursos implementados

- Hotkey global (`F8`) para abrir UI de storage.
- Leitura agregada de itens de baús estáticos no raio configurável.
- Busca por texto (case-insensitive).
- Retirada direta:
  - Clique: 1 item
  - `Shift` + clique: 1 stack (limitado por stack size/quantidade)
- Fluxo multiplayer autoritativo via RPC:
  - `US_RequestSnapshot`
  - `US_SnapshotResponse`
  - `US_WithdrawRequest`
  - `US_WithdrawResponse`

## Configuração (BepInEx)

- `HotkeyOpen = F8`
- `SearchDebounceMs = 80`
- `ScanRadius = 20`
- `SnapshotRefreshMs = 750`
- `MaxContainersScanned = 128`
- `RequireAccessCheck = true`

## Estrutura

- `src/UnifiedStorage.Mod`: plugin Valheim (BepInEx).
- `src/UnifiedStorage.Core`: lógica pura (agregação, busca, planejamento de retirada).
- `tests/UnifiedStorage.Core.Tests`: testes unitários da lógica pura.

## Build local

Defina a pasta `valheim_Data\\Managed` com a propriedade `VALHEIM_MANAGED_DIR`.

Exemplo (PowerShell):

```powershell
dotnet build src\UnifiedStorage.Mod\UnifiedStorage.Mod.csproj `
  /p:VALHEIM_MANAGED_DIR="C:\Path\To\Valheim\valheim_Data\Managed"
```

Saída esperada: `UnifiedStorage.dll` para copiar em `BepInEx\plugins`.

## Observação do ambiente atual

Neste workspace não há .NET SDK instalado, então não foi possível executar `dotnet build/test` aqui.
