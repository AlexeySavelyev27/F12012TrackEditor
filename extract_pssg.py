import argparse
import struct
from pathlib import Path

from bxml2xml import bxml_to_xml


def read_node(f, depth=0, limit=None):
    start = f.tell()
    if limit is not None and start >= limit:
        return []

    length_data = f.read(4)
    if len(length_data) < 4:
        return []
    name_len = struct.unpack('>I', length_data)[0]

    name_bytes = f.read(name_len)
    if len(name_bytes) < name_len:
        return []
    name = name_bytes.decode('ascii', errors='replace')

    header = f.read(8)
    if len(header) < 8:
        return []
    flags, child_count = struct.unpack('>II', header)

    nodes = [(depth, name, start)]
    for _ in range(child_count):
        nodes.extend(read_node(f, depth + 1, limit))
    return nodes


def extract(input_path: Path, out_dir: Path):
    with input_path.open('rb') as f:
        if f.read(4) != b'PSSG':
            raise ValueError('Not a PSSG file')
        size, str_off, root_off = struct.unpack('>III', f.read(12))
        f.seek(root_off - 2)  # heuristic for root name length
        nodes = read_node(f, limit=str_off)
    # Extraction of actual data is not implemented yet
    out_dir.mkdir(parents=True, exist_ok=True)
    outline = ['%s%s' % ('  ' * d, n) for d, n, _ in nodes]
    (out_dir / 'listing.txt').write_text('\n'.join(outline))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Extract contents of a PSSG/ENS archive')
    parser.add_argument('input', type=Path, help='Input .pssg or .ens file')
    parser.add_argument('output_dir', type=Path, help='Directory to extract to')
    args = parser.parse_args()
    extract(args.input, args.output_dir)
