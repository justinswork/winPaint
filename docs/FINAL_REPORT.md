# winPaint — Final report

winPaint is a WPF (.NET 10, Fluent theme, MVVM) recreation of Windows 11 Paint without its AI/cloud features. Its key
difference: every text box stays a **live, re-editable text object** for the whole session. Double-click it with the
Select or Text tool to change the words, box, font, size, B/I/U/S, colors or background, or delete it. Pixels painted
after the text stay on top of it, pixel for pixel. Saved files are always plain flattened images.

## 1. Build and tests (Definition of Done §12.1)
| Check | Result |
|---|---|
| `dotnet build -c Release` | 0 warnings, 0 errors (warnings are errors; analyzers at `latest-recommended`) |
| Core tests (`tests/WinPaint.Core.Tests`, xUnit, headless) | **79 / 79 passed** |
| UI tests (`tests/WinPaint.UiTests`, FlaUI/UIA3) | **26 / 26 passed** (see §7 for the final run) |
| `grep -rn "TODO\|NotImplemented\|FIXME\|HACK" src/` | no matches |

## 2. Feature coverage (§12.2)
| Priority | IDs in the spec | Implemented and verified | Coverage |
|---|---|---|---|
| P0 | 56 | 56 | 100 % |
| P1 | 27 | 27 | 100 % |
| P2 (optional) | 3 | 0 | — |
| **All non-AI features** | **86** | **83** | **96.5 %** (target ≥ 90 %) |

Every P0/P1 ID is `[x]` in `docs/PROGRESS.md` with the test and/or screenshot that proves it. Not implemented (P2,
optional): scanner/camera import (F-FILE-14), share sheet (F-FILE-15), screen-wide eyedropper (F-COL-04).

All §6.9 text tests (T-01…T-22, T-WYSIWYG, T-PERF-TEXT) and all §10 tests (A-01…A-12, U-01…U-08) exist and pass,
plus the §12.6 walk-through (`TextUiTests.DoD_WalkThrough`, screenshots `DoD_01`…`DoD_10`).

## 3. Performance (§9, recorded by the tests in `artifacts/perf/` and `artifacts/screenshots/*.txt`)
| Measurement | Result | Target |
|---|---|---|
| Cold start: process start → first rendered frame | **671 ms** | < 1.5 s |
| T-PERF-TEXT: edit bottom-most of 30 texts under 30 strokes, 3840×2160 | **1.97 ms / keystroke** | < 16 ms (×3 CI allowance) |
| F-VIEW-08: brush stroke on 3840×2160, per pointer move incl. compositing | **2.51 ms** (≈ 400 fps headroom) | 60 fps (16.7 ms) |
| A-01: 8 MP scanline flood fill | **39.6 ms** | < 150 ms |
| T-WYSIWYG: caret vs. rendered glyph positions at 50/100/300/800 % | **0 px** deviation | ≤ 1 px |
| Leak test: 20 open/close cycles of a 1920×1080 image | +32 MB private bytes (bounded, test limit 60 MB) | no unbounded growth |
| 10000×10000 image | opens, draws, inverts (background task, busy cursor) | works |

## 4. Architecture in brief
- **Core** (`src/WinPaint.Core`, no windows/controls): premultiplied BGRA32 pixels in sparse 256×256 copy-on-write tiles
  (uniform tiles cost no memory); each layer is an element stack `[segment, text, segment, …]`; text renders through one
  `FormattedText` layout engine shared with the editor; history stores cheap snapshots that share tiles, with a memory
  budget (default 1 GB); tools are UI-independent and talk to an `IToolHost`.
- **App** (`src/WinPaint.App`): MVVM (CommunityToolkit.Mvvm), a custom `CanvasView` (dirty-rectangle
  `WriteableBitmap` pipeline, nearest-neighbor zoom, rulers, gridlines, overlays) and the text editor overlay: a
  transparent-glyph `TextBox` with the same metrics and transform as the text object, so edit mode and committed mode
  are identical by construction.

## 5. Known limitations
- P2 features above are not implemented.
- The development workstation was locked for this whole run, so UI tests drive the app through UI Automation patterns
  and the canvas's automation interface (pointer scripts routed through the same handlers as the mouse) instead of
  physical input, and screenshots are rendered in-process (window content; native dialogs and context-menu popups are
  not part of those images). Keyboard shortcuts are injected through the same routing the window's KeyDown uses.
- The system clipboard could not be opened in that session; the in-app clipboard fallback was exercised by U-08.
  Clipboard formats (PNG/DIB/bitmap, alpha kept) are verified by A-08.
- Printing was exercised up to the system print dialog (cancelled) plus page setup and print preview; nothing was sent
  to a printer. Set-as-desktop-background was exercised up to the save-first prompt; the tests never change the real
  wallpaper.
- Formatting applies to a whole text box (per spec), not to individual characters.
- A text object merged down from a layer with a non-Normal blend mode or < 100 % opacity is flattened (by design).
- Fill results, and selections lifted over text, are pixels: later text edits don't recompute them (by design).

## 6. Decisions summary (full log: `docs/DECISIONS.md`)
- Snapshot history with shared copy-on-write tiles → every command undoable byte-for-byte, memory = changed tiles.
- Transparent erasing above a text object writes an erase mask that keeps erasing it after edits (T-07).
- Own BMP (32-bit V5 with alpha), GIF (octree + LZW, binary transparency) and ICO encoders.
- 100 % zoom = one image pixel per physical screen pixel.
- Ctrl+Plus/Minus: size for size-adjustable tools, zoom otherwise; text box caret placed at the double-click point;
  Ctrl+Z inside the text box is text-level undo, Edit ▸ Undo commits the session first.
- Whole-image Invert acts on the active layer (text colors inverted too); Black-and-white on all layers.
- Selection lift + move + drop is one undo step; lifting over live text flattens it inside that same step (T-15).
- The TextBox's built-in 2 px text margin is cancelled so caret and glyphs coincide exactly.
- Heavy whole-image operations on images above 4K run on a background task with a busy cursor.
- Settings in `%AppData%\winPaint\settings.json`; crash logs in `%LocalAppData%\winPaint\logs`.

## 7. Final audit (§12)
| # | Item | Status |
|---|---|---|
| 1 | Release build 0/0; all tests green | see §1 |
| 2 | Every P0/P1 `[x]` with evidence | `docs/PROGRESS.md` |
| 3 | Every §6.9 and §10 test exists and passes | yes |
| 4 | No TODO/NotImplemented/FIXME/HACK in `src/` | yes |
| 5 | Screenshots cover every UI test and were reviewed | `artifacts/screenshots/` (issues found and fixed during review: dialog snapshots cropped, selection Invert colors had no effect, compact status bar overflow, toolbar text-color indicator) |
| 6 | Scripted §12.6 walk-through with screenshots | `DoD_01`…`DoD_10` |
| 7 | This report | yes |
| 8 | Committed and pushed, clean tree | yes |
