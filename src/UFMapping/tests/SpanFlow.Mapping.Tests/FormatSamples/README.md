# Format samples (E73)

One folder per codec type (`json/`, later `xml/`, `text/`, `edi/`, `positional/`). FormatSamplesTests runs every
sample; a codec with no sample folder fails the build.

| File | Required | Meaning |
|---|---|---|
| `<name>.raw.<ext>` | yes | The raw input, read as bytes |
| `<name>.expected.json` | yes | The JSON it must decode to |
| `<name>.options.json` | no | Codec options for both directions |
| `<name>.decode-options.json` | no | Extra options for reading only (merged over the above) |
| `<name>.encode-options.json` | no | Extra options for writing only (merged over the above) |
| `<name>.schema.xsd` | no | An XSD given to the reader as its `xsd` option (xml) |
| `<name>.encoded.<ext>` | no | The exact text writing the expected JSON must give |

Every sample is also round-tripped: the expected JSON is written, read back, and must be unchanged.
