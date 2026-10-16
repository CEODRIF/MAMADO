#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# AusbildungsWeg Interview — pinned whisper.cpp native runtime (Linux dev).
#
# The Linux counterpart of build-whisper.ps1: builds the EXACT pinned
# whisper.cpp commit into shared libraries and stages them (with the SONAME
# symlinks the dynamic loader needs) so the app's native P/Invoke path and
# the real-transcription tests can run on a dev machine.
#
# Usage:
#   scripts/build-whisper.sh                      # checkout ../tools/whisper.cpp,
#                                                 # stage into native/linux-x64
#   scripts/build-whisper.sh <source-dir> [out]   # reuse/custom paths
#
# Requirements: git, cmake, a C++20 compiler, make. No admin rights; the
# result is fully offline after the one-time source fetch.
# -----------------------------------------------------------------------------
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

PIN_SHA="0c7be696371069e0baa2515ed7aa08d5a5473aa6"   # libwhisper 1.9.5 / ggml 0.26.0
repo_url="https://github.com/ggml-org/whisper.cpp"

source_dir="${1:-$repo/../tools/whisper.cpp}"
output_dir="${2:-$repo/native/linux-x64}"

if [ -d "$source_dir/.git" ]; then
    git -C "$source_dir" checkout --quiet "$PIN_SHA"
    git -C "$source_dir" submodule update --init --recursive --quiet
else
    echo "==> Fetching whisper.cpp source (one time)"
    git clone --quiet "$repo_url" "$source_dir"
fi
head="$(git -C "$source_dir" rev-parse HEAD)"
if [ "$head" != "$PIN_SHA" ]; then
    echo "Pinned SHA mismatch: got $head, expected $PIN_SHA" >&2
    exit 1
fi

build_dir="$source_dir/build"
echo "==> cmake configure (Release, shared libs, portable CPU code)"
cmake -S "$source_dir" -B "$build_dir" -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=ON -DGGML_NATIVE=OFF
echo "==> cmake build"
cmake --build "$build_dir" --config Release --parallel
