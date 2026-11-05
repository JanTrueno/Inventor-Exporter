# Inventor STEP Export

Autodesk Inventor 2025 add-in that exports assemblies and parts to STEP files in the folder you are working in (the folder of the open assembly, part or drawing). The files are
named after the model, in a folder named after the order number and the date, for example
`Tekeningenpakket-STP (160261496) 04-11-2026\Frame.stp`. Both names can be built from any iProperties.

The panel is docked in Inventor's left-hand area, built in Inventor's own style and follows your theme, like the
[Engrave](https://github.com/JanTrueno/Inventor-Engrave) and [Tab and Slot](https://github.com/JanTrueno/Inventor-Tab-and-Slot) add-ins. The ribbon icon is a blue cube.

## Use

Click **STEP** on the **Assemble** tab of an assembly, the **3D Model** or **Sheet Metal** tab of a part, or the
**Place Views** tab of a drawing.

- **What is ticked at first**: in an assembly or part, the open document itself (the main assembly). In a drawing,
  the assembly shown on **Sheet 1** (or its part, when sheet 1 shows no assembly); the tree lists every model in the
  drawing's views. Components selected before you start are ticked instead.
- **Folder**: **In** is the folder of the open assembly, part or drawing (of an IDW: the folder of the .idw).
  **Browse…** picks another folder, **Reset** goes back to the folder of the document. Inside it a folder is made,
  named after the iProperty list under it and the date: at first the custom iProperty `ordernummer` with
  `Tekeningenpakket-STP (` before and `)` after, then the date as `dd-MM-yyyy`, for example
  `...\Frame\Tekeningenpakket-STP (160261496) 04-11-2026\`. The rows work like those of the file
  name (before / value / after / remember); untick **Date at the end** or remove the rows for no folder.
  The line below shows the full folder.
- **File name** (folded up): at first just the model name. Open it to add iProperties after the model name and the
  date (**Date at the end**). **+ iProperty** adds one (Summary, Project, Status or Custom, or
  type any name); the cross removes it. Each row has text to put **before** and **after** the value, and the value
  itself: read from the drawing (else from the model it shows) or the open assembly. A value you type is used for
  every model; untouched, each model uses its own iProperty. Tick **remember** to keep the value you filled in, also
  next time, instead of reading the iProperty. Empty iProperties drop out; `160100` means "not filled in yet" and is
  refused. The line below shows the resulting name.
- **Models**: the model tree, folded up. Tick the assemblies and parts to export; each gets its own STEP file, and an
  assembly is exported whole. Shift click ticks every row from the last clicked one (the top row when none was clicked; when all were ticked they are unticked), Ctrl click one row, Ctrl+A every row shown. Rows show **In folder** when that STEP
  file exists already, and **Exported** or **Failed** after an export.
- **Export**: asks before overwriting (No exports only the others), shows Inventor's progress bar (with Cancel) and
  opens the folder in Explorer when everything went well.
- **Settings**: the separator and date format of the file name (`_`, `yyyy-MM-dd`) and of the folder name (a space,
  `dd-MM-yyyy`), the placeholder,
  the STEP protocol
  (AP203, AP214 or AP242; default AP214), the extension (`.stp` or `.step`) and whether the folder is opened afterwards.
  They are kept in `%APPDATA%\StepExport\settings.json`. Problems are logged in `%LOCALAPPDATA%\StepExport\log.txt`.

The panel stays open for further exports and switches to the document you activate.

## Building and installing

Requires Inventor 2025 and the .NET 8 SDK. Close Inventor (it locks the add-in DLL), then:

```
powershell -ExecutionPolicy Bypass -File install.ps1
```

This builds the add-in and installs it in `%APPDATA%\Autodesk\ApplicationPlugins\StepExport.bundle`.
`install.ps1 -Remove` uninstalls it.

## Installer

Run `build-installer.ps1` (needs Inno Setup 6). It builds the add-in and writes `dist\StepExport-Setup-<version>.exe`.
The setup installs per user and asks to close Inventor first. `installer\make-logo.ps1` draws the cube logo and
`installer\make-images.ps1` makes the icon and wizard pictures from it.
