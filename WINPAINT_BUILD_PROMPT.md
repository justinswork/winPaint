# Build Prompt: winPaint — a WPF clone of Windows 11 Paint with re-editable text

> **How to use this file:** Open Claude Code in `C:\Users\perso\source\winPaint` and say:
> *"Read WINPAINT_BUILD_PROMPT.md in full and carry it out. Follow the Operating Rules exactly."*
>
> **To resume after an interruption**, say:
> *"Continue the winPaint build. Read WINPAINT_BUILD_PROMPT.md and follow the resume procedure in Section 13."*

---

## 0. Your mission (read this first)

You are a senior C#/WPF engineer. Build **winPaint**, a Windows desktop app that recreates **Windows 11 Microsoft Paint**
(the current version, *not including* its AI or cloud features) in **C# on .NET 10 with WPF**.

Two goals matter more than anything else:

1. **Feature parity.** winPaint must have **at least 90% of Windows 11 Paint's non-AI functionality**. Section 5 lists those features. Every **P0** and **P1** item must be fully built. Together, P0 and P1 make up the 90%+ target.
2. **The key difference: re-editable text.** In real Paint, text is permanently burned into the pixels once you click away from the text box. In winPaint, each text box stays a **live text object** for as long as the image is open. The user can **double-click it later** (with the Select tool or the Text tool) to reopen it. They can then change the words, move and resize the box, change every formatting option, or delete it. The text **keeps its place in the stacking order**: anything painted after it stays on top of it, pixel for pixel, no matter how the text changes. This feature has to be bulletproof. Section 6 specifies it exactly.

Saved files are always plain flattened images. When a saved file is reopened, it is just pixels, and its text cannot be edited. **No sidecar files, no project format, no metadata that keeps the text objects.**

You are running autonomously in Claude Code. **Keep working until every item in Section 12, the Definition of Done, is true.** Do not stop at a "first version," a "skeleton," or an "MVP." Do not ask the user questions. When something is ambiguous, make the decision that best matches Windows 11 Paint's behavior, write it down in `docs/DECISIONS.md`, and keep going.

---

## 1. Operating rules (mandatory)

1. **Planning files first.** Before writing any app code, create these files:
   - `docs/PLAN.md`: architecture plus the milestone plan (use Section 11 as the base).
   - `docs/PROGRESS.md`: a checklist that includes **every feature ID from Section 5** and **every acceptance test ID from Section 6.9 and Section 10**. Each item gets `[ ]` (open), `[~]` (in progress), or `[x]` (done and verified).
   - `docs/DECISIONS.md`: a dated log of every judgment call you make.
2. **Resumability.** Your context may get compacted, or your session may end without warning (usage limits, a crash, the user closing the terminal). Keep a **"Current state" section at the top of `docs/PROGRESS.md`** that is always accurate. It must say: the current milestone, the task you're working on right now, the next 3 concrete steps, any half-finished work and where it is, and known failing tests and why. Update it before starting each task and whenever you commit. After a compaction or a new session, follow the **resume procedure in Section 13** before doing anything else.
3. **Git.** This folder is **already a git repository**. Do **not** run `git init`, do not create or switch branches, and do not rewrite history (no rebase, amend, reset --hard, or force-push). Work on the branch that is checked out when you start, and commit there. In your first commit, add a proper .NET/Visual Studio `.gitignore` (bin/, obj/, .vs/, *.user, TestResults/, and so on). Keep `artifacts/screenshots/` committed so the evidence survives.
   - **Commit small and often.** Make a commit whenever a coherent piece builds and its tests pass. Aim for at least one commit every ~30–45 minutes of work, not just one per milestone. Every commit must build. Use clear messages, for example `M3: text element stack + T-05/T-06 passing (F-TOOL-04)`.
   - **Every commit that finishes a step also updates `docs/PROGRESS.md`** in the same commit, so the repository on its own always tells the next session exactly where things stand.
   - **Push after every commit.** The repository has a remote named `origin` (GitHub). Right after each commit, run `git push`; the very first push is `git push -u origin <current-branch>`. Push only the current branch, and **never** use `--force` or `--force-with-lease`.
     - If the push is rejected because the remote has new commits, run `git pull --no-rebase`, resolve any conflicts, make sure it still builds and passes the tests, commit the merge, and push again.
     - If the push fails for any other reason (network, authentication, GitHub outage), don't stop and don't retry in a loop. Log it in the "Current state" section of `docs/PROGRESS.md`, keep working, and try again after the next commit. Any commits that are still unpushed go up with the next successful push.
   - **Never leave uncommitted work for long.** Work that hasn't been committed is the work most at risk of being lost if the session ends (see Section 13).
4. **Build and test gate.** After every meaningful change, run `dotnet build -c Debug` (must finish with **0 errors and 0 warnings**, because warnings are errors) and `dotnet test`. A milestone is done only when both pass.
5. **No fakes.** No stub menu items, `NotImplementedException`, "coming soon" dialogs, or `// TODO` in shipped code paths. If something is visible in the UI, it must work. Before you declare completion, run `grep -rn "TODO\|NotImplemented\|FIXME\|HACK" src/` and fix everything it finds.
6. **Never weaken tests to make them pass.** If a test is wrong, fix it and record the reason in `DECISIONS.md`. Never delete or skip a failing test to get a green run.
7. **Verify visually.** Your UI tests (FlaUI) must save screenshots of key states to `artifacts/screenshots/`. **Look at those screenshots yourself**: you can read image files. Use them to confirm the UI looks like Windows 11 Paint and that the text editor overlay lines up with the rendered text.
8. **Stay in scope.** Do not add the AI features listed in Section 4.2. Do not add any way to save editable text to disk.
9. **Don't stop early.** If you think you're done, run the full Definition-of-Done audit in Section 12. If any item fails, keep working.

---

## 2. Technology constraints

| Item | Requirement |
|---|---|
| Language | C# (latest version that ships with the .NET 10 SDK) |
| Runtime | **.NET 10**, target framework `net10.0-windows`, `<UseWPF>true</UseWPF>` |
| UI | **WPF** only (no WinForms or WinUI UI; WinForms/Win32 interop is allowed only where WPF has no API, such as wallpaper or scanner) |
| Theme | WPF's built-in **Fluent theme** through `Application.ThemeMode` / `Window.ThemeMode` (`System`, `Light`, `Dark`). If the API is still marked experimental, suppress diagnostic `WPF0001` *only* where it's needed. |
| Pattern | **MVVM** using `CommunityToolkit.Mvvm`. Pointer and canvas input handling may live in a custom control. Business logic must not live in code-behind. |
| Packages | **Free/OSS NuGet packages only.** Allowed: `CommunityToolkit.Mvvm`, `xunit` (or xunit.v3), `FluentAssertions` 7.x or `Shouldly`, `FlaUI.UIA3`, `Microsoft.NET.Test.Sdk`. Anything else needs an OSI-approved license, and you must justify it in `DECISIONS.md`. **No paid or commercial UI suites.** |
| Project settings | `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`, built-in .NET analyzers at `latest-recommended` |
| DPI | Per-monitor DPI aware (PerMonitorV2 in `app.manifest`) |
| Platform | Windows 10 1809+ / Windows 11, x64 and ARM64 |

**Trademark and asset note:** Do not copy Microsoft's Paint artwork. Draw your own icons as XAML vector `Path`s, or use glyphs from the **Segoe Fluent Icons** system font, falling back to **Segoe MDL2 Assets**. The app name is **winPaint** everywhere.

---

## 3. Solution layout

```
winPaint/
  winPaint.sln
  Directory.Build.props            # shared settings: nullable, warnings-as-errors, LangVersion, analyzers
  src/
    WinPaint.Core/                 # net10.0-windows, UseWPF (for imaging/text APIs) — NO Window/Control types
      Imaging/                     # pixel buffers, tiles, blending, flood fill, resampling, codecs
      Document/                    # PaintDocument, Layer, LayerElement, RasterSegment, TextObject
      Tools/                       # tool logic (input → document mutations), independent of WPF controls
      Shapes/                      # shape geometry generators
      Brushes/                     # brush stamp/texture engines
      History/                     # undo/redo commands, memory budget
      Text/                        # text layout & rendering (shared by editor overlay + renderer)
    WinPaint.App/                  # WPF exe: Views, ViewModels, Controls (CanvasView), Dialogs, Themes, Resources
  tests/
    WinPaint.Core.Tests/           # xUnit; headless; run WPF imaging on STA threads where needed
    WinPaint.UiTests/              # FlaUI end-to-end tests; save screenshots to artifacts/screenshots/
  docs/  PLAN.md  PROGRESS.md  DECISIONS.md  FINAL_REPORT.md
  artifacts/screenshots/
```

Keep `WinPaint.Core` testable without a window. Pixel algorithms (fill, blend, erase, transform) should run on your own BGRA32/PBGRA32 buffers, so they can be unit-tested deterministically.

---

## 4. Scope

### 4.1 Reference
The reference is **Windows 11 Paint as of 2025–2026**, which includes layers, transparency support, the brush and shape sets, dark mode, and the zoom/view features. **When you're unsure how Paint behaves, match Windows 11 Paint.** If you can't tell, pick the most reasonable behavior and log it.

### 4.2 Explicitly OUT of scope (do not build)
- Cocreator, Image Creator, Generative Erase, Generative Fill, AI/one-click background removal, sticker generator, Copilot/Microsoft account features, and anything else that needs a cloud service or ML model.
- Paint's native `.paint` project file format, or **any** format that preserves layers or editable text on disk.
- Microsoft Store/OneDrive integration.

---

## 5. Feature specification (the 90% checklist)

Priorities: **P0** = core and must exist early. **P1** = required for completion. **P2** = optional, do it only after everything else is done.
**Every P0 and P1 item is required for the Definition of Done.** Copy every ID into `docs/PROGRESS.md`.

### 5.1 Window, layout, and chrome
- **F-UI-01 (P0)** Main window titled `<filename> - winPaint`, or `Untitled - winPaint`. Show an unsaved-changes indicator (`*` prefix).
- **F-UI-02 (P0)** Windows 11 Paint–style layout: a top **menu bar** (File, Edit, View) and a **toolbar/ribbon strip** below it with groups: *Selection*, *Image*, *Tools*, *Brushes*, *Shapes*, *Size*, *Colors*, *Layers toggle*. Then the canvas area with a neutral surround, an optional **Layers panel** docked on the right, and a **status bar** at the bottom.
- **F-UI-03 (P0)** Fluent theme with **Light / Dark / System** modes, selectable under View or Settings, and persisted. The canvas surround color matches the theme. The image itself never changes with the theme.
- **F-UI-04 (P1)** Tooltips on every toolbar control that show the name and shortcut.
- **F-UI-05 (P1)** Toolbar groups collapse gracefully when the window is narrow (overflow into dropdowns), the same way Paint compacts.
- **F-UI-06 (P1)** The app remembers window size/position/state, theme, the visibility of rulers, gridlines, status bar, and layers panel, the last-used tool, brush/shape settings, both colors, and custom colors. Store them in `%AppData%\winPaint\settings.json`.
- **F-UI-07 (P1)** Full keyboard accessibility: every command is reachable, focus is visible, and UI Automation names are set on all controls (FlaUI needs these too).
- **F-UI-08 (P1)** A **Settings** page/dialog (gear icon) with the theme choice and an About section (version, .NET version).

### 5.2 File menu
- **F-FILE-01 (P0)** **New** (Ctrl+N): a blank white canvas at a default of 1152×648 or the last-used size, with a single background layer.
- **F-FILE-02 (P0)** **Open** (Ctrl+O): PNG, JPEG/JPG/JPE/JFIF, BMP/DIB, GIF (first frame), TIFF/TIF (first page), ICO (largest frame). Opening a file always produces a single plain raster layer.
- **F-FILE-03 (P0)** **Save** (Ctrl+S) and **Save As** (F12 and Ctrl+Shift+S) to PNG, JPEG, BMP, GIF, TIFF, and ICO. The default format for a new image is PNG.
- **F-FILE-04 (P0)** Save **flattens** all visible layers and live text into pixels for the output file *only*. **The in-memory document is unchanged**: text stays editable for the rest of the session. The dirty flag is cleared.
- **F-FILE-05 (P0)** Format-specific handling:
  - JPEG: flatten onto white (no alpha), quality 90. Before the first lossy save, show a warning (like Paint's) when the image has transparency.
  - BMP: 24-bit by default, or 32-bit when the image has transparency.
  - GIF: quantize to 256 colors, keep binary transparency, and warn that color quality may be reduced.
  - ICO: offer sizes from 16 to 256. Downscale with high quality. If the image isn't square, warn and pad it to a square.
  - TIFF and PNG: lossless with alpha.
- **F-FILE-06 (P0)** **Unsaved-changes prompt** (Save / Don't save / Cancel) on New, Open, opening a recent file, closing the window, and drag-dropping a file.
- **F-FILE-07 (P1)** **Recent files** list (10 entries, pinned at the top of the File menu or in a submenu). Missing files are removed with a message.
- **F-FILE-08 (P1)** **Import to canvas → From a file**: pastes the image as a floating selection at the top-left of the visible area. If it's larger than the canvas, ask whether to enlarge the canvas, the same as Paint does.
- **F-FILE-09 (P1)** **Print** (Ctrl+P) using the WPF `PrintDialog`. Add **Page setup**: orientation, margins, scaling (Adjust to N% or Fit to X by Y pages), and center horizontally/vertically. Add a **print preview** window.
- **F-FILE-10 (P1)** **Set as desktop background** → Fill / Tile / Center. The document must be saved first (prompt if it isn't). Use `SystemParametersInfo(SPI_SETDESKWALLPAPER)` and the registry `WallpaperStyle`/`TileWallpaper` values.
- **F-FILE-11 (P1)** **Image properties** (Ctrl+E): shows file attributes (last saved, size on disk, resolution in DPI). Lets the user set width and height in **pixels, inches, or centimeters** and choose **Color / Black and white** (B&W converts the image after a confirmation). Changing the size changes the *canvas* size anchored at the top-left, without scaling.
- **F-FILE-12 (P0)** **Exit**, with the unsaved-changes prompt.
- **F-FILE-13 (P1)** Open a file by **drag and drop** onto the window, and from the **command line** (`winPaint.exe "C:\path\img.png"`).
- **F-FILE-14 (P2)** Import from scanner or camera (WIA).
- **F-FILE-15 (P2)** Share through the Windows share sheet (DataTransferManager interop).

### 5.3 Edit menu and clipboard
- **F-EDIT-01 (P0)** **Undo** (Ctrl+Z) and **Redo** (Ctrl+Y, Ctrl+Shift+Z), with **at least 100 steps** under a configurable memory budget (default 1 GB) that drops the oldest steps first. *Every* document change can be undone, including every text-object operation (Section 6.7).
- **F-EDIT-02 (P0)** **Cut** (Ctrl+X), **Copy** (Ctrl+C), **Paste** (Ctrl+V). Use the system clipboard with interop: write PNG (with alpha), `DeviceIndependentBitmap`, and the standard WPF bitmap. On read, prefer PNG, then DIB/DIBV5, then bitmap. Paste creates a **floating selection** at the top-left of the visible canvas area. If the pasted image is larger than the canvas, offer to enlarge the canvas.
- **F-EDIT-03 (P1)** Paste plain text from the clipboard while editing a text object (inserted into the text box). Outside the text editor, clipboard text is ignored. Don't create text objects from clipboard text.
- **F-EDIT-04 (P0)** **Select all** (Ctrl+A) and **Delete** (Del). Delete clears the selection to the secondary color on an opaque background layer, or to transparent on a transparent layer.
- **F-EDIT-05 (P1)** **Paste from** file (same as F-FILE-08).

### 5.4 Selection group
- **F-SEL-01 (P0)** **Rectangular selection**: marching-ants outline and 8 resize handles. Drag the inside to move it. Shift constrains to a square. Arrow keys nudge by 1 px, and Shift+Arrow by 10 px.
- **F-SEL-02 (P0)** **Free-form selection** (lasso).
- **F-SEL-03 (P0)** **Select all**, **Invert selection**, **Delete**.
- **F-SEL-04 (P0)** **Transparent selection** toggle: when it's on, pixels matching the secondary color in a lifted selection are treated as transparent.
- **F-SEL-05 (P0)** Floating selection behaviors: move, resize by handles (resample), **Ctrl+drag duplicates**, **Shift+drag stamps a trail** (classic Paint behavior), Esc or a click outside commits. A right-click context menu offers Cut, Copy, Paste, Crop, Select all, Invert selection, Delete, Rotate, Flip, Resize, and **Invert color** (Ctrl+Shift+I).
- **F-SEL-06 (P0)** Rotate, Flip, Resize/Skew, and Invert Color act on **the selection only** when one exists, and on the whole image otherwise.
- **F-SEL-07 (P0)** **Crop** (Ctrl+Shift+X) to the selection's bounding box. For a free-form selection, the area outside the shape becomes the secondary color, or transparent on a transparent layer.

### 5.5 Image group
- **F-IMG-01 (P0)** **Rotate** right 90°, left 90°, and 180°. **Flip** vertical and horizontal.
- **F-IMG-02 (P0)** **Resize and Skew** (Ctrl+W) dialog: resize by Percentage or Pixels, a Maintain aspect ratio checkbox, and Skew horizontal/vertical in degrees (−89…89). Resample with high quality (bicubic or Fant). Keep pixel art sharp: use nearest-neighbor for exact integer upscales.
- **F-IMG-03 (P0)** **Canvas resize by dragging** the handles on the bottom edge, right edge, and bottom-right corner, with a live size readout in the status bar. New areas are filled with the secondary color on the background layer and are transparent on the other layers.
- **F-IMG-04 (P1)** **Invert colors** for the whole image (when there's no selection).
- **F-IMG-05 (P1)** **Transparent canvas**: the user can make the background layer transparent, shown as a checkerboard.

### 5.6 Tools group
- **F-TOOL-01 (P0)** **Pencil**: a hard-edged, aliased line at the current size (1 px default, the size slider adjusts it). Left button uses the primary color and right button uses the secondary color. Shift+drag constrains to horizontal, vertical, or 45°.
- **F-TOOL-02 (P0)** **Eraser**: on an opaque background layer it paints the **secondary color**. On a transparent layer it erases to **transparent**. It has a square tip and a size setting. **Right-drag replaces only the primary color with the secondary color** (classic Paint color-eraser behavior).
- **F-TOOL-03 (P0)** **Fill (bucket)**: a 4-connected flood fill with exact color match (tolerance 0, like Paint), using the primary color on left click and the secondary color on right click. It reads the active layer's composited pixels. Use a scanline algorithm that completes in under 150 ms on an 8-megapixel image.
- **F-TOOL-04 (P0)** **Text**: see Section 6.
- **F-TOOL-05 (P0)** **Color picker**: a left click sets the primary color and a right click sets the secondary color, sampled from the visible composited canvas. It then switches back to the previous tool.
- **F-TOOL-06 (P0)** **Magnifier**: a left click zooms in one step centered on the cursor, and a right click zooms out.

### 5.7 Brushes group (dropdown; each with its own look)
All brushes: left = primary color, right = secondary color. Size comes from the Size control. Opacity comes from the Opacity control (F-SIZE-02). Strokes are smooth and anti-aliased, except Pencil. Interpolate stamps along the path so fast strokes have no gaps.
- **F-BR-01 (P0)** Brush (round, soft-edged antialiased)
- **F-BR-02 (P0)** Calligraphy brush (flat nib at 45°, width varies with direction)
- **F-BR-03 (P0)** Calligraphy pen (flat nib at −45°)
- **F-BR-04 (P0)** Airbrush (random spray dots that keep building while the mouse is held still, timer-driven)
- **F-BR-05 (P1)** Oil brush (textured, streaky)
- **F-BR-06 (P1)** Crayon (grainy, paper-texture dropout)
- **F-BR-07 (P1)** Marker (semi-transparent, flat, *non-accumulating within one stroke*)
- **F-BR-08 (P1)** Natural pencil (thin, grainy, slightly gray)
- **F-BR-09 (P1)** Watercolor brush (soft, translucent, wet edges)

Use deterministic seeded randomness so brush output can be tested.

### 5.8 Shapes group
- **F-SH-01 (P0)** Shapes: **Line, Curve, Oval, Rectangle, Rounded rectangle, Polygon, Triangle, Right triangle, Diamond, Pentagon, Hexagon, Right arrow, Left arrow, Up arrow, Down arrow, Four-point star, Five-point star, Six-point star, Rounded rectangular callout, Oval callout, Cloud callout, Heart, Lightning**. That's all 23.
- **F-SH-02 (P0)** **Curve**: drag a line, then click/drag up to two times to bend it (Bezier), the same as Paint.
- **F-SH-03 (P0)** **Polygon**: click-click-click, and double-click or click the start point to close.
- **F-SH-04 (P0)** **Shift** constrains to a circle, square, or regular shape, or 45° lines.
- **F-SH-05 (P0)** **Outline** options: No outline, Solid color, Crayon, Marker, Oil, Natural pencil, Watercolor. **Fill** options: No fill, Solid color, Crayon, Marker, Oil, Natural pencil, Watercolor. The outline uses the primary color and the fill uses the secondary color. A right-drag swaps them.
- **F-SH-06 (P0)** **Post-draw adjustment**, as in Paint: after you draw a shape, it stays a *floating shape* with a bounding box and handles. You can move or resize it, and change the outline/fill/size/colors to update it live. It commits (rasterizes) when you click outside it, press Enter or Esc, or switch tools. Committed shapes are pixels. **Only text gets the persistent-object treatment.**
- **F-SH-07 (P0)** Outline width comes from the Size control.

### 5.9 Size, opacity, and colors
- **F-SIZE-01 (P0)** **Size** control: a slider plus a numeric box (1–100 px) with a preview. **Ctrl+Plus/Ctrl+Minus** (and Ctrl+NumPad +/−) change it by 1.
- **F-SIZE-02 (P1)** **Opacity** slider (1–100%) for brushes, pencil, and shapes.
- **F-COL-01 (P0)** **Color 1 (primary)** and **Color 2 (secondary)** swatches. Click one to make it active for palette clicks. Add a swap button and the shortcut **X**.
- **F-COL-02 (P0)** A **palette of 20 default colors** (Paint's standard two rows) plus **10 custom color slots** that persist. Left-clicking a palette color sets the *active* color slot. Right-clicking sets the secondary color.
- **F-COL-03 (P0)** **Edit colors** dialog: a hue/saturation square plus a value slider (or Paint's spectrum plus luminance bar), **RGB**, **HSV** (or HSL), a **Hex** field, an alpha/opacity field when supported, a live preview of old vs new, and "Add to custom colors."
- **F-COL-04 (P2)** An eyedropper inside the Edit colors dialog that can sample anywhere on screen.

### 5.10 View
- **F-VIEW-01 (P0)** **Zoom**: 12.5%, 25%, 50%, 100%, 200%, 300%, 400%, 500%, 600%, 700%, 800%, plus any value from 1% to 800% through the status-bar slider/box. Shortcuts: Ctrl+wheel, **Ctrl+PageUp / Ctrl+PageDown**, **Ctrl+0 / Ctrl+1** (fit to window / 100%), and **Ctrl+Plus/Minus when no size-adjustable tool is active**. If Ctrl+Plus conflicts with F-SIZE-01, follow Paint and log the decision. Zoom **anchors on the cursor** for wheel zoom and on the viewport center otherwise. Render pixels with **nearest-neighbor** when zoomed in.
- **F-VIEW-02 (P0)** Scroll and pan: scrollbars, the mouse wheel (vertical), Shift+wheel (horizontal), touchpad scrolling, and **middle-mouse or Space+drag panning**.
- **F-VIEW-03 (P0)** **Rulers** (Ctrl+R) in pixels, with cursor position markers that track zoom and scroll.
- **F-VIEW-04 (P0)** **Gridlines** (Ctrl+G): 1-px pixel grid, drawn only at zoom ≥ 400% (or Paint's threshold) so it isn't too dense.
- **F-VIEW-05 (P0)** **Status bar** toggle. The status bar shows: cursor position (x, y px), selection size, image size, file size on disk (after a save), zoom % with − / slider / + controls, and a fit-to-window button.
- **F-VIEW-06 (P1)** **Full screen** (F11): shows only the image, centered on black. Esc or a click exits.
- **F-VIEW-07 (P1)** **Thumbnail** window: a small floating view of the whole image with a viewport rectangle you can click to navigate.
- **F-VIEW-08 (P0)** Fast rendering. Brush strokes stay at 60 fps on a 3840×2160 canvas. Use dirty-rectangle invalidation and avoid re-compositing the whole image every frame.

### 5.11 Layers
- **F-LAY-01 (P0)** **Layers panel** (toggle button) with a thumbnail of each layer, visibility (eye) toggle, and selection of the active layer.
- **F-LAY-02 (P0)** **Add layer** (transparent), **Delete layer** (the last remaining layer can't be deleted), **Duplicate layer**, **Merge down**, **Move up / down** (drag to reorder and buttons), **Hide / Show**.
- **F-LAY-03 (P1)** Per-layer **opacity** (0–100%) and **blend mode** (at minimum: Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference).
- **F-LAY-04 (P0)** Tools act on the **active layer**. The color picker samples the composite. Fill reads the active layer's composite.
- **F-LAY-05 (P0)** Every layer operation can be undone.
- **F-LAY-06 (P1)** **Flatten image** (merge all), which flattens text objects too and asks for confirmation first, because live text becomes uneditable.

### 5.12 Keyboard shortcuts (the full table must work)
Make an in-app **Keyboard shortcuts** help dialog (F1 or the Help menu) that lists them all.

| Shortcut | Action | Shortcut | Action |
|---|---|---|---|
| Ctrl+N | New | Ctrl+O | Open |
| Ctrl+S | Save | F12 / Ctrl+Shift+S | Save As |
| Ctrl+P | Print | Ctrl+E | Image properties |
| Ctrl+Z | Undo | Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+X / C / V | Cut / Copy / Paste | Ctrl+A | Select all |
| Del | Delete selection or selected text object | Esc | Cancel or commit the current floating operation |
| Ctrl+W | Resize and Skew | Ctrl+Shift+X | Crop |
| Ctrl+Shift+I | Invert colors | Ctrl+R | Rulers |
| Ctrl+G | Gridlines | F11 | Full screen |
| Ctrl+PgUp / PgDn | Zoom in / out | Ctrl+wheel | Zoom at cursor |
| Ctrl+Plus / Minus | Size up / down (brush tools) | X | Swap colors |
| Arrows / Shift+Arrows | Nudge selection 1 / 10 px | Ctrl+B / I / U | Bold / Italic / Underline (text editing) |
| Ctrl+0 | Fit to window | Ctrl+1 | 100% zoom |
| Enter (text editing) | New line | Tab / Shift+Tab | Move between toolbar controls |

### 5.13 Robustness
- **F-ROB-01 (P0)** Unhandled exceptions get logged to `%LocalAppData%\winPaint\logs\` and show a friendly dialog that offers to save a recovery copy (flattened PNG) of the current image.
- **F-ROB-02 (P0)** Open errors (corrupt file, unsupported format, access denied) show clear messages and never crash the app.
- **F-ROB-03 (P1)** Images up to **10000×10000** open and work. Above about 4K, operations longer than 300 ms show a busy cursor and stay responsive.
- **F-ROB-04 (P1)** Single document per window. Opening a second instance is allowed (Paint allows it).

---

## 6. THE KEY FEATURE: live, re-editable text objects

This is the reason winPaint exists. Build it with the most care, and test it the most.

### 6.1 Concepts
- A **TextObject** is a live, non-destructive element in the document. It belongs to exactly one layer and sits at a specific position in that layer's **element stack**.
- Text objects exist **only in memory** for the current session. They are flattened when a file is saved and never written to disk in editable form. A reopened file has no text objects.
- Text objects never interfere with normal painting. To the user, the canvas looks and behaves exactly like Paint until they double-click some text.

### 6.2 TextObject data
```
TextObject
  Guid      Id
  string    Text                    // plain text, may include line breaks
  Rect      Box                     // layout box in *untransformed* text space (canvas px), width = wrap width
  Matrix    Transform               // accumulated whole-image transforms (rotate/flip/resize/skew/crop-offset); identity at creation
  string    FontFamily              // default: Segoe UI (or last used)
  double    FontSizePt              // default: 11 (or last used); allowed 1–999; dropdown 8–72
  bool      Bold, Italic, Underline, Strikethrough
  Color     Foreground              // primary color at creation
  Color     Background              // secondary color at creation
  bool      OpaqueBackground        // Paint's Transparent/Opaque background toggle
  double    Opacity                 // from F-SIZE-02 at creation
```
Formatting applies to the **whole text box** (log this decision). The box auto-grows downward as you type when the text overflows. Its width only changes when the user resizes it.

### 6.3 Creating text
1. With the **Text tool**, click (default box) or drag (custom box) on the canvas. A text editing box appears with a dashed border, resize handles, and a blinking caret. A **contextual Text toolbar** appears (font family, size, B/I/U/S, Transparent/Opaque background, and the text color, which follows Color 1).
2. The user types. Typing, caret movement, selection, Ctrl+A *inside the box*, clipboard text paste, and in-box undo (Ctrl+Z while the box has focus, for text-edit-level undo) all behave like a normal text box.
3. **Commit** happens on a click outside the box, a tool switch, **Esc**, or any command that affects the document. Commit does *not* rasterize. It creates or updates the TextObject and adds **one** undo entry.
4. If the text is empty or only whitespace when committed, the object is **discarded** and no undo entry is added. If the object already existed, committing it empty is the same as deleting it, with one undo entry.

### 6.4 Re-opening text (the core behavior)
- **Double-click** inside the transformed bounds of a live text object reopens it for editing. This works when the active tool is **Select** (rectangular or free-form) or **Text**. **Double-clicking with any other tool (pencil, brush, fill, shapes, eraser, picker, magnifier) does not open the editor.** Those tools behave normally.
- Hit testing: check the transformed box bounds. Choose the **topmost** text object at that point across all **visible** layers. Hidden layers' text can't be hit. If the text object is on a different layer than the active one, **make its layer active**.
- **No stray objects:** with the Text tool, the first click of a double-click must not leave behind a new empty text box or an undo entry. With the Select tool, it must not leave behind an empty selection that changes the document.
- Once reopened, the editor shows the **exact current text and formatting**, with the caret placed at the double-click point (or the full word selected, if you choose to imitate standard word double-click; log the choice).
- While the editor is open, the user can:
  - edit the text;
  - **move** the box (drag its border) and **resize** it (handles), which re-wraps the text live;
  - change **all formatting**: font, size, B/I/U/S, text color (clicking a palette color while editing sets the text color), background color, Transparent/Opaque;
  - press **Delete** while the *box itself* is selected (the border is focused or there's no caret in the text, for example after clicking the box border), or use a **Delete text** button on the Text toolbar, to **remove the object entirely**, which reveals whatever was beneath it. (Delete with a caret in the text deletes characters as usual.)
- **Hover affordance:** with the Select or Text tool, hovering over a live text object shows a thin dashed outline around its box (1 screen pixel, theme-aware) and an I-beam cursor, so users can find editable text. Show nothing extra with the other tools.
- The status bar says `Editing text — click outside to finish` while the editor is open.

### 6.5 Z-order semantics: "text keeps its place"
Each layer is an **ordered element stack**: `[RasterSegment₀, Text_A, RasterSegment₁, Text_B, RasterSegment₂, …]`.
- When a text object is created on a layer, it is pushed onto that layer's stack, and a **new empty RasterSegment** is pushed above it. Everything painted afterward goes into that top segment.
- The layer's composite is built bottom-to-top:
  `acc = Segment₀`
  for each element above: if it's a TextObject, `acc = Render(text) OVER acc`. If it's a RasterSegment, first `acc = acc × (1 − segment.EraseAlpha)` (transparent-erase mask), then `acc = segment.Pixels OVER acc`.
- **Invariant (test this heavily):** editing a text object (content, formatting, box, position, deletion) changes *only* that object's contribution. **All pixels from operations performed after the text object was created, including strokes, fills, shapes, pastes, and transparent erasures, stay exactly the same and stay on top of it.** All pixels from before it stay beneath it.
- Fill bucket and other pixel-reading tools read the composite at the time of the operation. The *result* is stored as pixels in the top segment. Later text edits don't recompute earlier fills. (This is deliberate and predictable. Log it.)
- Opaque-color erasing (eraser on the background layer) is ordinary painting in the top segment. Transparent erasing writes `EraseAlpha` so that it also erases the text and lower segments, and **that erasure persists after the text is edited**.
- **Memory:** Store RasterSegments as **sparse tiles** (for example 256×256 tiles allocated on first write). An empty segment costs about 0 bytes. When a text object is deleted (and the deletion is committed past the undo horizon, or immediately if the undo entry keeps enough to restore it), **merge the adjacent segments** so the stack doesn't grow forever.
- **Performance:** While a text object is being edited, cache `Below = composite(elements beneath it)` and `Above = elements above it`, so each keystroke re-composites only the text's dirty rectangle (old bounds ∪ new bounds). Typing must feel instant (<16 ms per keystroke at 4K).

### 6.6 Interactions with other features
| Operation | Effect on live text objects |
|---|---|
| **Whole-image Rotate / Flip** | Apply the same transform to every RasterSegment *and* append it to every TextObject's `Transform`. Text stays editable. The editor overlay is shown with the same transform (rotated/flipped `RenderTransform`), so the user edits text in place at its rotated angle. |
| **Whole-image Resize / Skew** | Resample the segments and append the scale/skew to each `Transform`. Text stays editable and re-renders crisply at the new scale (vector re-render, not resampled pixels). |
| **Canvas resize** (drag handles, Image Properties) | No transform. Text that ends up partly outside the canvas is clipped when rendered but kept. Text that ends up *completely* outside is kept too (a later enlargement brings it back). Log this. |
| **Crop** | Translate by −cropOrigin. Text fully outside the crop rect is **removed**. Undo restores it. Partly-outside text is kept and clipped. |
| **Whole-image Invert colors** | Invert the segments' pixels, and invert each TextObject's `Foreground` and `Background` colors. Text stays editable. |
| **Black-and-white conversion** (Image Properties) | Convert the segments, and convert the text colors using the same threshold. Text stays editable. |
| **Selection-based pixel edits** (move, cut, delete, resize, rotate, flip, or invert a *selection*; Shift-stamp; crop of a free-form selection's outside area) | Any live text object on the active layer whose bounds **intersect the lifted area** is **flattened first** (rasterized into the segment stack at its own position, then removed as an object). That happens inside the same undo step, so a single Ctrl+Z restores the editable text. Show a status-bar notice: `Text in the selection was flattened`. |
| **Copy** (no modification) | The copied pixels include the rendered text. Live objects are not changed. |
| **Paste** | Always plain pixels. Pasting never creates text objects. |
| **Layer Duplicate** | Deep-copies the text objects as **independent** editable objects. |
| **Layer Merge down** | The upper layer's element stack is placed on top of the lower layer's stack, keeping the order. Text stays editable. (If the upper layer has non-Normal blend or opacity < 100%, flatten the upper layer's text into pixels first and log this.) |
| **Layer Hide** | Its text can't be hit and isn't rendered. |
| **Layer Delete** | Its text objects are deleted. Undo restores them. |
| **Flatten image** | All text becomes pixels, after a confirmation. |
| **Save / Save As** | The flattened output is written. **The in-memory text stays editable.** |
| **Open / New** | Start a new document with no text objects. |

### 6.7 Undo/redo for text
Each of these is **one** undo step with full redo: create, edit text content (the whole edit session counts as one step), move, resize, any formatting change done in that session (the whole session is one step: before-state → after-state), delete, flattening (because of a selection op or Flatten image), and the effect of whole-image transforms on the text. Undoing an edit session restores the *previous* TextObject state exactly. Redoing reapplies it. Undo/redo while the editor is open first commits the session, then performs the undo. (Inside the TextBox, Ctrl+Z is text-level undo. Log your final choice and make it consistent.)

### 6.8 Rendering and WYSIWYG
- Use one shared text layout engine (WPF `FormattedText` with `MaxTextWidth` = box width, `TextFormattingMode.Ideal`, grayscale antialiasing, **no ClearType** on the bitmap) for both final rendering and the edit preview.
- **Recommended editor approach:** an overlay WPF `TextBox` with a **transparent foreground** (so its caret and selection highlight still show), the same font metrics, zero padding/border, the same wrap width, and transform = zoom × `Transform`. The glyphs the user sees while editing are drawn by the **real document renderer**, live. That makes edit mode and committed mode match pixel for pixel by construction. If you use a different approach, you must still pass test **T-WYSIWYG**.
- At every zoom level (12.5%–800%) and every transform, the caret and selection highlight must line up with the rendered glyphs to within 1 screen pixel.

### 6.9 Text acceptance tests (implement all of them; Core tests where possible, FlaUI where it involves the UI)
- **T-01 Create/commit:** typing "Hello" and clicking outside gives 1 TextObject and 1 undo step, and the pixels show the text.
- **T-02 Re-open with Select:** double-clicking the text with the Select tool opens the editor showing "Hello" with the same font, size, and colors.
- **T-03 Re-open with Text tool:** same as T-02, and also no extra TextObject and no extra undo entry are created.
- **T-04 Other tools don't open:** double-clicking the text with Pencil draws pencil dots and does not open the editor.
- **T-05 Keeps its place (above):** create text, paint a red stroke crossing it, edit the text to "Goodbye world". The red stroke's pixels are byte-identical to before and still on top.
- **T-06 Keeps its place (below):** paint a blue rectangle, create opaque-background text over it, switch the text to a transparent background. The blue rectangle shows through again.
- **T-07 Transparent erase persists:** on a transparent layer, create text, transparent-erase across part of it, then change the text. The erased area stays fully transparent.
- **T-08 Move/resize re-wraps:** shrinking the box width wraps the text onto more lines. Moving the box exposes the correct underlying pixels where it used to be.
- **T-09 Formatting:** change the font, size, B/I/U/S, colors, and background mode one at a time. Each change renders correctly, and the whole session undoes in a single step.
- **T-10 Delete:** deleting the text object restores the exact pixels underneath. Undo brings back the editable object.
- **T-11 Undo/redo chain:** create → edit → move → format → delete, then undo ×5 and redo ×5. The state matches at every step (compare pixel hashes plus object state).
- **T-12 Rotate 90° keeps it editable:** after rotating, double-clicking on the rotated text opens a rotated editor. Editing works and the output is rotated.
- **T-13 Flip/Resize/Skew:** the same as T-12 for flip horizontal, 200% resize (text is crisp, not blurry), and a 20° horizontal skew.
- **T-14 Crop:** cropping that includes the text shifts it and keeps it editable. Cropping that excludes it removes it, and undo restores it.
- **T-15 Selection flattens:** select a rectangle overlapping the text and move it. The text is flattened (it can't be double-clicked anymore), and one undo makes it editable again.
- **T-16 Copy includes text:** Ctrl+C on an area with text, then paste into a new image, gives the pixels and no text object.
- **T-17 Save keeps session editability:** after saving as PNG, the text is still editable in the session, and the saved PNG matches the flattened composite byte-for-byte (after decode).
- **T-18 Reopen is plain:** reopen the saved PNG and double-click where the text was. Nothing opens, and no text objects exist.
- **T-19 Layers:** text on layer 2: hide layer 2 and it can't be hit. Show it, then Merge down, and it's still editable. Duplicate gives two independent objects (edit one and the other doesn't change). Delete the layer and undo it, and the text is back.
- **T-20 Hit priority:** with two overlapping text objects, a double-click opens the topmost one.
- **T-21 Empty discard:** create a box and click away without typing. There are no objects and no undo entry.
- **T-22 Invert colors:** inverting the whole image inverts the text colors, and the text stays editable.
- **T-WYSIWYG:** at zoom 50%, 100%, 300%, and 800%, a screenshot comparison of the edit mode vs. the committed mode for the same text shows no glyph shift > 1 screen px. Save the screenshots for both states.
- **T-PERF-TEXT:** on a 3840×2160 document with 30 text objects and 30 interleaved strokes, editing the bottom-most text runs at under 16 ms per keystroke on average (measure in a Core benchmark test, with the threshold relaxed ×3 for CI noise).

---

## 7. Architecture guidance

- **PaintDocument:** canvas size, DPI, layer list, active layer, selection state, the current floating object (selection/shape/text editor), the dirty flag, and the history.
- **Layer:** Id, Name, Visible, Opacity, BlendMode, IsBackground, `List<LayerElement>` (RasterSegment | TextObject). It keeps a cached composite with dirty-rect invalidation.
- **Tiled pixel storage:** PBGRA32 (premultiplied) internally for fast compositing. Convert at import/export. Use `Span<uint>`, and `System.Numerics.Vector`/SIMD in the blend loops.
- **Compositor:** document composite = the visible layers combined with their opacity and blend. Push the dirty rectangles to the canvas `WriteableBitmap` (`WritePixels` on the dirty rect only).
- **CanvasView control:** hosts the `WriteableBitmap` image (`RenderOptions.BitmapScalingMode = NearestNeighbor`), overlay adorners (selection ants, handles, shape preview, text editor, gridlines, hover outlines), and rulers. It converts screen ↔ canvas coordinates through a single, well-tested `ViewTransform` (zoom + scroll + DPI).
- **Tools:** an `ITool` interface (`OnPointerDown/Move/Up`, `OnDoubleClick`, `OnKey`, `Cursor`, `Activate/Deactivate`). Tools change the document only through **commands** in History.
- **History:** a command pattern. Pixel commands store **tile-level before/after diffs** (only the touched tiles). Structural commands store the object state before and after. Track memory usage, and evict the oldest steps once the budget is exceeded.
- **Text subsystem:** `TextLayoutEngine` (FormattedText builder shared by the renderer and the editor), `TextRenderer` (renders to a tile region with the transform applied), `TextEditSession` (handles the overlay editor and the before/after state).
- **Codecs:** WPF `BitmapDecoder`/`BitmapEncoder` (PNG, JPEG, BMP, GIF, TIFF, ICO decode). Write the **ICO encoder** yourself (PNG-compressed entries for 256 px, BMP entries for smaller sizes) because WPF has no ICO encoder. GIF quantization should be your own (octree or median-cut) for decent quality.
- **Threading:** UI on the main thread. Heavy operations (resize, rotate of large images, open/save, flatten for save) run on a background `Task` against a snapshot and marshal back to the UI thread. Never block the UI for more than 100 ms on common operations.

---

## 8. Visual design targets

- Match the **Windows 11 Paint** look: rounded corners, Fluent/Mica-like backgrounds (Mica through `WindowBackdropType` if available, with a solid fallback), compact grouped toolbar, subtle separators, a selected-tool highlight, a split-button dropdown for Brushes and Shapes, and color swatches as circles (Win 11 style). Section labels under the groups are optional.
- Light and Dark themes must both look polished. The canvas surround is a neutral gray that differs by theme. Selection ants must stay visible on any image (use an XOR-like two-tone dash).
- Custom cursors: a crosshair for shapes and selection, a brush-size circle preview for brushes and the eraser (a square for the eraser), an I-beam for text, a bucket for fill, a dropper for the picker, and a magnifier for zoom. Build them from vector art at runtime, and scale them for DPI.
- Before declaring a milestone done, use the saved screenshots (Rule 7) to review the UI. Fix misalignment, clipped text, or inconsistent spacing.

---

## 9. Non-functional requirements
- Cold start under 1.5 s on a typical modern PC (measure it and record it in FINAL_REPORT.md).
- No memory leaks: do 20 open/close-image cycles, and the working set must not grow without bound. Unsubscribe event handlers and dispose bitmaps/streams.
- All user-facing strings are in a `.resx` resource file (English only, but ready for localization).
- Code is readable: small classes, XML doc comments on public Core APIs, and no file over about 600 lines without a good reason.

---

## 10. General acceptance tests (in addition to Section 6.9)
Core (xUnit):
- **A-01** Flood fill: exact-match region, 4-connectivity, handles the canvas edge, and 8 MP in <150 ms.
- **A-02** Each of the 23 shapes produces the expected geometry (bounding box, closed path, and point counts for polygons/stars) and a Shift-constrained variant.
- **A-03** Rotate/flip round trips: rotating 4× right equals the original, and flipping twice equals the original (byte-equal).
- **A-04** Resize: 100% is a no-op. 200% nearest-neighbor for an integer scale of pixel art is exact.
- **A-05** Codec round-trip for PNG, BMP, and TIFF is lossless. JPEG decodes with the expected dimensions. The GIF has ≤256 colors. The ICO contains the requested sizes.
- **A-06** Undo/redo for every command type returns byte-identical document states (verify with a hash).
- **A-07** History memory budget eviction works.
- **A-08** Clipboard: copy/paste round-trip keeps alpha (PNG format).
- **A-09** Layer blend modes match reference formulas for sample pixels.
- **A-10** Transparent selection excludes pixels matching the secondary color.
- **A-11** Selection Ctrl+drag duplicates, and the original pixels stay in place.
- **A-12** ViewTransform screen↔canvas round trip is exact at every zoom level.

UI (FlaUI; save a screenshot for each):
- **U-01** Launch, draw with each brush, and screenshot.
- **U-02** Each shape group item can be selected and drawn.
- **U-03** Open the Edit colors dialog, enter hex `#3366CC`, and Color 1 updates.
- **U-04** Zoom to 800% with gridlines on.
- **U-05** Full end-to-end run of the text feature: create, paint over, double-click, edit, verify, save, reopen, verify not editable.
- **U-06** Dark mode screenshot of the main window, and Light mode screenshot.
- **U-07** The unsaved-changes prompt appears on close after an edit.
- **U-08** Keyboard shortcut smoke test: every row in the 5.12 table triggers its command.

---

## 11. Milestone plan (adjust in PLAN.md, but keep the order of risk)
1. **M0 — Scaffold:** solution, projects, Directory.Build.props, `.gitignore` (in the existing repo), CI-like `build.ps1` (build + test), and the docs files. An empty window with the Fluent theme.
2. **M1 — Pixel core:** tiled buffers, blending, compositor, ViewTransform, CanvasView with zoom/pan/scroll, and the dirty-rect pipeline. Open and Save PNG.
3. **M2 — History plus the basic tools:** pencil, eraser, fill, color picker, magnifier, the colors UI, size control, undo/redo.
4. **M3 — The text object system (highest risk, so do it early):** the element stack, TextObject, editor overlay, double-click re-edit, the Section 6.5 invariants, and T-01…T-11 plus T-20, T-21. **Do not continue past M3 until those tests pass.**
5. **M4 — Selection:** rect/free-form, floating selection, clipboard, transparent selection, crop, and the selection-flattens-text rule (T-15, T-16).
6. **M5 — Image operations:** rotate/flip/resize/skew/invert/canvas resize/image properties with the text transform rules (T-12, T-13, T-14, T-22).
7. **M6 — Brushes and shapes:** all 9 brushes, 23 shapes, outline/fill styles, post-draw adjustment, opacity.
8. **M7 — Layers:** the panel, all operations, blend modes, and the text-with-layers rules (T-19).
9. **M8 — File completeness:** all codecs and their warnings, recent files, import, print and print preview, wallpaper, drag-drop, command line, the unsaved prompts (T-17, T-18).
10. **M9 — View and polish:** rulers, gridlines, status bar, full screen, thumbnail, settings persistence, keyboard shortcuts dialog, cursors, the Paint-faithful visual pass, dark/light polish, accessibility.
11. **M10 — Hardening:** performance targets (T-PERF-TEXT, F-VIEW-08), large images, leak test, crash handling, the full UI test suite, and the screenshot review.
12. **M11 — Final audit** (Section 12).

---

## 12. Definition of Done (audit this literally before stopping)
You may stop only when **all** of these are true. Record the evidence for each in `docs/FINAL_REPORT.md`:
1. `dotnet build -c Release` finishes with 0 warnings and 0 errors. `dotnet test` is 100% green (show the counts).
2. Every **P0 and P1** feature ID in Section 5 is `[x]` in PROGRESS.md, and each one links to the test or screenshot that proves it.
3. Every test in **6.9** and **10** exists and passes.
4. A grep for `TODO|NotImplemented|FIXME|HACK` in `src/` returns nothing.
5. The screenshots in `artifacts/screenshots/` cover every UI test, and you have reviewed them. Note any visual issues you fixed.
6. You've done a manual walk-through, scripted with FlaUI, of this exact scenario: *new image → draw a blue rectangle → add text "Draft v1" with the Text tool → paint a red scribble across the text → switch to the Select tool → double-click the text → change it to "Final v2", make it bold and 36 pt, and widen the box → click outside → verify the red scribble is still on top and unchanged → rotate the image 90° right → double-click the text and change its color → Save As PNG → close → reopen the PNG → double-clicking the text does nothing.* Save the screenshots of every step.
7. FINAL_REPORT.md contains: the feature coverage table (P0/P1/P2 counts and percentage), known limitations, the performance measurements, and a summary of the decisions log.
8. Everything is committed and **pushed** to `origin`, the working tree is clean, and `git status` shows the branch is up to date with its remote.

If any item fails, go back to work. **Do not end your session with a summary of what "could be done next." Do it.**

---

## 13. Resume procedure (after a compaction, an interruption, or a new session)

The session may end at any moment, for example because the usage limit was reached. Assume this will happen several times before the project is finished. A new session (or you, after a context compaction) must be able to continue with **no memory of the previous conversation**, using only the repository.

When you start, or whenever you're unsure what you were doing, do these steps in order:
1. Read this whole prompt, then `docs/PROGRESS.md` (the "Current state" section first), `docs/PLAN.md`, and `docs/DECISIONS.md`.
2. Run `git fetch`, `git status`, and `git log --oneline -15` to see what was committed last, whether there's uncommitted work, and whether any commits haven't been pushed yet. Push any unpushed commits now.
3. **If there are uncommitted changes**, they come from an interrupted session. Don't throw them away. Read the diff (`git diff`, plus any untracked files), and compare it with the "Current state" notes.
   - Run `dotnet build` and `dotnet test`.
   - If the work is coherent and builds, finish the step it was part of, get the tests passing, and commit.
   - If it's broken beyond a quick fix, save it as a **patch file** (`git diff > …`) under `docs/wip/<date>-<topic>.patch`, log the reason in `DECISIONS.md`, restore the files to the last commit with `git restore` (or `git stash` and keep the stash), and redo that step cleanly. Never silently delete someone's work.
4. Run `dotnet build` and `dotnet test` on the clean state and record the result in "Current state."
5. Continue from the next unchecked item. Don't redo work that's already marked `[x]` unless a test shows it's broken.

**Before any long-running or risky step** (large refactor, new subsystem, mass rename), commit first, so an interruption in the middle costs very little.
