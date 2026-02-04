# Crab Game Mono Creator

Created by **64bitdev**

Crab Game Mono Creator is a tool that builds a **Mono-compatible Crab Game install** by combining the Windows and macOS versions of the game and applying managed assembly patches.

In addition to rebuilding the game, the tool renames **class names, methods, and variables** strictly according to mapping files. By default, mappings are downloaded from `64bitdev/crabgamemappings`.

---

## Installation / Setup Guide

### Requirements
- Steam installed
- A valid Windows copy of Crab Game
- Internet connection (for mappings and Steam depots)

---

## Step 1: Obtain the macOS Mono Build (Required)

Crab Game Mono Creator **requires the macOS Mono build** of Crab Game. You do **not** need a Mac to obtain it.

- The **Windows version** of Crab Game uses **IL2CPP** and cannot be deobfuscated
- The **macOS version** uses **Mono**, which provides the managed assemblies required for deobfuscation and patching

To download the macOS build:

1. Open the Steam console:
   - Press `Win + R`
   - Enter:
     ```
     steam://open/console
     ```

2. Run the following command:
   ```
   download_depot 1782210 1782212 4682332062135883449
   ```

3. Wait for the download to complete. Steam will print the download path.

Example output path:
```
C:\Program Files (x86)\Steam\steamapps\content\app_1782210\depot_1782212
```

This directory will be used as the **Crab Game Mac Directory**.

---

## Step 2: Run Crab Game Mono Creator

1. Launch the program
2. When prompted, download the latest mappings (recommended)
3. Provide the following directories:
   - Crab Game Mac Directory
   - Crab Game Win Directory
   - Crab Game Mono Output Directory (recommended: `Game`)

If the output directory does not exist, it will be created automatically.

---

## What the Tool Does

When run, Crab Game Mono Creator performs the following steps:

1. Ensures a valid Crab Game mapping file is available
2. Copies all required files from the **Windows** Crab Game install
3. Copies required native and Mono files
4. Deobfuscates **classes, methods, and variables** according to mappings
5. Patches **managed assemblies only**
6. Rewrites Unity asset references to match patched assemblies

Extremely verbose console output is **normal and expected**.

---

## Deobfuscation Behavior

- Renaming is **entirely mapping-driven**
- Symbols present in the mapping file are renamed
- Symbols not present in the mapping file are left unchanged
- Assembly structure is preserved
- Unity engine assemblies are **never modified**

Expanding deobfuscation coverage is done by updating **mapping files**, not by changing the tool.

---

## Custom Game Executable and Console Window

The generated Mono build uses a **custom Crab Game executable** when launching the game.

When the game is started, a warning window may appear explaining that:
- A custom executable is being used
- A console window will be opened

This behavior is **intentional**.

### Why a Custom Executable Is Used

The custom executable exists **only to provide a console window for logging and debugging**.

- The regular `Crab Game.exe` could technically be used
- However, it does not provide a visible console for standard output
- The custom executable simply launches the game with an attached console

No gameplay logic is changed by this executable.

### Console Output

Because this executable is console-based:
- A console window will open when the game starts
- Game logs are printed directly to **standard output**

This includes:
- Unity logs
- Managed code logs
- Error and debug output

The console window is **not an error** and does **not indicate a crash**.

Closing the console window will close the game.

---

## --vanilla Mode (Static Analysis Only)

Crab Game Mono Creator supports a special command-line flag:

```
--vanilla
```

**WARNING:** This mode is intended **ONLY** for static analysis.

When `--vanilla` is used:
- Modder-specific helpers are disabled
- Wrapper generation is skipped
- Convenience accessors are not emitted
- Output is kept as close to the original Mono assemblies as possible

This mode is **NOT** intended for:
- Playing the game
- Running mods
- General use

Use `--vanilla` **only** if you are performing static analysis or reverse engineering.

---

## Stability and Bug Reporting

Crab Game Mono Creator is **experimental and actively under development**.

Bugs, crashes, incomplete deobfuscation, or unexpected behavior are **expected**, especially when mappings change.

### Reporting Bugs

Please include:
- Crab Game version (Windows and macOS depots if applicable)
- Full console output
- Operating system
- Mapping source used
- Any changes made to output files

Submit bug reports via **GitHub Issues** on this repository.

---

## Important Notes About Crab Game Versioning and Obfuscation

Crab Game Mono Creator relies heavily on **Beebyte-based obfuscation** used by current Crab Game builds.

### Version Status
- Crab Game has been effectively unchanged since 2022
- Current mappings are built specifically for this state

### Future Compatibility Warning

If Crab Game receives an update that:
- Removes or changes Beebyte obfuscation
- Switches obfuscators
- Restructures assemblies or assets

This tool will likely break. Existing mappings will become invalid and patching may fail.

---

## Why the macOS Mono Build Is Required

- Windows Crab Game builds are **IL2CPP**
- macOS builds use **Mono**
- Mono assemblies are required for managed deobfuscation and patching

Crab Game Mono Creator uses macOS Mono assemblies as the **source of managed code** and combines them with Windows game data.

Without the macOS Mono build, this tool cannot function.


-----


# MapCompress

Created by **64bitdev**

MapCompress is a small utility used to **compress a folder of JSON mapping files** into a single `.jecgm` file.

It is primarily intended for use with **Crab Game Mono Creator** mapping workflows, but it can be used on its own.

MapCompress does **not** modify mappings, analyze content, or perform deobfuscation.
It only compresses JSON files into a packaged `.jecgm` archive.

---

## What MapCompress Does

When run, MapCompress:

- Takes the **current working directory**
- Finds JSON mapping files
- Compresses them into a single `.jecgm` file

No other processing is performed.

---

## Usage

MapCompress is run from the command line.

```
mapcompress --compress json
```

### Important Notes

- MapCompress operates on the **folder it is run in**
- All relevant JSON files in that folder will be included
- The output will be a `.jecgm` file in the same directory

---

## Example

Given a folder structure like this:

```
mappings/
  classes.json
  methods.json
  fields.json
```

Running:

```
mapcompress --compress json
```

Will produce:

```
mappings.jecgm
```

The original JSON files are **not deleted or modified**.

---

## Output Format

- Output file extension: `.jecgm`
- The `.jecgm` file is a **compressed container** of the JSON files
- It is designed to be consumed by tools that understand the `.jecgm` format

MapCompress itself does not validate or interpret the contents of the JSON files.

---

## Limitations

- Only compression is supported
- No decompression mode is provided
- No validation or schema checking is performed
- The tool assumes valid JSON input

---

## Intended Use

MapCompress is intended for:

- Packing mapping files for distribution
- Reducing the number of files required to ship mappings
- Creating `.jecgm` files for use with Crab Game Mono Creator

It is **not** intended to:

- Edit mappings
- Generate mappings
- Perform deobfuscation

---

## Notes

- The tool is intentionally minimal
- Any errors encountered are usually related to file access or invalid JSON
- Console output is minimal by design

---

## License / Disclaimer

This tool is provided as-is.
No guarantees are made regarding compatibility with future mapping formats.

