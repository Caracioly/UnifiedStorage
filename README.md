# Unified Storage (Valheim 0.221.12)

Unified storage for Valheim using a dedicated terminal, without changing vanilla chest behavior.

## Current state (v1.1.0)

- New placeable: `Unified Chest` (via Jotunn) with custom `Interactable`.
- Opening the terminal uses Valheim's native chest UI.
- Aggregates items from nearby static vanilla chests within configured range.
- Respects real `maxStackSize` per item — no stack size mutation.
- Built-in text search in the interface.
- Native scroll support for large item lists.
- Type-based ordering with lightweight material grouping (for example: ores/metals).
- Deposit and withdraw use normal chest interactions.
- `Take all` is disabled in the terminal to avoid incorrect behavior.
- Vanilla chest UI now includes `Include In Unified` toggle (default: enabled).
- Multiplayer consistency is server-authoritative with reservation/timeout flow.
- Delta broadcast keeps multiple players viewing the same terminal in sync.
- Core library (`UnifiedStorage.Core`) used for aggregation, search, withdraw/deposit planning.

## Architecture (v1.1.0)

- `UnifiedTerminal` implements `Interactable` + `Hoverable` directly — no `Container.Interact` patch needed.
- `ReflectionHelpers` centralizes all reflection-based access to Valheim internals.
- `TerminalUIManager` owns all UI creation and layout management.
- `TerminalSessionService` manages client-side session state without stack size mutation.
- `TerminalAuthorityService` handles server-side authorization with real stack sizes.

## Scope

- No built-in crafting system.
- Vanilla chests remain unchanged.
- The terminal is the only access point to unified storage.

## Dependencies

- `denikson-BepInExPack_Valheim`
- `ValheimModding-Jotunn`

## Configuration (BepInEx)

- `ScanRadius = 20`
- `MaxContainersScanned = 128`
- `RequireAccessCheck = true`
- `EnableDevLogs = false`
- `TerminalPieceEnabled = true`
- `TerminalDisplayName = "Unified Chest"`
- `TerminalRangeOverride = 0`
- `TerminalTintEnabled = true`
- `TerminalTintColor = "#6EA84A"`
- `TerminalTintStrength = 0.35`

## Project structure

- `src/UnifiedStorage.Mod`: Valheim plugin (BepInEx + Jotunn + Harmony).
  - `Pieces/`: `UnifiedTerminal` (Interactable) and `UnifiedTerminalRegistrar`.
  - `UI/`: `TerminalUIManager` — all UI creation and layout.
  - `Server/`: `TerminalAuthorityService`, `ContainerScanner`, `ChestInclusionRules`.
  - `Session/`: `TerminalSessionService` — client-side session logic.
  - `Network/`: `TerminalRpcRoutes`, `TerminalCodec` — RPC and serialization.
  - `Shared/`: `ReflectionHelpers` — centralized reflection.
  - `Patches/`: Harmony patches for InventoryGui, InventoryGrid, ZInput.
- `src/UnifiedStorage.Core`: shared models and pure logic (aggregation, search, withdraw/deposit planning).
- `tests/UnifiedStorage.Core.Tests`: core unit tests.

## Local build

PowerShell:

```powershell
$VALHEIM_MANAGED_DIR = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
$BEPINEX_CORE_DIR = "C:\Users\<user>\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\<profile>\BepInEx\core"
$JOTUNN_DLL = "C:\Users\<user>\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\<profile>\BepInEx\plugins\ValheimModding-Jotunn\Jotunn.dll"

dotnet build .\src\UnifiedStorage.Mod\UnifiedStorage.Mod.csproj -c Release `
  /p:VALHEIM_MANAGED_DIR="$VALHEIM_MANAGED_DIR" `
  /p:BEPINEX_CORE_DIR="$BEPINEX_CORE_DIR" `
  /p:JOTUNN_DLL="$JOTUNN_DLL"
```

Output:

- `src/UnifiedStorage.Mod/bin/Release/net472/UnifiedStorage.dll`
- `src/UnifiedStorage.Core/bin/Release/netstandard2.0/UnifiedStorage.Core.dll`

## Manual install (r2modman profile)

Copy DLLs to:

`BepInEx/plugins/UnifiedStorage/`

Files:

- `UnifiedStorage.dll`
- `UnifiedStorage.Core.dll`

## Thunderstore packaging

Expected release ZIP layout:

- `manifest.json`
- `README.md`
- `icon.png`
- `plugins/UnifiedStorage/UnifiedStorage.dll`
- `plugins/UnifiedStorage/UnifiedStorage.Core.dll`
