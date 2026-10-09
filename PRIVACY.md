# Privacy Policy

*Last updated: 9 October 2026*

winPaint is a Windows image editor that runs entirely on your local device. This policy describes what data the app
handles and, more importantly, what it does **not** do with it.

## Summary

- **The app does not collect, transmit, or store any data on remote servers.**
- All settings, recent files, and images stay on your local device.
- The app has no telemetry, no analytics, no crash reporting service, no ads, no account system, and no network calls
  of its own. It does not even request internet access.
- The author has no access to anything you do in the app.

## What the app stores locally

When you use winPaint, the app writes a small amount of data under Windows' standard per-app data location. When
installed from the Microsoft Store, Windows keeps it inside the app's private package folder:

```
%LOCALAPPDATA%\Packages\JustinKing.winPaint_<hash>\
```

What lives there:

- **Preferences** (`settings.json`) — your chosen theme, last used tool, brush, shape and size settings, colors and
  custom colors, window size and position, which panels are shown (rulers, gridlines, status bar, layers), page setup,
  and the undo memory budget.
- **Recently opened files** — the paths of up to 10 images you opened or saved, used for the File ▸ Recent files list.
- **Crash logs** — if the app ever closes because of an unexpected error, a technical error log (the error message and
  where in the program it happened) is written to a `logs` folder there. Logs never contain your images and are never
  sent anywhere.

This data never leaves your device. Uninstalling the app removes the package folder along with everything above.

## What the app does *not* do

- No network requests. winPaint does not contact any remote server for telemetry, analytics, license checks,
  advertisements, automatic update checks, or any other purpose. App updates are delivered by the Microsoft Store
  itself, not by the app.
- No background activity. The app only runs while you have it open.
- No third-party SDKs that collect or transmit information.
- No access to data outside the files and folders you explicitly open or save.

## Images you open and edit

Images you open and save stay where you put them. The app does not upload, copy, mirror, scan, or otherwise transmit
your pictures anywhere. Text you add to an image stays editable only while the image is open; saved files are ordinary
image files.

A few actions touch the rest of Windows, and only when you choose them:

- **Clipboard** — Copy and Cut place the selected part of the image on the Windows clipboard; Paste reads an image
  from it.
- **Set as desktop background** — saves nothing extra; it tells Windows to use the image file you already saved.
- **Printing** — the image is sent to the printer you pick in the Windows print dialog.
- **Recovery copy** — after an unexpected error, the app asks whether to save a recovery copy of your picture to your
  Pictures folder. Nothing is saved unless you say yes.

## Children

The app is suitable for general audiences and does not knowingly collect data from anyone — including children.

## Contact

If you have questions about this policy or the app, please open an issue at
<https://github.com/justinswork/winPaint/issues>.

## Changes to this policy

If the policy ever changes, the updated version will be published at the same URL you're reading this one at, and the
"Last updated" date at the top of the document will reflect the change.
