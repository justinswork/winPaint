# winPaint — Progress

## Current state
- **Milestone:** M0 — Scaffold
- **Working on:** creating solution, projects, docs
- **Next steps:** 1) Core pixel buffers + compositor  2) CanvasView + ViewTransform  3) Open/Save PNG
- **Half-finished work:** none
- **Known failing tests:** none
- **Push status:** ok

Legend: `[ ]` open, `[~]` in progress, `[x]` done and verified.

## Features
### 5.1 Window
- [ ] F-UI-01 (P0) title + dirty indicator
- [ ] F-UI-02 (P0) layout
- [ ] F-UI-03 (P0) Fluent theme Light/Dark/System
- [ ] F-UI-04 (P1) tooltips
- [ ] F-UI-05 (P1) toolbar overflow
- [ ] F-UI-06 (P1) settings persistence
- [ ] F-UI-07 (P1) keyboard accessibility / automation names
- [ ] F-UI-08 (P1) Settings dialog + About
### 5.2 File
- [ ] F-FILE-01 (P0) New
- [ ] F-FILE-02 (P0) Open
- [ ] F-FILE-03 (P0) Save / Save As
- [ ] F-FILE-04 (P0) Save flattens output only
- [ ] F-FILE-05 (P0) format-specific handling
- [ ] F-FILE-06 (P0) unsaved-changes prompt
- [ ] F-FILE-07 (P1) recent files
- [ ] F-FILE-08 (P1) import from file
- [ ] F-FILE-09 (P1) print / page setup / preview
- [ ] F-FILE-10 (P1) set as desktop background
- [ ] F-FILE-11 (P1) image properties
- [ ] F-FILE-12 (P0) exit
- [ ] F-FILE-13 (P1) drag-drop + command line
- [ ] F-FILE-14 (P2) scanner/camera
- [ ] F-FILE-15 (P2) share
### 5.3 Edit
- [ ] F-EDIT-01 (P0) undo/redo
- [ ] F-EDIT-02 (P0) cut/copy/paste
- [ ] F-EDIT-03 (P1) paste text into text editor
- [ ] F-EDIT-04 (P0) select all / delete
- [ ] F-EDIT-05 (P1) paste from
### 5.4 Selection
- [ ] F-SEL-01 (P0) rectangular selection
- [ ] F-SEL-02 (P0) free-form selection
- [ ] F-SEL-03 (P0) select all / invert / delete
- [ ] F-SEL-04 (P0) transparent selection
- [ ] F-SEL-05 (P0) floating selection behaviors + context menu
- [ ] F-SEL-06 (P0) ops act on selection
- [ ] F-SEL-07 (P0) crop
### 5.5 Image
- [ ] F-IMG-01 (P0) rotate/flip
- [ ] F-IMG-02 (P0) resize and skew
- [ ] F-IMG-03 (P0) canvas resize by dragging
- [ ] F-IMG-04 (P1) invert colors
- [ ] F-IMG-05 (P1) transparent canvas
### 5.6 Tools
- [ ] F-TOOL-01 (P0) pencil
- [ ] F-TOOL-02 (P0) eraser
- [ ] F-TOOL-03 (P0) fill
- [ ] F-TOOL-04 (P0) text
- [ ] F-TOOL-05 (P0) color picker
- [ ] F-TOOL-06 (P0) magnifier
### 5.7 Brushes
- [ ] F-BR-01 (P0) brush
- [ ] F-BR-02 (P0) calligraphy brush
- [ ] F-BR-03 (P0) calligraphy pen
- [ ] F-BR-04 (P0) airbrush
- [ ] F-BR-05 (P1) oil
- [ ] F-BR-06 (P1) crayon
- [ ] F-BR-07 (P1) marker
- [ ] F-BR-08 (P1) natural pencil
- [ ] F-BR-09 (P1) watercolor
### 5.8 Shapes
- [ ] F-SH-01 (P0) 23 shapes
- [ ] F-SH-02 (P0) curve
- [ ] F-SH-03 (P0) polygon
- [ ] F-SH-04 (P0) shift constrain
- [ ] F-SH-05 (P0) outline/fill styles
- [ ] F-SH-06 (P0) post-draw adjustment
- [ ] F-SH-07 (P0) outline width
### 5.9 Size/colors
- [ ] F-SIZE-01 (P0) size control
- [ ] F-SIZE-02 (P1) opacity
- [ ] F-COL-01 (P0) color 1/2 + swap
- [ ] F-COL-02 (P0) palette + custom colors
- [ ] F-COL-03 (P0) edit colors dialog
- [ ] F-COL-04 (P2) screen eyedropper
### 5.10 View
- [ ] F-VIEW-01 (P0) zoom
- [ ] F-VIEW-02 (P0) scroll/pan
- [ ] F-VIEW-03 (P0) rulers
- [ ] F-VIEW-04 (P0) gridlines
- [ ] F-VIEW-05 (P0) status bar
- [ ] F-VIEW-06 (P1) full screen
- [ ] F-VIEW-07 (P1) thumbnail
- [ ] F-VIEW-08 (P0) fast rendering
### 5.11 Layers
- [ ] F-LAY-01 (P0) layers panel
- [ ] F-LAY-02 (P0) layer operations
- [ ] F-LAY-03 (P1) opacity + blend modes
- [ ] F-LAY-04 (P0) tools act on active layer
- [ ] F-LAY-05 (P0) layer undo
- [ ] F-LAY-06 (P1) flatten image
### 5.12 Shortcuts
- [ ] Shortcut table + help dialog
### 5.13 Robustness
- [ ] F-ROB-01 (P0) crash handler
- [ ] F-ROB-02 (P0) open errors
- [ ] F-ROB-03 (P1) large images
- [ ] F-ROB-04 (P1) multi-instance

## Text acceptance tests (6.9)
- [ ] T-01 - [ ] T-02 - [ ] T-03 - [ ] T-04 - [ ] T-05 - [ ] T-06 - [ ] T-07 - [ ] T-08
- [ ] T-09 - [ ] T-10 - [ ] T-11 - [ ] T-12 - [ ] T-13 - [ ] T-14 - [ ] T-15 - [ ] T-16
- [ ] T-17 - [ ] T-18 - [ ] T-19 - [ ] T-20 - [ ] T-21 - [ ] T-22
- [ ] T-WYSIWYG
- [ ] T-PERF-TEXT

## General acceptance tests (10)
- [ ] A-01 - [ ] A-02 - [ ] A-03 - [ ] A-04 - [ ] A-05 - [ ] A-06
- [ ] A-07 - [ ] A-08 - [ ] A-09 - [ ] A-10 - [ ] A-11 - [ ] A-12
- [ ] U-01 - [ ] U-02 - [ ] U-03 - [ ] U-04 - [ ] U-05 - [ ] U-06 - [ ] U-07 - [ ] U-08
- [ ] DoD walk-through (§12.6)
