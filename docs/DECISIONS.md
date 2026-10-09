# Decisions log

- **2026-10-08** Pin SDK to .NET 10 via `global.json` (machine also has .NET 11 RC installed, which would otherwise be picked up).
- **2026-10-08** Text formatting applies to the whole text box (per prompt §6.2).
- **2026-10-08** Fill bucket results are stored as pixels; later text edits don't recompute earlier fills (§6.5).
- **2026-10-08** History is snapshot-based: every undo step stores a `DocumentState` whose raster tiles are shared
  copy-on-write with the live document (256×256 tiles, frozen on capture, copied on first write). This makes every
  command (including text and layer operations) undoable with byte-identical results and costs memory only for tiles
  that actually changed. Memory per step = tiles introduced by that step; oldest steps are evicted past the budget.
- **2026-10-08** Tiles can be "uniform" (single value, no storage), so a blank 10000×10000 white canvas costs ~0 bytes.
- **2026-10-08** Transparent erasing in a segment above the base writes an erase mask (§6.5) in addition to clearing
  that segment's own pixels; on the base segment it just clears pixels.
- **2026-10-08** 100 % zoom = one image pixel per *physical* screen pixel (scale = zoom / DPI scale), like Paint.
- **2026-10-08** Own encoders for BMP (24-bit, or 32-bit BITMAPV5 with alpha when transparent), GIF (octree
  quantizer + LZW, binary transparency at alpha < 128) and ICO (PNG for 256 px, 32-bit DIB below). WPF has no ICO
  encoder, its GIF encoder doesn't do transparency and its BMP encoder drops alpha.
- **2026-10-08** Pencil Shift-constrain: while Shift is held the stroke is redrawn as a single straight line from the
  stroke start to the snapped end point (horizontal, vertical or 45°).
- **2026-10-08** Color picker keeps the sampled alpha (picking a transparent pixel gives a transparent color), then
  returns to the previous tool.
- **2026-10-08** Whole-image Invert colors applies to the active layer (Paint's image adjustments act on the current
  layer); its text colors are inverted. Premultiplied inversion distributes over source-over, so the result equals
  inverting the layer composite. Black-and-white conversion (Image properties) applies to every layer.
- **2026-10-08** Merge down of a hidden upper layer simply removes it (it contributed nothing visible). With a non-Normal
  blend or opacity < 100 % the upper layer's text is flattened and its blended contribution is baked into a new top
  segment of the lower layer, computed against the lower layer's composite at merge time.
- **2026-10-08** A selection lift copies the active layer's composite (all its elements) and clears the area in the top
  segment (secondary color on an opaque background, transparency + erase mask elsewhere). Lift + move + drop is one
  undo step. Selection resize by handles uses nearest-neighbor (classic Paint); Resize dialog on a selection uses high
  quality resampling.
- **2026-10-08** Text editing: Ctrl+Z inside the text box is the TextBox's own text-level undo. Edit ▸ Undo (menu or
  toolbar) while editing commits the session first and then undoes it as one step. Clicking outside the box commits
  (and does not start a new box with the same click), like Paint.
- **2026-10-08** Reopened text puts the caret at the double-click point (not word selection).
- **2026-10-08** Ctrl+Plus/Minus changes the size for size-adjustable tools (pencil, brush, eraser, shapes) and zooms
  otherwise (Paint behavior); Ctrl+PgUp/PgDn always zoom.
- **2026-10-08** `LangVersion=preview` in the App project only: CommunityToolkit.Mvvm 8.4 generates
  `[ObservableProperty]` partial properties only with "preview".
- **2026-10-08** CA1863 (cache CompositeFormat) is disabled in `.editorconfig`: format strings are localizable
  resources used once per user action.
- **2026-10-08** User-facing strings live in `Resources/Strings.resx`; `tools/gen-strings.ps1` generates both the resx
  and a small typed accessor class from `Strings.txt` (the WPF temp markup-compile project does not run the resx
  strongly-typed generator).
- **2026-10-08** The development workstation is locked during this run (LogonUI.exe active), so synthetic mouse/keyboard
  input (SendInput) and screen capture can't reach the app. UI tests therefore use FlaUI over UI Automation patterns
  (Invoke/Value/Toggle/ExpandCollapse) and the canvas's automation Value pattern, which accepts pointer scripts in canvas
  pixels and routes them through exactly the same handlers as real mouse events (`HandleDown/Move/Up`). Keyboard
  shortcuts are injected through the same routing method the window's KeyDown uses. Screenshots are rendered
  in-process (`RenderTargetBitmap` of the window), which works on a locked desktop. The automation interface is a
  legitimate accessibility/automation feature (scripted drawing), documented in `CanvasView.Automation.cs`.
- **2026-10-08** Cloud callout geometry is normalized to exactly fill its drag box.
