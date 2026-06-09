import os
import sys
import zipfile


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: package_zip.py <source_directory> <destination_zip>", file=sys.stderr)
        return 2

    source_dir = os.path.abspath(sys.argv[1])
    destination_zip = os.path.abspath(sys.argv[2])

    if not os.path.isdir(source_dir):
        print(f"Source directory does not exist: {source_dir}", file=sys.stderr)
        return 1

    os.makedirs(os.path.dirname(destination_zip), exist_ok=True)
    if os.path.exists(destination_zip):
        os.remove(destination_zip)

    with zipfile.ZipFile(destination_zip, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for root, _, files in os.walk(source_dir):
            for file_name in files:
                file_path = os.path.join(root, file_name)
                archive_name = os.path.relpath(file_path, source_dir).replace(os.sep, "/")
                archive.write(file_path, archive_name)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
