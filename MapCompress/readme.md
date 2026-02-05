# Map Compress

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
Root
	/dll1
	/dll2
	/dll3
	cgmapinfo.json
	mapcompress
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
