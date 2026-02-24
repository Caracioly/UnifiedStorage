# Unified Storage v1.1.2

## Summary
This release focuses on session reliability, localized item names, and lower overhead in terminal hover feedback.

## What's new
- Added localization support for item display names in terminal sessions.
- Fixed terminal session close logic to prevent incorrect/stale shutdown states.
- Optimized terminal hover text by caching nearby chest count queries.

## Notes
- Multiplayer remains server-authoritative for reservation, commit/cancel, and delta sync.
- Vanilla chest behavior remains unchanged outside terminal-specific flows.