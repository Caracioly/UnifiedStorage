# Unified Storage v1.1.1

## Summary
This release focuses on terminal UX improvements and safer inventory behavior under real storage constraints.

## What's new
- Added a slot usage bar and `used / total` counter in the terminal view.
- Added integrated search input to quickly filter items in the terminal inventory.
- Added storage-capacity checks to block deposits when all linked chests are full.
- Added terminal hover text showing how many nearby chests are currently in range.
- Improved interaction handling and container scanning flow around terminal sessions.
- Improved session projection/refresh consistency between client UI and server-authoritative snapshots.
- Switched operation-heavy logging to opt-in development logging (`EnableDevLogs`).

## Notes
- Multiplayer remains server-authoritative for reservation, commit/cancel, and delta sync.
- Vanilla chest behavior is still preserved outside terminal-specific interactions.
