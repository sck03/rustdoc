#!/bin/sh
# Run inside the official PostgreSQL 18 image; preserve the runtime library closure.
set -eu
destination=$1
source_bin=/usr/lib/postgresql/18/bin
mkdir -p "$destination/bin" "$destination/lib" "$destination/licenses"
for tool in pg_dump pg_restore psql; do
  cp "$source_bin/$tool" "$destination/bin/$tool"
done
for tool in pg_dump pg_restore psql; do ldd "$source_bin/$tool"; done |
  awk '/=> \/[^ ]+/ { print $3 } /^[[:space:]]*\/[^ ]+/ { print $1 }' | sort -u |
  while IFS= read -r library; do
    name=$(basename "$library")
    case "$name" in libc.so.*|libm.so.*|libpthread.so.*|libdl.so.*|librt.so.*|ld-linux*|ld-musl*) continue ;; esac
    cp -L "$library" "$destination/lib/$name"
  done
for directory in /usr/share/doc/*; do
  if [ -f "$directory/copyright" ]; then cp "$directory/copyright" "$destination/licenses/$(basename "$directory").copyright"; fi
done
cp /usr/share/doc/postgresql-18/copyright "$destination/POSTGRESQL_LICENSE.txt"
chmod 0755 "$destination/bin/pg_dump" "$destination/bin/pg_restore" "$destination/bin/psql"
chmod 0644 "$destination"/lib/* "$destination/POSTGRESQL_LICENSE.txt"
