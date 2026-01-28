# Crab Game Mono Creator

Created by **64bitdev**

Crab Game Mono Creator is a tool that builds a **Mono-compatible Crab Game install**
by combining the Windows and macOS versions of the game and applying managed
assembly patches.

In addition to rebuilding the game, the tool **deobfuscates most class names**.
Full deobfuscation of **functions and variables is planned** for future releases.

---

## Installation / Setup Guide

### Requirements
- Steam installed
- A valid Windows copy of Crab Game
- Internet connection (for mappings and Steam depots)

### Step 1: Obtain the macOS Mono Build (Required)

Crab Game Mono Creator **requires the macOS Mono build** of Crab Game.
You do **not** need a Mac to obtain it.

The Windows version of Crab Game uses **IL2CPP** and cannot be deobfuscated.
The macOS version uses **Mono**, which provides the managed assemblies needed
for deobfuscation and patching.

To download the macOS build:

1. Open the Steam console:
   - Press `Win + R`
   - Enter:
     ```
     steam://open/console
     ```

2. Run the following command in the Steam console:
   ```
   download_depot 1782210 1782212 4682332062135883449
   ```

3. Wait for the download to complete.
   Steam will print the download path.

Example output path:
```
C:\Program Files (x86)\Steam\steamapps\content\app_1782210\depot_1782212
```

You will use this folder later as the **Crab Game Mac Directory**.

---

### Step 2: Run Crab Game Mono Creator

1. Launch the program.
2. When prompted, download the latest mappings (recommended).
3. Provide the following directories when asked:
   - Crab Game Mac Directory (from Step 1)
   - Crab Game Win Directory (your installed Windows version)
   - Crab Game Mono Output Directory (recommended: `Game`)

If the output directory does not exist, it will be created automatically.

---

---

## What the Tool Does

When run, Crab Game Mono Creator performs the following steps:

1. Ensures a valid Crab Game mapping file is available
2. Copies all required files from the **Windows** Crab Game install
3. Copies required native and Mono files
4. **Deobfuscates most managed class names**
5. Patches **managed assemblies only**
6. Rewrites Unity asset references to match the patched assemblies

A very large amount of console output is **normal and expected**.

---

## Deobfuscation Status

**Current behavior**
- Most class names are deobfuscated
- Assembly structure is preserved
- Unity engine assemblies are not modified

**Planned behavior**
- Deobfuscation of functions
- Deobfuscation of fields / variables
- More complete symbol recovery across assemblies

---

## First Run Behavior

On first launch, the tool will attempt to locate `cgmonomap.jecgm`.

If it is not found, you will be prompted to choose a source:

```text
=== Crab Game Mono Creator ===
Created by 64bitdev

Select one of the following
1. Download from 64BitDev/CrabGameMappings
2. Use Local File
```

Choosing option **1** will automatically download the latest compatible mapping file.

---

## Directory Selection

You will be prompted to provide three directories:

```text
Crab Game Mac Directory:
C:\Program Files (x86)\Steam\steamapps\content\app_1782210\depot_1782212

Crab Game Win Directory:
C:\Program Files (x86)\Steam\steamapps\common\Crab Game

Crab Game Mono Output Directory:
Game
```

If the output directory does not exist, it will be created automatically.

---

## Copying Windows Game Files

The tool copies all required Windows game files, including:

- Executables
- Unity data files
- Level files
- Shared asset bundles
- Required native plugins

Example output:

```text
Copying Windows Crab Game Files
Copying Windows file Crab Game.exe
Copying Windows file UnityCrashHandler64.exe
Copying Windows file Crab Game_Data\globalgamemanagers
Copying Windows file Crab Game_Data\sharedassets0.assets
...
```

This phase produces a very large amount of output due to the number of files involved.

---

## Managed Assembly Patching

After file copying, the tool begins patching managed assemblies.

Only **non-Unity assemblies** are modified.

Example output:

```text
Starting to patch Managed Assemblys
Trying to patch Assembly-CSharp.dll
Trying to patch Assembly-CSharp-firstpass.dll
Trying to patch Newtonsoft.Json.dll
```

Unity engine assemblies are intentionally skipped:

```text
Skiped file UnityEngine.CoreModule.dll because it was a unity file
Skiped file UnityEngine.dll because it was a unity file
```

This is expected behavior.

---

## Unity File Rewriting

Once assemblies are patched, Unity files are rewritten to reference the updated assemblies.

```text
Rewriting Unity Files
Creating List of assets to replace
Doing File Game\Crab Game_Data\globalgamemanagers
Doing File Game\Crab Game_Data\level0
Doing File Game\Crab Game_Data\sharedassets0.assets
...
```

This step may take time depending on disk speed.

---

## Completion

When finished, the tool will exit normally after all assemblies and Unity files have been processed.

No errors at the end indicates a successful build.

---

## Notes

- Extremely verbose output is **intentional**
- Skipped Unity files are **not errors**
- The tool currently focuses on **class-level deobfuscation**
- Full function and variable deobfuscation is **planned**
- The process may take several minutes
- Disk usage will be high during execution


---

## Stability and Bug Reporting

Crab Game Mono Creator is **experimental and actively under development**.

Bugs, crashes, incomplete deobfuscation, or unexpected behavior are **expected**,
especially when new Crab Game versions are released or mappings change.

### What to Expect
- The program may fail on some game versions
- Deobfuscation may be partial or incorrect
- Some assemblies or assets may not be processed correctly
- Error messages may be minimal or missing

### Reporting Bugs

If you encounter a bug, please report it with as much information as possible.

Include:
- Your Crab Game version (Windows and macOS depots if applicable)
- The exact console output or error message
- Your operating system
- Whether you used downloaded or local mappings
- Any changes you made to the output files

Bug reports should be submitted via:
- GitHub Issues on this repository

Clear bug reports help improve stability and speed up fixes.


---

## Important Notes About Crab Game Versioning and Obfuscation

Crab Game Mono Creator **relies heavily on Beebyte-based obfuscation behavior**
used by the currently available Crab Game builds.

### Version Status

- Crab Game has effectively been **stuck on the same version since 2022**
- No meaningful official updates have been released since then
- Current mappings and deobfuscation logic are built specifically around this state

### Future Compatibility Warning

If Crab Game ever receives a revival, major update, or re-release that:
- Changes or removes Beebyte obfuscation
- Switches to a different obfuscator
- Significantly restructures assemblies or assets

**This tool will very likely break.**

In that case:
- Existing mappings will become invalid
- Deobfuscation may fail completely
- Assembly patching may produce incorrect or unusable results

This is an inherent limitation of tools that rely on stable obfuscation behavior.

---



---

## Why the macOS Mono Build Is Required

Most users are not aware that **Crab Game has a macOS Mono build**. This tool relies on that build for a critical reason:

- The **Windows version** of Crab Game is IL2CPP
- The **macOS version** of Crab Game uses **Mono**
- Mono assemblies are required to perform meaningful deobfuscation and patching

Crab Game Mono Creator uses the macOS Mono assemblies as the **source of managed code** and combines them with the Windows game data to produce a usable Mono-based build.

Without the macOS Mono build, this tool cannot function.

---