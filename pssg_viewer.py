import argparse
from pathlib import Path

from pssg import PSSGArchive


def print_tree(node, indent=0):
    print("  " * indent + node.name)
    for child in node.children:
        print_tree(child, indent + 1)


def main():
    parser = argparse.ArgumentParser(description="Inspect contents of a PSSG archive")
    parser.add_argument("input", type=Path, help="Input .pssg/.ens file")
    parser.add_argument("--header", action="store_true", help="Print header information")
    parser.add_argument("--strings", action="store_true", help="List all strings")
    parser.add_argument("--tree", action="store_true", help="Display node hierarchy")
    args = parser.parse_args()

    archive = PSSGArchive.load(args.input)

    if args.header:
        print(f"File size: {archive.size}")
        print(f"String table offset: {archive.string_table_offset}")
        print(f"Root node offset: {archive.root_offset}")

    if args.strings:
        for s in archive.strings:
            print(s)

    if args.tree or (not args.header and not args.strings):
        print_tree(archive.root)


if __name__ == "__main__":
    main()
