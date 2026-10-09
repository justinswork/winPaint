# Decisions log

- **2026-10-08** Pin SDK to .NET 10 via `global.json` (machine also has .NET 11 RC installed, which would otherwise be picked up).
- **2026-10-08** Text formatting applies to the whole text box (per prompt §6.2).
- **2026-10-08** Fill bucket results are stored as pixels; later text edits don't recompute earlier fills (§6.5).
