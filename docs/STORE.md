# Publishing winPaint to the Microsoft Store

winPaint ships like MarkdownStudio: an **unsigned `.msixupload`** with x64 and ARM64 packages that the Store signs
during certification. Packaging is a plain `makeappx` pipeline (WPF), not a Visual Studio packaging project.

| Item | Value |
|---|---|
| Package identity name | the value Partner Center assigns to the winPaint reservation (default in the script: `JustinKing.winPaint`) |
| Publisher | `CN=7FF40E1D-C390-4E69-A012-910F68FFFA3A` (same account as MarkdownStudio) |
| Publisher display name | `Justin King` |
| Architectures | x64, ARM64 (self-contained .NET 10, no runtime needed on the PC) |
| Minimum OS | Windows 10 1809 (10.0.17763) |
| Capabilities | `runFullTrust` only (no internet) |
| File types | .png .jpg .jpeg .jpe .jfif .bmp .dib .gif .tif .tiff .ico ("Open with") |

## 1. Reserve the name (once)
Partner Center → **Apps and games → New product → MSIX or PWA app** → reserve **winPaint**.
Then open **Product management → Product identity** and copy **Package/Identity/Name**.

> Naming note: "winPaint" is close to Microsoft Paint. If certification flags the name under policy 10.1 (misleading
> or confusingly similar names), reserve a different name. The app's display name is only in
> `packaging/Package.appxmanifest`, `src/WinPaint.App/Resources/Strings.txt` (AppName/TitleFormat) and the window title.

## 2. Build the upload
From the repo root (Windows SDK required; it's already installed on this PC):

```powershell
.\tools\package.ps1 -IdentityName "<Package/Identity/Name from Partner Center>" -Version 1.0.0.0
```

Output: `artifacts\package\winPaint_<version>.msixupload` (and the `.msixbundle` inside it).
Each new submission needs a higher version; the last part must stay `0` (e.g. 1.0.1.0).

## 3. Optional local check (needs your approval on your PC)
The upload is unsigned, so it can't be installed directly. To run the Windows App Certification Kit locally, sign a copy
with a test certificate whose subject matches the publisher, trust it, install, then run:

```powershell
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath artifacts\package\winPaint_1.0.0.0.msixbundle -reportoutputpath artifacts\package\wack.xml
```

Partner Center runs the same checks during certification, so this step is optional.

## 4. Create the submission
- **Packages:** upload the `.msixupload`.
- **Pricing and availability:** your choice (MarkdownStudio's settings are a good default).
- **Properties:** category **Photo & video** (subcategory none). Privacy policy URL:
  <https://github.com/justinswork/winPaint/blob/develop/PRIVACY.md> (same approach as MarkdownStudio).
- **Age ratings (IARC questionnaire):** utility/productivity app; no violence, sexual content, gambling, drugs,
  user-to-user communication, sharing of location or personal info, purchases or ads → expected rating **3+ / Everyone**.
- **Store listing (English):** text below; screenshots in `artifacts/store/` (1904×1041, above the 1366×768 minimum);
  Store logos are generated into `packaging/Images` (use `StoreLogo.scale-400.png` 200×200 or `Square150x150Logo.scale-400.png` 600×600 if Partner Center asks for a logo).

### Listing text

**Product name:** winPaint

**Short description:**
A familiar Paint-style image editor where text stays editable until you close the image.

**Description:**
winPaint is a fast, familiar image editor for Windows with one big difference: text never gets "stuck" to your picture.

Add a text box, paint over it, rotate the image — then double-click the text whenever you like to fix a typo, change
the font, size, color or background, move or resize it, or delete it. Everything you painted after the text stays on
top of it exactly as it was.

Everything you expect from a classic paint program is here:
• Pencil, eraser, fill, color picker and magnifier
• Nine brushes: brush, calligraphy brush and pen, airbrush, oil, crayon, marker, natural pencil and watercolor
• 23 shapes with outline and fill styles that you can still move, resize and restyle after drawing
• Rectangle and free-form selection, transparent selection, crop, rotate, flip, resize and skew
• Layers with opacity and blend modes
• Zoom from 1 % to 800 %, rulers, gridlines, full screen and a thumbnail navigator
• Light and dark themes
• Open and save PNG, JPEG, BMP, GIF, TIFF and ICO
• Unlimited-feeling undo with a configurable memory budget

Saved files are ordinary images that open anywhere. winPaint works offline and collects no data.

**Features (up to 20 short lines):**
- Re-editable text: double-click any text to change it later
- Paint over text without losing your edits
- 9 brushes and 23 shapes
- Layers with blend modes
- Light and dark themes
- PNG, JPEG, BMP, GIF, TIFF and ICO support
- Works offline; no data collection

**Search terms:** paint, drawing, image editor, text on image, pixel art

## 5. After certification
Certification usually takes 24–72 hours. Tag the release commit (`git tag v1.0.0`) and push the tag.
