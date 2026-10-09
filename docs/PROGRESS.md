# winPaint — Progress

## Current state
- **Milestone:** M4–M8 implemented in code; working on M9/M10 (UI tests with FlaUI, visual polish, screenshots)
- **Working on:** FlaUI UI test project (U-01..U-08, T-WYSIWYG, DoD walk-through)
- **Next steps:** 1) FlaUI harness + automation bridge tests  2) T-WYSIWYG screenshot comparison  3) visual review pass (dark/light)
- **Half-finished work:** none uncommitted
- **Known failing tests:** none (Core: 71/71 passing)
- **Environment note:** the workstation is locked (LogonUI running), so SendInput and screen capture don't reach the app.
  UI tests drive the app through UI Automation patterns and the canvas automation bridge, and snapshots are rendered in-process
  (see DECISIONS.md 2026-10-08).
- **Push status:** ok (push via Git Bash; PowerShell git has no credential prompt)

Legend: `[ ]` open, `[~]` in progress, `[x]` done and verified. Evidence in parentheses.

## Features
### 5.1 Window
- [~] F-UI-01 (P0) title + dirty indicator
- [~] F-UI-02 (P0) layout
- [~] F-UI-03 (P0) Fluent theme Light/Dark/System
- [~] F-UI-04 (P1) tooltips
- [~] F-UI-05 (P1) toolbar overflow
- [~] F-UI-06 (P1) settings persistence
- [~] F-UI-07 (P1) keyboard accessibility / automation names
- [~] F-UI-08 (P1) Settings dialog + About
### 5.2 File
- [~] F-FILE-01 (P0) New
- [~] F-FILE-02 (P0) Open
- [~] F-FILE-03 (P0) Save / Save As
- [x] F-FILE-04 (P0) Save flattens output only (T-17)
- [~] F-FILE-05 (P0) format-specific handling (A-05 codecs)
- [~] F-FILE-06 (P0) unsaved-changes prompt
- [~] F-FILE-07 (P1) recent files
- [~] F-FILE-08 (P1) import from file
- [~] F-FILE-09 (P1) print / page setup / preview
- [~] F-FILE-10 (P1) set as desktop background
- [~] F-FILE-11 (P1) image properties
- [~] F-FILE-12 (P0) exit
- [~] F-FILE-13 (P1) drag-drop + command line
- [ ] F-FILE-14 (P2) scanner/camera
- [ ] F-FILE-15 (P2) share
### 5.3 Edit
- [x] F-EDIT-01 (P0) undo/redo (A-06, A-07, T-11)
- [~] F-EDIT-02 (P0) cut/copy/paste (A-08 core)
- [~] F-EDIT-03 (P1) paste text into text editor
- [~] F-EDIT-04 (P0) select all / delete
- [~] F-EDIT-05 (P1) paste from
### 5.4 Selection
- [~] F-SEL-01 (P0) rectangular selection
- [~] F-SEL-02 (P0) free-form selection
- [~] F-SEL-03 (P0) select all / invert / delete
- [x] F-SEL-04 (P0) transparent selection (A-10)
- [~] F-SEL-05 (P0) floating selection behaviors + context menu (A-11, ShiftDragStampsTrail)
- [~] F-SEL-06 (P0) ops act on selection
- [~] F-SEL-07 (P0) crop (T-14)
### 5.5 Image
- [x] F-IMG-01 (P0) rotate/flip (A-03, T-12, T-13)
- [~] F-IMG-02 (P0) resize and skew (A-04, T-13)
- [~] F-IMG-03 (P0) canvas resize by dragging
- [x] F-IMG-04 (P1) invert colors (T-22)
- [~] F-IMG-05 (P1) transparent canvas
### 5.6 Tools
- [x] F-TOOL-01 (P0) pencil (PencilDrawsAliasedAndShiftConstrains)
- [x] F-TOOL-02 (P0) eraser (EraserOpaqueVsTransparentVsColorReplace, T-07)
- [x] F-TOOL-03 (P0) fill (A-01)
- [~] F-TOOL-04 (P0) text (T-01..T-22 core)
- [x] F-TOOL-05 (P0) color picker (PickerAndMagnifier)
- [~] F-TOOL-06 (P0) magnifier
### 5.7 Brushes
- [~] F-BR-01 (P0) brush
- [~] F-BR-02 (P0) calligraphy brush
- [~] F-BR-03 (P0) calligraphy pen
- [~] F-BR-04 (P0) airbrush
- [~] F-BR-05 (P1) oil
- [~] F-BR-06 (P1) crayon
- [~] F-BR-07 (P1) marker
- [~] F-BR-08 (P1) natural pencil
- [~] F-BR-09 (P1) watercolor
### 5.8 Shapes
- [~] F-SH-01 (P0) 23 shapes (A-02)
- [~] F-SH-02 (P0) curve
- [~] F-SH-03 (P0) polygon
- [~] F-SH-04 (P0) shift constrain
- [~] F-SH-05 (P0) outline/fill styles
- [~] F-SH-06 (P0) post-draw adjustment
- [~] F-SH-07 (P0) outline width
### 5.9 Size/colors
- [~] F-SIZE-01 (P0) size control
- [~] F-SIZE-02 (P1) opacity
- [~] F-COL-01 (P0) color 1/2 + swap
- [~] F-COL-02 (P0) palette + custom colors
- [~] F-COL-03 (P0) edit colors dialog
- [ ] F-COL-04 (P2) screen eyedropper
### 5.10 View
- [~] F-VIEW-01 (P0) zoom
- [~] F-VIEW-02 (P0) scroll/pan
- [~] F-VIEW-03 (P0) rulers
- [~] F-VIEW-04 (P0) gridlines
- [~] F-VIEW-05 (P0) status bar
- [~] F-VIEW-06 (P1) full screen
- [~] F-VIEW-07 (P1) thumbnail
- [~] F-VIEW-08 (P0) fast rendering
### 5.11 Layers
- [~] F-LAY-01 (P0) layers panel
- [~] F-LAY-02 (P0) layer operations (T-19)
- [~] F-LAY-03 (P1) opacity + blend modes (A-09)
- [~] F-LAY-04 (P0) tools act on active layer
- [~] F-LAY-05 (P0) layer undo (A-06)
- [~] F-LAY-06 (P1) flatten image
### 5.12 Shortcuts
- [~] Shortcut table + help dialog
### 5.13 Robustness
- [~] F-ROB-01 (P0) crash handler
- [~] F-ROB-02 (P0) open errors
- [~] F-ROB-03 (P1) large images (LargeImage10000Works)
- [~] F-ROB-04 (P1) multi-instance

## Text acceptance tests (6.9)
- [x] T-01 - [x] T-02 - [x] T-03 - [x] T-04 - [x] T-05 - [x] T-06 - [x] T-07 - [x] T-08
- [x] T-09 - [x] T-10 - [x] T-11 - [x] T-12 - [x] T-13 - [x] T-14 - [x] T-15 - [x] T-16
- [x] T-17 - [x] T-18 - [x] T-19 - [x] T-20 - [x] T-21 - [x] T-22
- [ ] T-WYSIWYG
- [x] T-PERF-TEXT

## General acceptance tests (10)
- [x] A-01 - [x] A-02 - [x] A-03 - [x] A-04 - [x] A-05 - [x] A-06
- [x] A-07 - [x] A-08 - [x] A-09 - [x] A-10 - [x] A-11 - [x] A-12
- [ ] U-01 - [ ] U-02 - [ ] U-03 - [ ] U-04 - [ ] U-05 - [ ] U-06 - [ ] U-07 - [ ] U-08
- [ ] DoD walk-through (§12.6)
