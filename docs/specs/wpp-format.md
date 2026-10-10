# winPaint project format (WPP) — specification

| | |
|---|---|
| Status | **Draft for review** |
| Format version | 1.0 |
| File extension | `.wpp` (standalone project) |
| Media type | `application/vnd.winpaint.project+zip` |
| Also embedded in | PNG, TIFF, GIF and JPEG images saved by winPaint |

## 1. Goals

1. **It just works.** A user saves `poster.png`, sends it, reopens it later in winPaint, and can still fix a typo in the
   text. Nobody has to know that projects exist or save twice.
2. **Advanced users stay in control.** The project format is documented and can be saved explicitly as `.wpp`. The
   app always shows when an image carries editable data, and plain images are one click away.
3. **Easy to extend.** In-app plugins can store their own data in a project and attach it to the whole document, a
   layer, a text object or a region of the image. Data from plugins that aren't installed is never lost by accident.
4. **Safe to open.** Projects come from anywhere (email, downloads). Reading one must never execute anything or be
   able to exhaust memory or write outside the app.

Non-goals for 1.0: undo history, view state (zoom, scroll, panels) and tool settings aren't saved. Fonts aren't
embedded (licensing); a missing font falls back and keeps its name so it returns when the font is installed.

## 2. Two ways a project is stored

The same **project container** (section 3) is used in two places:

| | Standalone `.wpp` | Embedded in an image |
|---|---|---|
| Created by | File ▸ Save as ▸ *winPaint project (.wpp)* | Saving as PNG, TIFF, GIF or JPEG when the image has something worth keeping (section 7.1) |
| Other apps see | An unknown file (or the preview, via a future Explorer thumbnail handler) | A normal image |
| Contains a preview | Yes (`preview.png`, `thumbnail.png`) | No (the image itself is the preview) |
| Survives sharing | Yes, as long as the file isn't converted | Yes, unless a service strips private image data; then it degrades to a plain image |

## 3. The project container

A project container is a **ZIP archive** (deflate compression, ZIP64 allowed).

### 3.1 Layout

```
mimetype                         first entry, stored uncompressed (no extra field), ASCII:
                                 application/vnd.winpaint.project+zip
manifest.json                    format version, part index, extension registry, anchors, fingerprints
document.json                    canvas, layers, element stacks, text objects
layers/<layerId>/<segmentId>.png        raster segment pixels
layers/<layerId>/<segmentId>.erase.png  raster segment erase mask (only when present)
preview.png                      .wpp only: full-size flattened image
thumbnail.png                    .wpp only: flattened image, longest side 256 px
extensions/<extensionId>/...     plugin-owned data (any files, any structure)
```

- `<layerId>`, `<segmentId>` and every other id are lowercase GUIDs without braces, e.g.
  `3f0c9e1e-6a1d-4c35-9a52-0f0a2d6c1b11`. They are the same ids winPaint uses in memory, so they are stable across saves.
- Paths use `/`, are case-sensitive, and must not contain `..`, a leading `/`, a drive letter, a backslash or a colon.
- Readers ignore files they don't recognize outside `extensions/`; writers don't keep them (section 9).

### 3.2 `manifest.json`

```json
{
  "format": "winpaint-project",
  "formatVersion": "1.0",
  "generator": { "name": "winPaint", "version": "1.1.0" },
  "parts": [
    { "path": "document.json", "mediaType": "application/json", "sha256": "…" },
    { "path": "layers/3f0c…/9a7e….png", "mediaType": "image/png", "sha256": "…" }
  ],
  "hostFingerprint": { "algorithm": "sha256-rgba8", "value": "…" },
  "extensions": [ /* section 5.2 */ ],
  "anchors": [ /* section 5.3 */ ]
}
```

| Field | Rules |
|---|---|
| `format` | Always `winpaint-project`. |
| `formatVersion` | `major.minor`. See section 8. |
| `generator` | Informational. |
| `parts` | Every file in the archive except `mimetype` and `manifest.json`, with its SHA-256 (lowercase hex). A part whose hash doesn't match is treated as missing. |
| `hostFingerprint` | Embedded containers only. Section 6.5. |
| `extensions`, `anchors` | Plugin data registry. Section 5. |

### 3.3 `document.json`

```json
{
  "canvas": { "width": 1152, "height": 648, "dpiX": 96, "dpiY": 96 },
  "activeLayerId": "3f0c9e1e-6a1d-4c35-9a52-0f0a2d6c1b11",
  "layers": [
    {
      "id": "3f0c9e1e-6a1d-4c35-9a52-0f0a2d6c1b11",
      "name": "Background",
      "visible": true,
      "opacity": 1.0,
      "blendMode": "normal",
      "isBackground": true,
      "isTransparent": false,
      "elements": [
        { "type": "raster", "id": "9a7e…", "pixels": { "fill": "#FFFFFFFF" } },
        { "type": "text", "id": "c41d…",
          "text": "Draft v1",
          "box": [100, 120, 240, 40],
          "transform": [1, 0, 0, 1, 0, 0],
          "fontFamily": "Segoe UI", "fontSizePt": 36,
          "bold": true, "italic": false, "underline": false, "strikethrough": false,
          "foreground": "#FF000000", "background": "#FFFFFFFF",
          "opaqueBackground": false, "opacity": 1.0 },
        { "type": "raster", "id": "5be2…",
          "pixels": { "part": "layers/3f0c…/5be2….png", "x": 80, "y": 90, "width": 400, "height": 120 },
          "erase": { "part": "layers/3f0c…/5be2….erase.png", "x": 0, "y": 0, "width": 1152, "height": 648 } }
      ]
    }
  ]
}
```

- `layers` are listed **bottom to top**; `elements` are listed **bottom to top** and keep winPaint's invariant: the first
  and last element of every layer are `raster` elements.
- A **raster** element's `pixels` is one of:
  - `{ "part", "x", "y", "width", "height" }`: a straight-alpha 8-bit RGBA PNG covering that rectangle of the canvas
    (writers store only the bounding box of non-transparent pixels);
  - `{ "fill": "#AARRGGBB" }`: the whole canvas is one color (e.g. a white background);
  - `null`: empty.
- `erase` (optional) is an 8-bit grayscale PNG with the same rectangle rules: 255 = fully erased beneath this element.
- A **text** element mirrors winPaint's `TextObject`. `box` is `[x, y, width, height]` in untransformed canvas pixels;
  `transform` is the 2D affine matrix `[m11, m12, m21, m22, offsetX, offsetY]`; colors are `#AARRGGBB`.
- `blendMode`: `normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `difference`.
- Unknown properties inside known objects are ignored on read and not written back (section 8).

## 4. What a project restores

Opening a project restores the canvas, every layer (including hidden ones) with its properties, the full element stack
of each layer, every live text object (editable), the active layer, and all plugin data. It does **not** restore undo
history or view state; the document opens clean (not dirty) with an empty undo stack.

## 5. Plugin data

### 5.1 Ownership

Every plugin has an **extension id** in reverse-DNS form, e.g. `com.contoso.hotspots` (lowercase letters, digits,
`.`, `-`; 3–128 characters). A plugin owns `extensions/<extensionId>/` and its entries in the manifest. winPaint never
interprets the contents of a plugin's folder and only gives it to the plugin with that id. Plugins can't read or write
other plugins' data through the plugin API.

### 5.2 The extension registry

```json
"extensions": [
  {
    "id": "com.contoso.hotspots",
    "displayName": "Contoso Hotspots",
    "version": "2.3.0",
    "dataVersion": "1",
    "infoUrl": "https://contoso.example/hotspots",
    "policy": "keep"
  }
]
```

`displayName` and `infoUrl` let winPaint tell users which plugin a file came from and where to get it.

**`policy`** is the rule winPaint follows *when the plugin isn't installed*. The plugin writes it when it saves its
data, so a copy of winPaint without the plugin can still act sensibly:

| Policy | Meaning (when the plugin is missing) |
|---|---|
| `keep` (default) | Always carry the data along unchanged. For data that stays true whatever happens to the image (tags, author, notes). |
| `discard-on-geometry-change` | Drop the data if the canvas is rotated, flipped, resized, skewed, cropped or its size changes. |
| `discard-on-pixel-change` | Drop the data if any pixel or text changes (any undoable edit). |
| `discard-on-any-change` | Drop the data on any change at all, including layer renames and visibility. |

When the plugin *is* installed, it is told about every change through the plugin API and decides for itself; the
policy only applies in its absence. Region anchors (5.3) are handled automatically regardless of policy.

### 5.3 Anchors: attaching data to parts of the image

Plugin data can attach to four kinds of targets. The attachment is declared as an **anchor** in the manifest so
winPaint can maintain it even when the plugin is missing; the plugin's own files refer to anchors by their `id`.

```json
"anchors": [
  { "id": "a1", "extension": "com.contoso.tagger",   "target": { "kind": "document" } },
  { "id": "a2", "extension": "com.contoso.tagger",   "target": { "kind": "layer", "layerId": "3f0c…" } },
  { "id": "a3", "extension": "com.contoso.translate","target": { "kind": "text",  "textId": "c41d…" } },
  { "id": "a4", "extension": "com.contoso.hotspots", "target": { "kind": "region",
      "shape": { "type": "polygon", "points": [[100,100],[300,100],[300,200],[100,200]] },
      "layerId": null,
      "onGeometryChange": "transform" } }
]
```

| Kind | Meaning | What winPaint does to the anchor when the document changes |
|---|---|---|
| `document` | The whole project | Nothing. |
| `layer` | One layer | Deleted when the layer is deleted. Merge down moves it to the lower layer; duplicating a layer does not copy it. |
| `text` | One live text object | Deleted when the text object is deleted or flattened into pixels (selection edits, Flatten image, merging a blended layer). |
| `region` | An area of the canvas, optionally tied to a layer | Rotate/flip/resize/skew apply the same matrix to `shape`. Crop and canvas resize translate it; a region fully outside the new canvas is deleted. If `onGeometryChange` is `discard`, any of those operations deletes it instead. |

`shape.type` is `rect` (`[x, y, width, height]`) or `polygon` (points in canvas pixels). When an anchor is deleted,
its id disappears from the manifest; a plugin that later finds its files referring to a missing anchor ignores them.

### 5.4 Users control plugin data

- **File ▸ Image properties** lists every extension in the document (display name, whether the plugin is installed,
  data size) with **Remove** per extension and **Remove data from missing plugins**.
- Removing data is an undoable step.

## 6. Embedding in images

The embedded payload is a complete project container (section 3) **without** `preview.png` and `thumbnail.png`,
preceded by an 8-byte header:

```
57 50 50 31     "WPP1"   magic
00 01           container encoding version (1)
00 00           reserved, must be 0
<ZIP bytes>
```

### 6.1 PNG

A private, ancillary, **unsafe-to-copy** chunk named **`wpRJ`** (`w` lowercase = ancillary, `p` lowercase = private,
`R` = reserved bit, `J` uppercase = unsafe to copy, so PNG editors that change the image must drop it). Written after
the last `IDAT` and before `IEND`. A payload larger than 2^31 − 1 bytes is split over consecutive `wpRJ` chunks that
are concatenated in order.

### 6.2 TIFF

A private tag **65129** (`WinPaintProject`), type `UNDEFINED`, in IFD0, holding the payload.

### 6.3 JPEG

One or more **APP11** segments, each starting with the identifier `WINPAINT\0` (9 bytes), then a 2-byte big-endian
sequence number and a 2-byte total count, then up to 65,520 bytes of payload. Segments follow the APP0/APP1 segments.
Readers concatenate the payload in sequence order and reject gaps.

### 6.4 GIF

An Application Extension with identifier `WINPAINT` and authentication code `1.0`, whose data sub-blocks hold the
payload. Written after the Global Color Table, before the first image.

### 6.5 Fingerprint: never restore an out-of-date project

When winPaint embeds a project it also stores `hostFingerprint`: SHA-256 of the image **as it will decode**
(width, height, then straight-alpha RGBA bytes, row by row). For JPEG and GIF it is computed from the encoded result,
so lossy compression and color reduction are accounted for.

On open, winPaint decodes the image, recomputes the fingerprint and compares:

- **Match:** open as a project (layers and editable text restored).
- **Mismatch:** another program changed the picture. Open it as a plain image and show *"This image was changed
  outside winPaint, so its editable text wasn't restored."* with a **Restore the winPaint version** action, which opens
  the embedded project and discards the outside changes. That action opens it as a new, unsaved document.

### 6.6 BMP and ICO

There is no standard place for private data, so projects are never embedded. When the document has something worth
keeping, the Save dialog says *"Text won't stay editable in this format"* and suggests PNG or `.wpp`.

## 7. App behavior

### 7.1 Saving

- **File ▸ Save (Ctrl+S)** keeps writing the file's format as today. For PNG, TIFF, GIF and JPEG, winPaint embeds the
  project **when the document has something worth keeping**: live text, more than one layer, a layer with non-default
  opacity/blend/visibility, or plugin data. Otherwise it writes a plain image, byte-for-byte as today.
- **Setting:** *Keep text editable in saved images* (on by default). Off = images are always plain.
- **File ▸ Save as** adds *winPaint project (.wpp)* and a **Save as plain image** check box (one-off, doesn't change the
  setting).
- After saving with an embedded project, the status bar shows *"Editable text is saved inside this image"*.
- Hidden layers are included (the project is a full copy). Because embedded data travels with the image, the Save as
  dialog shows *"Includes N hidden layers"* when there are any.
- The unsaved-changes prompt is unchanged: one save covers both the picture and the project.
- New documents still default to PNG.

### 7.2 Opening

- `.wpp`: opened as a project. Open, recent files, drag and drop and the command line all accept it.
- Images: decoded as today; if a valid payload with a matching fingerprint is present, the project is restored
  (6.5). The title shows the image's file name; Save writes back to the same image.
- Corrupt or unsupported payloads (bad magic, failed hash, unknown major version, limits exceeded) are ignored with a
  status-bar notice; the image opens as a plain image. A corrupt `.wpp` shows an error like other unreadable files.
- Missing fonts: the text keeps its font name and renders with a fallback; Image properties lists missing fonts.
- Data from plugins that aren't installed is shown in Image properties (5.4) and kept according to its policy.

### 7.3 Interaction with existing features

| Feature | Behavior |
|---|---|
| Flatten image | Text becomes pixels; text anchors are deleted; the next save embeds only if something else is still worth keeping. |
| Copy / paste | Unchanged: always plain pixels, never projects or plugin data. |
| Set as desktop background | Uses the saved image; the embedded data is ignored by Windows. |
| Print | Unchanged. |

## 8. Versioning and compatibility

- `formatVersion` is `major.minor`.
  - **Minor** versions only add optional fields or parts. A reader that sees a newer minor loads what it knows and
    shows *"This file was saved by a newer winPaint; some information may not be kept if you save it."*
  - **Major** versions may change meaning. A reader that sees a newer major refuses the container (a `.wpp` shows an
    error; an image opens plain with a notice).
- Writers always write the version they implement.
- Plugin data has its own `dataVersion`, which winPaint never interprets.

## 9. Security and limits (readers must enforce)

| Limit | Value |
|---|---|
| Canvas | ≤ 100,000 × 100,000 px and ≤ 1.5 × 10⁹ px total |
| Embedded payload | ≤ 2 GB; container entries ≤ 100,000 |
| Uncompressed total | ≤ 8 GB, and ≤ 200 × the compressed size (zip-bomb guard) |
| JSON | `manifest.json` ≤ 16 MB, `document.json` ≤ 256 MB, nesting depth ≤ 64 |
| Text object | ≤ 1,000,000 characters |
| Per-extension data | ≤ 256 MB |

- Paths are validated before use (3.1); nothing is ever extracted to disk.
- winPaint never executes anything from a project. Plugin data is opaque bytes handed only to the matching installed
  plugin; plugins must treat it as untrusted input.
- A failed limit or validation means the container is ignored (images) or rejected (`.wpp`), never a crash.

## 10. Changes this requires elsewhere

- Privacy policy, Store description and README: they currently say saved files are always plain images and that text
  is editable only until the image is closed. Update them to describe embedded projects, the setting, and that
  embedded data (including hidden layers and plugin data) travels with shared images.
- `docs/DECISIONS.md`: the original "no project format, no sidecar files" decision is superseded by this spec.
- MSIX manifest: add `.wpp` to the file-type association.
- Plugin API spec (separate document): plugin identity and loading, the read/write API for a plugin's extension
  folder and anchors, change notifications (so installed plugins can update their data), and the full extensibility
  surface (tools, effects, commands, panels, undo integration). Microsoft Store policy 10.2.2 limits loading code that
  isn't part of the submitted package; the plugin model must be checked against its current wording before that spec
  is finalized.

## 11. Open questions

1. Should Explorer show `.wpp` thumbnails (a thumbnail-provider shell extension in the MSIX package)?
2. Should plugin data be included when the user picks **Save as plain image**? (Draft: no; plain means plain.)
3. Should a project remember which image format it came from, so a `.wpp` can be re-exported to the same format?
4. Is 256 px the right thumbnail size, or should `.wpp` also include a mid-size preview for fast opening of huge
   images?

## Appendix A. Example `.wpp` listing

```
mimetype                                                        38 B   stored
manifest.json                                                  1.2 KB
document.json                                                  2.4 KB
layers/3f0c9e1e-…/5be2….png                                    48 KB
layers/8d41b7a0-…/0c19….png                                   210 KB
preview.png                                                    512 KB
thumbnail.png                                                   21 KB
extensions/com.contoso.hotspots/hotspots.json                  3.1 KB
```
