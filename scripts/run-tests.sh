#!/usr/bin/env bash
# Run the test suite reliably on WSL.
#
# Running the suite directly over /mnt (drvfs / the Windows drive) wedges the mount
# under the test IO load — the process can SIGABRT and the whole mount starts
# returning EIO. So when the checkout lives on /mnt we rsync it to an ext4 work dir
# and run there; on a native-Linux checkout we run in place.
#
# Tests use TUnit on Microsoft.Testing.Platform (MTP opt-in via global.json), so
# `dotnet test` needs --solution / --project. On the net10.0-windows TFM we also
# set EnableWindowsTargeting for the Linux host.
#
# Usage:
#   scripts/run-tests.sh                 # whole solution
#   scripts/run-tests.sh --project tests/ArtifactView.Core.Tests/ArtifactView.Core.Tests.csproj
#   ARTIFACTVIEW_TEST_DIR=/path scripts/run-tests.sh   # override ext4 work dir
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd -P)"

export EnableWindowsTargeting=true

case "$ROOT" in
  /mnt/*)
    WORK="${ARTIFACTVIEW_TEST_DIR:-$HOME/.cache/artifactview-test}"
    echo "drvfs checkout ($ROOT) — syncing to ext4 work dir: $WORK"
    mkdir -p "$WORK"
    rsync -a --delete \
      --exclude 'bin/' --exclude 'obj/' --exclude '.git/' --exclude '.vs/' \
      --exclude 'artifacts/' --exclude 'TestResults/' \
      "$ROOT/" "$WORK/"
    cd "$WORK"
    ;;
esac

if [ "$#" -gt 0 ]; then
  exec dotnet test "$@"
else
  exec dotnet test --solution ArtifactView.sln
fi
