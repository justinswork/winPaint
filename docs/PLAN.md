# winPaint — Plan

## Architecture

```
WinPaint.Core (net10.0-windows, UseWPF for imaging/text only; no Window/Control types)
  Imaging/   PixelBuffer (PBGRA32 uint[]), TiledSurface (256x256 sparse tiles), Blend (Normal/Multiply/...),
             FloodFill (scanline), Resampler (nearest / bicubic), Transforms (rotate/flip/skew), Codecs
             (WPF BitmapDecoder/Encoder + own ICO encoder + own octree GIF quantizer), ColorUtil.
  Document/  PaintDocument, Layer, LayerElement { RasterSegment | TextObject }, Compositor, Selection.
  Text/      TextLayoutEngine (FormattedText builder), TextRenderer (render into PixelBuffer w/ transform).
  History/   IHistoryCommand, HistoryManager (budget eviction), LayerSnapshot / TileDiff commands.
  Tools/     ITool + PencilTool, EraserTool, FillTool, PickerTool, MagnifierTool, BrushTool, ShapeTool,
             SelectionTool, TextTool (logic; UI-agnostic: they receive canvas-space pointer events).
  Shapes/    ShapeGeometry (23 shapes) → WPF Geometry / point lists.
  Brushes/   BrushEngine per brush kind, seeded RNG.
WinPaint.App (WPF exe)
  ViewModels/  MainViewModel (commands), LayersViewModel, ColorsViewModel, SettingsService.
  Controls/    CanvasView (WriteableBitmap host, overlays, rulers, input → tools), Rulers.
  Dialogs/     ResizeSkew, ImageProperties, EditColors, PageSetup/PrintPreview, Shortcuts, Settings, About.
  Resources/   Strings.resx, icons (Segoe Fluent Icons glyphs).
```

### Layer element stack (text keeps its place)
`Layer.Elements = [RasterSegment0, Text_A, RasterSegment1, ...]`. Painting always goes to the top segment.
Each RasterSegment holds sparse premultiplied pixel tiles plus an optional sparse EraseAlpha tile set.
Layer composite = fold bottom→top. Composite is cached and invalidated by dirty rects.
Text editing caches Below/Above composites so keystrokes only recomposite the dirty rect.

### History
Command pattern. Pixel commands store tile-level before/after copies of touched tiles of the top segment.
Structural commands (text create/edit/delete, layer ops, whole-image transforms) store before/after
snapshots of the affected objects (layers are cheap to snapshot because tiles are immutable-on-write: copy on write).

## Milestones (from prompt §11, order kept)
- M0 Scaffold
- M1 Pixel core, compositor, ViewTransform, CanvasView, open/save PNG
- M2 History + basic tools, colors UI, size control
- M3 Text object system (T-01..T-11, T-20, T-21)
- M4 Selection, clipboard, crop (T-15, T-16)
- M5 Image operations (T-12, T-13, T-14, T-22)
- M6 Brushes & shapes
- M7 Layers (T-19)
- M8 File completeness (T-17, T-18)
- M9 View & polish
- M10 Hardening (perf, leaks, UI tests, screenshots)
- M11 Final audit
