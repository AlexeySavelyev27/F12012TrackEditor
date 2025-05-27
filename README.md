# F12012TrackEditor
A tool for editing existing and creating new tracks for F1 2012
See [docs/file-format-map.md](docs/file-format-map.md) for a description of the included Melbourne track files.

## Utilities

`bxml2xml.py` converts Codemasters BXML files to regular XML. Usage:

```bash
python3 bxml2xml.py <input.bxml> [output.xml]
```

Only the simple BXML variant starting with `00 42 58 4D` is currently
supported. Files starting with `1A 22 52 72` will raise
`NotImplementedError`.
