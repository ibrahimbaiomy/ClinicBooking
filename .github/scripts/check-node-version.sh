#!/usr/bin/env bash
# Fails when the Node major version differs between the three places that name it, so local
# development, CI and the Docker image cannot drift apart (D47):
#
#   src/clinic-booking-web/.nvmrc            e.g. "24"
#   Dockerfile                               e.g. "FROM node:24-bookworm-slim AS web"
#   src/clinic-booking-web/package.json      e.g. "engines": { "node": "^24.15.0" }
#
# Strict on purpose: if a major cannot be extracted from any of them, the script fails with a
# message instead of passing silently. Paths can be given as arguments (used to test the script).
#
#   .github/scripts/check-node-version.sh [nvmrc] [Dockerfile] [package.json]

set -euo pipefail

nvmrc="${1:-src/clinic-booking-web/.nvmrc}"
dockerfile="${2:-Dockerfile}"
package="${3:-src/clinic-booking-web/package.json}"

fail() {
  echo "::error title=Node version drift::$1"
  echo "ERROR: $1" >&2
  exit 1
}

for file in "$nvmrc" "$dockerfile" "$package"; do
  [ -f "$file" ] || fail "$file does not exist"
done

# .nvmrc: a single line such as "24", "v24" or "24.15.0". Aliases like "lts/*" are rejected.
nvmrc_value="$(head -n 1 "$nvmrc" | tr -d '\r' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^v//')"
if ! printf '%s' "$nvmrc_value" | grep -Eq '^[0-9]+(\.[0-9]+){0,2}$'; then
  fail "cannot read a Node version from $nvmrc (found '$nvmrc_value'; expected e.g. 24)"
fi
nvmrc_major="${nvmrc_value%%.*}"

# Dockerfile: every "FROM node:<major>..." line must name the same major.
docker_majors="$(tr -d '\r' < "$dockerfile" | sed -n -E 's/^FROM[[:space:]]+node:([0-9]+).*/\1/p' | sort -u)"
[ -n "$docker_majors" ] || fail "no 'FROM node:<major>' line found in $dockerfile"
[ "$(printf '%s\n' "$docker_majors" | wc -l)" -eq 1 ] || fail "$dockerfile uses more than one Node major: $(printf '%s ' $docker_majors)"
docker_major="$docker_majors"

# package.json: engines.node must be one range (no "||"), whose first number is the major.
engines="$(tr -d '\r' < "$package" | sed -n -E 's/.*"node"[[:space:]]*:[[:space:]]*"([^"]*)".*/\1/p' | head -n 1)"
[ -n "$engines" ] || fail "no engines.node found in $package"
case "$engines" in
  *'||'*) fail "engines.node in $package is '$engines'; it must name one Node major" ;;
esac
engines_major="$(printf '%s' "$engines" | sed -n -E 's/^[^0-9]*([0-9]+).*/\1/p')"
[ -n "$engines_major" ] || fail "cannot read a Node major from engines.node '$engines' in $package"

if [ "$nvmrc_major" != "$docker_major" ] || [ "$nvmrc_major" != "$engines_major" ]; then
  fail "Node majors differ: .nvmrc=$nvmrc_major, Dockerfile=$docker_major, engines=$engines_major (they must all agree)"
fi

echo "Node major $nvmrc_major agrees: .nvmrc, Dockerfile (node:$docker_major) and engines ($engines)."
