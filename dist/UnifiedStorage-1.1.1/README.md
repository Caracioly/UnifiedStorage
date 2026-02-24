# Unified Storage (Valheim 0.221.12)

Unified storage for Valheim using a dedicated terminal, without changing vanilla chest behavior.

## Current state (v1.1.1)

- New placeable: `Unified Chest` (via Jotunn) with custom `Interactable`.
- Opening the terminal uses Valheim's native chest UI.
- Aggregates items from nearby static vanilla chests within configured range.
- Respects real `maxStackSize` per item (no stack size mutation).
- Built-in text search in the terminal inventory.
- Slot usage bar and counter (`used / total`) with dynamic color by fill ratio.
- Native scroll support for large item lists.
- Type-based ordering with lightweight material grouping (for example: ores/metals).
- Deposit and withdraw use normal chest interactions.
- Deposit is blocked when unified storage has no available space.
- `Take all` is disabled in the terminal to avoid incorrect behavior.
- Vanilla chest UI includes `Include In Unified` toggle (default: enabled).
- Hover text shows nearby chest count when aiming at the terminal.
- Multiplayer consistency is server-authoritative with reservation/timeout flow.
- Delta broadcast keeps multiple players viewing the same terminal in sync.
- Core library (`UnifiedStorage.Core`) used for aggregation, search, withdraw/deposit planning.

## Release notes (v1.1.1)

- Added terminal UI improvements: slot usage bar + live slot counter and integrated search field.
- Added storage-capacity guardrails to prevent deposits when no chest space is available.
- Added terminal hover text with nearby chest count for better in-world feedback.
- Improved terminal interaction validation and nearby-container flow.
- Refined session and inventory refresh behavior to keep projection and server snapshot aligned.
- Replaced operation-heavy logs with opt-in development logs via `EnableDevLogs`.

## Architecture (v1.1.1)

- `UnifiedTerminal` implements `Interactable` + `Hoverable` directly (no `Container.Interact` patch needed).
- `ReflectionHelpers` centralizes all reflection-based access to Valheim internals.
- `TerminalUIManager` owns all UI creation and layout management.
- `TerminalSessionService` manages client-side session state and authoritative projection.
- `TerminalAuthorityService` handles server-side authorization, reservations, and conflict checks.

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
  - `UI/`: `TerminalUIManager` (all UI creation and layout).
  - `Server/`: `TerminalAuthorityService`, `ContainerScanner`, `ChestInclusionRules`.
  - `Session/`: `TerminalSessionService` (client-side session logic).
  - `Network/`: `TerminalRpcRoutes`, `TerminalCodec` (RPC and serialization).
  - `Shared/`: `ReflectionHelpers` (centralized reflection).
  - `Patches/`: Harmony patches for InventoryGui, InventoryGrid, ZInput, and terminal interactions.
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
