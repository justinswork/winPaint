# winPaint — Progress

## Current state
- **Milestone:** M11 — final audit
- **Working on:** running the full UI suite on the latest build; final report
- **Next steps:** 1) full `dotnet test` run  2) fill FINAL_REPORT.md numbers  3) final commit + push
- **Half-finished work:** none uncommitted
- **Known failing tests:** none known (Core 79/79; UI 19/19 on the previous build, new MoreUiTests pending)
- **Environment note:** the workstation is locked (LogonUI running); UI tests drive the app via UI Automation and the
  canvas automation interface, screenshots are rendered in-process (DECISIONS.md). The system clipboard can't be opened
  in this session; winPaint falls back to an in-app clipboard (DECISIONS.md).
- **Push status:** ok (push via Git Bash)

Legend: `[ ]` open, `[~]` in progress, `[x]` done and verified. Evidence: Core test (`tests/WinPaint.Core.Tests`),
UI test (`tests/WinPaint.UiTests`), screenshot (`artifacts/screenshots/…png`).

## Features
### 5.1 Window
- [x] F-UI-01 (P0) title + dirty indicator — U07_UnsavedPromptOnClose (asserts `*` title); U-05 screenshots
- [x] F-UI-02 (P0) layout — U-06_light.png, U-01_brushes.png
- [x] F-UI-03 (P0) Fluent theme Light/Dark/System — U06_DarkAndLight; U-06_dark.png, U-06_light.png, U-06_switched_to_dark.png
- [x] F-UI-04 (P1) tooltips — ToolbarTooltipsAndNames
- [x] F-UI-05 (P1) toolbar overflow — View_CompactToolbar…; F-UI-05_compact_toolbar.png
- [x] F-UI-06 (P1) settings persistence — SettingsPersistAcrossSessions
- [x] F-UI-07 (P1) keyboard accessibility / automation names — ToolbarTooltipsAndNames, U08 (focusable controls, shortcuts)
- [x] F-UI-08 (P1) Settings dialog + About — Dialogs_…; F-UI-08_settings_about.png
### 5.2 File
- [x] F-FILE-01 (P0) New — U08 (Ctrl+N with prompt), DoD_01_new_image.png
- [x] F-FILE-02 (P0) Open — OpenImportDropExitFlows, U05 (command line), A05 codecs
- [x] F-FILE-03 (P0) Save / Save As — U05, DoD_WalkThrough, FormatWarnings
- [x] F-FILE-04 (P0) Save flattens output only — T17, U05
- [x] F-FILE-05 (P0) format-specific handling — A05_*, FormatWarnings (JPEG/GIF warnings, 32-bit BMP), Files_IcoSave (ICO sizes + padding warning); F-FILE-05_*.png
- [x] F-FILE-06 (P0) unsaved-changes prompt — U07, U08 (New), OpenImportDropExitFlows (Open, drop, exit)
- [x] F-FILE-07 (P1) recent files — Files_IcoSave…, OpenImportDropExitFlows (missing file message); F-FILE-07_*.png
- [x] F-FILE-08 (P1) import from file — OpenImportDropExitFlows (enlarge prompt); F-FILE-08_imported_enlarged.png
- [x] F-FILE-09 (P1) print / page setup / preview — U08 (Ctrl+P → system print dialog), Dialogs_… ; F-FILE-09_*.png
- [x] F-FILE-10 (P1) set as desktop background — Files_IcoSave… (save-first prompt); F-FILE-10_wallpaper_save_first.png (the real wallpaper is not changed by tests)
- [x] F-FILE-11 (P1) image properties — Dialogs_… (size in px/cm, B&W); F-FILE-11_*.png
- [x] F-FILE-12 (P0) exit — OpenImportDropExitFlows (File ▸ Exit with prompt)
- [x] F-FILE-13 (P1) drag-drop + command line — OpenImportDropExitFlows (drop path), U05/DoD (command line)
- [ ] F-FILE-14 (P2) scanner/camera — not implemented (optional)
- [ ] F-FILE-15 (P2) share — not implemented (optional)
### 5.3 Edit
- [x] F-EDIT-01 (P0) undo/redo — A06, A07, T11, U08
- [x] F-EDIT-02 (P0) cut/copy/paste — A08, U08
- [x] F-EDIT-03 (P1) paste text into text editor (native TextBox paste); clipboard text ignored elsewhere — ClipboardTextIsIgnoredForImages
- [x] F-EDIT-04 (P0) select all / delete — U08, InvertSelectionAndDelete
- [x] F-EDIT-05 (P1) paste from — OpenImportDropExitFlows
### 5.4 Selection
- [x] F-SEL-01 (P0) rectangular selection — RectSelectionShiftSquareAndHandleResize, U08 (nudge)
- [x] F-SEL-02 (P0) free-form selection — Selection_FreeForm…; F-SEL-02_free_form_selected.png
- [x] F-SEL-03 (P0) select all / invert / delete — InvertSelectionAndDelete, U08
- [x] F-SEL-04 (P0) transparent selection — A10; F-SEL-04_transparent_selection.png
- [x] F-SEL-05 (P0) floating selection behaviors + context menu — A11, ShiftDragStampsTrail, Selection_…; F-SEL-05_*.png
- [x] F-SEL-06 (P0) ops act on selection — SelectionOnlyOperationsInvertRotate, Selection_… (context menu invert)
- [x] F-SEL-07 (P0) crop — T14, FreeFormCropFillsOutsideWithSecondary, U08
### 5.5 Image
- [x] F-IMG-01 (P0) rotate/flip — A03, T12, T13, DoD
- [x] F-IMG-02 (P0) resize and skew — A04, T13, Dialogs_…; F-IMG-02_*.png
- [x] F-IMG-03 (P0) canvas resize by dragging — View_…CanvasResize; F-IMG-03_canvas_resized.png
- [x] F-IMG-04 (P1) invert colors — T22, U08
- [x] F-IMG-05 (P1) transparent canvas — View_…TransparentCanvas; F-IMG-05_transparent_canvas.png
### 5.6 Tools
- [x] F-TOOL-01 (P0) pencil — PencilDrawsAliasedAndShiftConstrains, U02/U04
- [x] F-TOOL-02 (P0) eraser — EraserOpaqueVsTransparentVsColorReplace, T07
- [x] F-TOOL-03 (P0) fill — A01, A01_FillToolUsesActiveLayerComposite
- [x] F-TOOL-04 (P0) text — T-01..T-22, U05, DoD, TextToolbar_FormattingAndDelete
- [x] F-TOOL-05 (P0) color picker — PickerAndMagnifier
- [x] F-TOOL-06 (P0) magnifier — PickerAndMagnifier
### 5.7 Brushes
- [x] F-BR-01..F-BR-09 (P0/P1) all nine brushes — U01_DrawWithEachBrush (U-01_brushes.png), AllBrushesPaintAndAreDeterministic, FastStrokesHaveNoGaps, MarkerDoesNotAccumulateWithinStroke
### 5.8 Shapes
- [x] F-SH-01 (P0) 23 shapes — A02, U02 (U-02_shapes.png)
- [x] F-SH-02 (P0) curve — CurveAndPolygonInteractions, U02
- [x] F-SH-03 (P0) polygon — CurveAndPolygonInteractions, PolygonKeepsEveryClickedVertex, U02
- [x] F-SH-04 (P0) shift constrain — A02
- [x] F-SH-05 (P0) outline/fill styles — A02_AllShapesRasterizeWithEveryStyle, ShapeRightDragSwapsColors
- [x] F-SH-06 (P0) post-draw adjustment — ShapeToolFloatsAndAdjustsThenCommits, U02b; U-02_shape_floating_with_handles.png
- [x] F-SH-07 (P0) outline width — ShapeToolFloatsAndAdjustsThenCommits
### 5.9 Size/colors
- [x] F-SIZE-01 (P0) size control — U08 (Ctrl+Plus/Minus), SizeOpacityAndEditColorsCustom
- [x] F-SIZE-02 (P1) opacity — OpacityAppliesToStroke, SizeOpacityAndEditColorsCustom
- [x] F-COL-01 (P0) color 1/2 + swap — U08 (X), DoD
- [x] F-COL-02 (P0) palette + custom colors — SizeOpacityAndEditColorsCustom, SettingsPersistAcrossSessions
- [x] F-COL-03 (P0) edit colors dialog — U03; U-03_edit_colors_dialog.png
- [ ] F-COL-04 (P2) screen eyedropper — not implemented (optional)
### 5.10 View
- [x] F-VIEW-01 (P0) zoom — A12, ZoomAt_KeepsAnchorFixed, U04, U08
- [x] F-VIEW-02 (P0) scroll/pan — ScrollAndPan
- [x] F-VIEW-03 (P0) rulers — F-VIEW-03_rulers.png, U08
- [x] F-VIEW-04 (P0) gridlines — U04; U-04_zoom800_gridlines.png
- [x] F-VIEW-05 (P0) status bar — ScrollAndPan (toggle); status bar in all screenshots
- [x] F-VIEW-06 (P1) full screen — U08; U-08_full_screen.png
- [x] F-VIEW-07 (P1) thumbnail — View_…; F-VIEW-07_thumbnail.png
- [x] F-VIEW-08 (P0) fast rendering — StrokeRenderingIsFastAt4K (artifacts/perf/F-VIEW-08-stroke.txt)
### 5.11 Layers
- [x] F-LAY-01 (P0) layers panel — Layers_PanelOperations; F-LAY-01_layers_panel.png
- [x] F-LAY-02 (P0) layer operations — T19, Layers_PanelOperations
- [x] F-LAY-03 (P1) opacity + blend modes — A09, A06 (layer ops), LayerItemViewModel
- [x] F-LAY-04 (P0) tools act on active layer — T19, Layers_PanelOperations
- [x] F-LAY-05 (P0) layer undo — A06, Layers_PanelOperations
- [x] F-LAY-06 (P1) flatten image — Layers_PanelOperations; F-LAY-06_*.png
### 5.12 Shortcuts
- [x] Shortcut table + help dialog — U08; U-08_shortcuts_dialog.png
### 5.13 Robustness
- [x] F-ROB-01 (P0) crash handler — Robustness_… ; F-ROB-01_crash_dialog.png
- [x] F-ROB-02 (P0) open errors — Decode_CorruptFileThrowsFriendlyException, Robustness_…; F-ROB-02_open_error.png
- [x] F-ROB-03 (P1) large images — LargeImage10000Works, Robustness_… (10000×10000); F-ROB-03_large_image.png
- [x] F-ROB-04 (P1) multi-instance — MultiInstanceAndStartupTime

## Text acceptance tests (6.9)
- [x] T-01 - [x] T-02 - [x] T-03 - [x] T-04 - [x] T-05 - [x] T-06 - [x] T-07 - [x] T-08
- [x] T-09 - [x] T-10 - [x] T-11 - [x] T-12 - [x] T-13 - [x] T-14 - [x] T-15 - [x] T-16
- [x] T-17 - [x] T-18 - [x] T-19 - [x] T-20 - [x] T-21 - [x] T-22
- [x] T-WYSIWYG — TextUiTests.TWysiwyg; T-WYSIWYG_{0.5,1,3,8}_{edit,committed}.png
- [x] T-PERF-TEXT — TextIntegrationTests.TPerfText (artifacts/perf/T-PERF-TEXT.txt)

## General acceptance tests (10)
- [x] A-01 - [x] A-02 - [x] A-03 - [x] A-04 - [x] A-05 - [x] A-06
- [x] A-07 - [x] A-08 - [x] A-09 - [x] A-10 - [x] A-11 - [x] A-12
- [x] U-01 - [x] U-02 - [x] U-03 - [x] U-04 - [x] U-05 - [x] U-06 - [x] U-07 - [x] U-08
- [x] DoD walk-through (§12.6) — TextUiTests.DoD_WalkThrough; DoD_01..DoD_10 screenshots
