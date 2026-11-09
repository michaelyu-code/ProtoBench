# Synthetic fixtures

The log and fixed-width generation samples are synthetic framework examples, with no claimed HP NonStop business meaning.

`cp500-codepoints.txt` lists one Unicode code point per line (four hexadecimal digits), corresponding to CP500 byte values `00` through `FF` in order. It was generated independently of the .NET converter using Python's standard-library `cp500` codec:

```python
from pathlib import Path
Path('cp500-codepoints.txt').write_text(
    ''.join(format(ord(bytes([b]).decode('cp500')), '04X') + '\n'
            for b in range(256)), encoding='ascii')
```

Tests compare every decoded character against this fixture and verify exact byte round-tripping. Additional inline known vectors distinguish CP500 punctuation from other EBCDIC code pages.
