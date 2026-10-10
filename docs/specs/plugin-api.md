# winPaint plugin API — specification

| | |
|---|---|
| Status | **Deferred** — not planned for implementation (see below) |
| API version | 1.0 |
| Plugin package | `.wpplugin` (media type `application/vnd.winpaint.plugin+zip`) |
| SDK | NuGet package `WinPaint.Plugins.Sdk` (.NET 10, `net10.0-windows`) |
| Related | [Project format (WPP)](wpp-format.md) — how plugin data is stored in projects and images |

> **Deferred (2026-10-09).** winPaint is open source, so niche features can be contributed to the app directly, and
> features too niche or complex for most users go behind a **Labs** page in Settings (off by default). A public
> plugin API would freeze the internal design early, add support load for third-party code and need an SDK, docs and
> Store/privacy work, for a small audience. This draft is kept so the thinking isn't lost. Meanwhile:
>
> - Built-in effects, adjustments, tools and commands are written against **internal** contracts shaped like
>   sections 5–9 (self-contained classes that register themselves, declared properties, one undo step per
>   operation). Opening them to outside code later is then mostly a packaging job.
> - The project format keeps its `extensions/` mechanism ([WPP §5](wpp-format.md)); winPaint's own optional
>   features use it with `app.winpaint.*` ids.
>
> Revisit if there is real demand that contributions and Labs can't meet.

## 1. Scope

### 1.1 What plugins can do in v1

| Extension point | Example | Section |
|---|---|---|
| **Effects and adjustments** | Blur, sepia, auto-levels | 6 |
| **Tools** | A stamp tool, a measuring tool, a hotspot tool | 7 |
| **Commands** | *Plugins ▸ Edit tags…* opening a settings dialog | 8 |
| **Project data** | Tags, author, translation keys, hotspots stored in the project | 9 |

### 1.2 Not in v1 (the design leaves room for them)

A built-in plugin gallery, dockable panels, file-format plugins, script plugins (JavaScript, Python), running plugins
in a separate process, plugins editing layers or text objects directly, and keyboard shortcuts for plugin commands.

### 1.3 Decisions this spec is built on

| Decision | Choice |
|---|---|
| Distribution | Users install a `.wpplugin` file. A gallery may come later. |
| Isolation | **In-process.** Plugins are .NET libraries loaded into winPaint, like Paint.NET plugins. |
| Language | .NET only (C# or any .NET language) against the SDK. |
| Settings UI | **Declared properties.** Plugins describe their settings; winPaint builds the UI (section 5). |
| Code signing | Not checked. The install prompt shows the plugin's own name and author. |

## 2. Microsoft Store policy

Store Policies 7.20 (September 2026) allow apps to let users acquire *"add-ons or extensions … that enhance the
functionality of the product"* with user consent after install (10.1.5), and forbid using dynamically loaded code to
fundamentally change the app or add functionality that violates Store policy (10.2.2). winPaint complies by:

1. Describing plugin support in the Store listing.
2. Installing a plugin only when the user explicitly installs it and confirms the install prompt (section 4.2). winPaint
   never downloads or installs code by itself.
3. Never executing anything contained in a project or image. A project can reference a plugin's id, but never carries
   code.

## 3. Plugin package (`.wpplugin`)

A `.wpplugin` file is a ZIP archive:

```
plugin.json                    manifest (required)
lib/Contoso.Hotspots.dll        the plugin assembly and its private dependencies
icon.png                        optional, 64×64 or larger, square
README.md, LICENSE.txt          optional, shown in Settings ▸ Plugins
```

### 3.1 `plugin.json`

```json
{
  "id": "com.contoso.hotspots",
  "name": "Contoso Hotspots",
  "version": "2.3.0",
  "author": "Contoso Ltd.",
  "description": "Mark clickable areas on images and export them as an image map.",
  "website": "https://contoso.example/hotspots",
  "apiVersion": "1.0",
  "assembly": "lib/Contoso.Hotspots.dll",
  "entryType": "Contoso.Hotspots.HotspotsPlugin"
}
```

| Field | Rules |
|---|---|
| `id` | Reverse-DNS, lowercase letters, digits, `.` and `-`, 3–128 characters. It is also the plugin's **extension id** in projects ([WPP §5.1](wpp-format.md)), so it must never change once published. |
| `version` | Semantic version `major.minor.patch`. |
| `apiVersion` | The SDK API version the plugin was built against (section 11). |
| `assembly`, `entryType` | The assembly and the public class implementing `IPlugin`. |
| `name`, `author`, `description`, `website` | Shown in the install prompt and Settings ▸ Plugins. |

The package is validated like project containers: path rules, size limits (≤ 200 MB unpacked), no absolute paths or
`..`. A package that fails validation can't be installed.

## 4. Installing and running plugins

### 4.1 Where plugins live

`%LocalAppData%\winPaint\Plugins\<id>\<version>\` (inside the app's private storage when installed from the Store).
Plugins are per user. winPaint keeps a small registry (`plugins.json`) of installed plugins, their enabled state and
fault counts.

### 4.2 Install

- **Double-click a `.wpplugin` file** (file association in the MSIX manifest), or **Settings ▸ Plugins ▸ Install
  from file…**.
- winPaint validates the package and shows the install prompt:

  > **Install "Contoso Hotspots" 2.3.0?**
  > by Contoso Ltd. — contoso.example/hotspots
  > *Mark clickable areas on images and export them as an image map.*
  >
  > Plugins run inside winPaint with the same access to your files as winPaint itself. Only install plugins from
  > people you trust.
  >
  > [Install] [Cancel]

- Installing a newer version of an installed plugin replaces it; an older version asks first.
- A newly installed plugin is loaded immediately; updates take effect after restarting winPaint.

### 4.3 Manage

**Settings ▸ Plugins** lists installed plugins (icon, name, version, author, what they add) with **Enable/Disable**,
**Uninstall** and **Show details** (README, license, website). Disabling and uninstalling take effect after a
restart. Uninstalling never touches plugin data saved in projects; that data then follows its `keep`/`discard`
policy ([WPP §5.2](wpp-format.md)).

### 4.4 Loading

- At startup winPaint loads every enabled plugin, each in its **own `AssemblyLoadContext`**, so plugins can use
  different versions of the same dependency. The SDK contract assembly (`WinPaint.Plugins`) is always shared from
  winPaint.
- winPaint creates the `entryType` and calls `IPlugin.Initialize`, where the plugin registers its effects, tools and
  commands. Nothing else in the plugin runs until the user uses one of them or a document event fires.
- **Safe start:** holding **Shift** while winPaint starts, or starting with `--no-plugins`, skips all plugins.

### 4.5 Failures

- Every call into a plugin is wrapped. If it throws, winPaint cancels the operation (no partial edit is committed),
  shows *"Contoso Hotspots ran into a problem and the operation was cancelled"* and logs the details to
  `%LocalAppData%\winPaint\logs\plugins\<id>.log`.
- After 3 failures in one session a plugin is disabled for the rest of the session.
- If winPaint crashes and the crash log points to a plugin assembly, the next start offers to disable that plugin.
- A plugin that fails to load (missing dependency, incompatible `apiVersion`, `Initialize` throws) is marked
  *Couldn't load* in Settings ▸ Plugins with the reason; winPaint starts normally.

## 5. Declared properties (settings UI)

Effects, tools and commands describe their settings; winPaint builds the controls, applies the app theme, handles
keyboard access and accessibility names, drives live preview, and remembers the last values per plugin item.

```csharp
public override void DeclareProperties(PropertyBuilder p)
{
    p.Integer("radius", "Radius", defaultValue: 4, min: 1, max: 200);
    p.Number("strength", "Strength", defaultValue: 0.5, min: 0, max: 1, step: 0.01);
    p.Choice("mode", "Mode", ["Soft", "Hard"], defaultValue: "Soft");
    p.Boolean("keepAlpha", "Keep transparency", defaultValue: true);
    p.Color("tint", "Tint", defaultValue: Color.FromRgb(255, 200, 0), allowAlpha: true);
    p.Angle("angle", "Angle", defaultValue: 45);
    p.Text("label", "Label", defaultValue: "", maxLength: 200);
}
```

| Kind | Control | Value type |
|---|---|---|
| `Integer` | Slider + number box | `int` |
| `Number` | Slider + number box | `double` |
| `Choice` | Drop-down (or radio buttons for ≤ 3 options) | `string` |
| `Boolean` | Check box | `bool` |
| `Color` | Swatch opening winPaint's Edit colors dialog | `Color` |
| `Angle` | Dial + number box | `double` (degrees) |
| `Text` | Text box | `string` |

Values arrive as a read-only `PropertyValues` object (`values.GetInt("radius")`, …). Labels are plain text supplied by
the plugin (plugins handle their own localization).

## 6. Effects and adjustments

### 6.1 Contract

```csharp
public abstract class Effect
{
    public abstract string Name { get; }                 // "Gaussian blur"
    public virtual EffectCategory Category => EffectCategory.Effects;   // Effects or Adjustments
    public virtual string? SubmenuName => null;          // optional grouping
    public virtual void DeclareProperties(PropertyBuilder p) { }

    // Called on worker threads. Must be thread-safe and deterministic for the same input and values.
    public abstract void Render(EffectRenderArgs args);
}

public sealed class EffectRenderArgs
{
    public IReadOnlySurface Source { get; }      // the pixels being changed, as they were when the effect started
    public ISurface Destination { get; }         // write results here
    public RectInt Region { get; }               // the part to render in this call (a tile)
    public PropertyValues Values { get; }
    public CancellationToken Cancellation { get; }
}
```

Surfaces expose straight-alpha BGRA pixels (`ColorBgra`) with `Width`, `Height`, `GetRow(y)` (spans) and bounds-checked
`this[x, y]`. winPaint converts to and from its internal format.

### 6.2 How an effect runs

1. The user picks **Plugins ▸ Effects (or Adjustments) ▸ *Name***. Effects apply to the **selection** if there is one,
   otherwise to the **whole active layer**.
2. If the effect declares properties, winPaint shows a dialog built from them. The canvas shows a **live preview**:
   winPaint renders visible tiles on background threads, cancels and restarts when a value changes.
3. **OK** renders the full region (with a progress bar and Cancel for long renders) and commits **one undo step**
   named after the effect. **Cancel** discards everything.
4. The source is the active layer's composite in the region. Live text objects intersecting the region are
   **flattened first** inside the same undo step, exactly like other pixel edits on selections
   (status: *"Text in the area was flattened"*). Effects never create or edit text objects.
5. The result replaces the region's pixels; a free-form selection masks it.

## 7. Tools

### 7.1 Contract

```csharp
public abstract class PluginTool
{
    public abstract string Name { get; }
    public virtual ImageSource? Icon => null;             // 20×20 or vector; default icon if null
    public virtual ToolCursor Cursor => ToolCursor.Crosshair;
    public virtual void DeclareOptions(PropertyBuilder p) { }   // shown in the tool options bar

    public virtual void Activate(IToolContext context) { }
    public virtual void Deactivate() { }                   // commit or cancel anything pending
    public virtual void PointerDown(PointerArgs e) { }
    public virtual void PointerMove(PointerArgs e) { }
    public virtual void PointerUp(PointerArgs e) { }
    public virtual void DoubleClick(PointerArgs e) { }
    public virtual bool KeyDown(KeyArgs e) => false;
    public virtual void DrawOverlay(IOverlayCanvas overlay) { }
}
```

- `PointerArgs`: canvas position (fractional pixels), button (left/right/middle), modifiers (Shift/Ctrl/Alt).
- Plugin tools appear in a **Plugin tools** drop-down in the toolbar's Tools group. Their options appear in a tool
  options bar under the toolbar (the same place as the Text toolbar).

### 7.2 Drawing and undo

```csharp
using var edit = context.BeginEdit("Stamp");    // one undo step
edit.Surface.Blend(stampPixels, x, y);          // draws into the active layer above existing content
edit.Commit();                                  // or let it dispose without Commit to cancel
```

- `BeginEdit` gives a surface the size of the canvas that composites over the active layer, clipped to the selection
  if there is one, with winPaint's opacity setting applied. Only one edit can be open at a time.
- Drawing over live text keeps the text editable, the same as built-in brushes: the plugin's pixels land above it.
- `DrawOverlay` draws transient guides (lines, rectangles, polygons, handles, labels) in canvas coordinates. They are
  never part of the image. Call `context.InvalidateOverlay()` to redraw.
- Tools can read colors 1 and 2, the selection and document information through `context` (section 9.3).

## 8. Commands

```csharp
public abstract class PluginCommand
{
    public abstract string Name { get; }                    // "Edit tags…"
    public virtual void DeclareProperties(PropertyBuilder p) { }
    public virtual bool CanExecute(ICommandContext context) => true;
    public abstract void Execute(ICommandContext context, PropertyValues values);
}
```

- Commands appear in the **Plugins** menu, grouped under the plugin's name. The menu appears once any plugin is
  installed.
- If the command declares properties, winPaint shows a dialog first; `Execute` receives the values. A command can
  pre-fill the dialog from project data by overriding `GetInitialValues(ICommandContext)`.
- `context` gives access to project data (section 9) and can show a message (`context.ShowMessage(text)`).
- Commands change the image only through project data or `context.BeginEdit` (section 7.2).

## 9. Project data and the document

### 9.1 Reading and writing plugin data

Each plugin gets an `IProjectData` for the current document, scoped to its own extension folder
([WPP §5](wpp-format.md)). A plugin can't see or change another plugin's data.

```csharp
var data = context.ProjectData;
string? json = data.ReadText("tags.json");               // null if missing
using (var change = data.BeginChange("Edit tags"))        // one undo step
{
    data.WriteText("tags.json", newJson);
    data.Policy = DataPolicy.Keep;                        // or DataPolicy.Discard
    change.Commit();
}
```

- `ReadText`, `ReadBytes`, `WriteText`, `WriteBytes`, `Delete`, `List`, `Exists`. Paths are relative to the plugin's
  folder and use the same path rules as projects.
- Changes are held in memory and saved with the document. Every change goes through `BeginChange` and is undoable;
  undo/redo restores the plugin's data exactly.
- The data and its `Policy` are written to the project when the user saves ([WPP §5.2](wpp-format.md)).
- Size limit: 256 MB per plugin per document.

### 9.2 Anchors

```csharp
Anchor a = data.Anchors.Create(AnchorTarget.Region(new RectD(100, 100, 200, 100)));
Anchor b = data.Anchors.Create(AnchorTarget.Text(textId));
foreach (var anchor in data.Anchors.All) { /* anchor.Id, anchor.Target */ }
data.Anchors.Delete(a.Id);
```

Anchor targets: `Document`, `Layer(id)`, `Text(id)`, `Region(rect or polygon, optional layerId)`. winPaint maintains
anchors as described in [WPP §5.3](wpp-format.md) (moving regions on rotate/flip/resize/crop, deleting anchors whose
layer or text is gone), whether or not the plugin is running. Creating and deleting anchors happens inside
`BeginChange`.

### 9.3 Document information (read-only)

`context.Document` exposes the canvas size and DPI, the layers (id, name, visible, opacity, blend mode), live text
objects (id, layer id, text, font, size, colors, transformed bounds), the active layer, the selection bounds and mask,
and colors 1 and 2. In v1 plugins can't change layers or text objects directly.

### 9.4 Events

```csharp
context.Events.DocumentOpened += …;   // a document was opened or created
context.Events.DocumentChanged += …;  // after every committed change: kind (pixels, geometry, layers, text, plugin
                                      // data), affected layer/text ids, and the transform matrix for geometry changes
context.Events.Saving += …;           // before saving: last chance to update data
```

Events let an installed plugin keep its data correct (e.g. recompute something after a crop). Handlers run on the UI
thread and must return quickly; long work should be scheduled with `context.RunInBackground`.

## 10. Threading, settings and logging

- All API calls are made on the UI thread, except `Effect.Render`, which runs on worker threads and may only use its
  `EffectRenderArgs`.
- `context.RunInBackground(func, progressTitle)` runs work off the UI thread with a progress indicator and returns to
  the UI thread for results.
- `context.Settings`: a small per-plugin key/value store for plugin-wide preferences (not per document).
- `context.Log`: writes to the plugin's log file (section 4.5).

## 11. Versioning

- The SDK follows semantic versioning. `apiVersion` in `plugin.json` is the SDK `major.minor` the plugin was built
  against.
- winPaint loads a plugin when the **major** versions match and the plugin's **minor** is less than or equal to
  winPaint's. Within a major version, APIs are only added, never removed or changed.
- A plugin whose `apiVersion` is too new or has a different major version is shown as *Needs a newer winPaint* or
  *Not compatible*.

## 12. SDK deliverables

- `WinPaint.Plugins.Sdk` NuGet package: the contract assembly, XML docs and a build target that produces a
  `.wpplugin` from a project.
- `dotnet new winpaint-plugin` template.
- Samples: an adjustment (sepia), an effect with a dialog (blur), a tool (stamp) and a metadata command (tags).
- A developer guide, published with the specs.

## 13. Changes this requires elsewhere

- Store listing: mention plugin support (section 2). Privacy policy: third-party plugins aren't made by the winPaint
  author and may handle data differently; the core app's practices don't change.
- MSIX manifest: file-type association for `.wpplugin`.
- Main window: a **Plugins** menu, a **Plugin tools** drop-down and a tool options bar.
- Settings: a **Plugins** page.

## 14. Open questions

1. Where should effects live: a new top-level **Effects** menu (and **Adjustments**), or only under **Plugins**?
   (Draft: under Plugins, since winPaint itself has no effects.)
2. Should the install prompt offer to enable the plugin's commands only (no tools/effects) or install all-or-nothing?
   (Draft: all-or-nothing.)
3. Should disabling and updating plugins take effect without a restart? It requires unloadable plugin contexts, which
   adds complexity and failure modes. (Draft: restart.)
