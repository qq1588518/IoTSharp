---
name: sonnetdb-docker-language-build
description: Run SonnetDB language connector builds and smoke tests inside Docker when the host machine is missing non-.NET toolchains such as Go, Rust, Python, Linux CMake, or Linux native dependencies. Use when Codex needs a Docker-backed fallback for connectors/go, connectors/rust, connectors/python, or Linux connector checks without installing host SDKs; do not use for C/Java Windows CMake validation covered by sonnetdb-connectors-cmake.
---

# SonnetDB Docker Language Build

Use this skill when a connector task is blocked by a missing host language toolchain and Docker is available. Prefer the repository's native commands when the local toolchain exists; use Docker as the reproducible fallback.

## Workflow

1. Start from the `SonnetDB` repository root.
2. Read the connector README for the language being checked.
3. Detect host tools with `Get-Command` or direct version commands.
4. If the required tool is missing, run the check in a short-lived Docker container.
5. Mount the repository read/write only when the command needs to create `artifacts/`, `bin/`, `obj/`, target outputs, or language caches.
6. Report the host tools detected, Docker image used, exact command run, and whether artifacts were created.

For C and Java Windows native connector work, use `$sonnetdb-connectors-cmake` instead. This skill is for Docker-backed language/runtime fallback paths.

## Docker Basics

On Windows PowerShell, run Docker with `${PWD}` mounted at `/work`:

```powershell
docker run --rm -v "${PWD}:/work" -w /work <image> <command>
```

If the command writes many dependency caches, prefer container-local caches or a disposable path under `artifacts/`. Do not mount secrets, host profile directories, or production configuration files.

## Go Connector

Use when `go` is missing locally and the task touches `connectors/go`.

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/go golang:1.25 `
  go test ./...
```

Run the quickstart only after a compatible native SonnetDB C library exists for the container OS, usually under `artifacts/connectors/c/linux-x64`.

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/go `
  -e SONNETDB_NATIVE_LIB_DIR=/work/artifacts/connectors/c/linux-x64 `
  golang:1.25 `
  go run ./examples/quickstart
```

## Rust Connector

Use when `cargo` or `rustc` is missing locally and the task touches `connectors/rust`.

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/rust rust:1 `
  cargo test
```

Run the quickstart only after a Linux native library is available:

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/rust `
  -e SONNETDB_NATIVE_LIB_DIR=/work/artifacts/connectors/c/linux-x64 `
  rust:1 `
  cargo run --example quickstart
```

## Python Connector

Use when `python` is missing or local Python package state is unreliable and the task touches `connectors/python`.

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/python python:3.13 `
  python -m unittest discover -s tests
```

Run the quickstart only after a Linux native library is available:

```powershell
docker run --rm -v "${PWD}:/work" -w /work/connectors/python `
  -e SONNETDB_NATIVE_LIB_DIR=/work/artifacts/connectors/c/linux-x64 `
  python:3.13 `
  python examples/quickstart.py
```

## Linux Native Library

For language quickstarts that need the C ABI on Linux, build the native library in Docker when local CMake/Linux tools are unavailable:

```powershell
docker run --rm -v "${PWD}:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet publish connectors/c/native/SonnetDB.Native/SonnetDB.Native.csproj `
  --configuration Release `
  --runtime linux-x64 `
  /p:SelfContained=true `
  --output artifacts/connectors/c/linux-x64
```

If the connector specifically requires the CMake-produced library, use a .NET SDK image and install only the missing build packages inside the disposable container:

```powershell
docker run --rm -v "${PWD}:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0 `
  bash -lc "apt-get update && apt-get install -y --no-install-recommends cmake clang zlib1g-dev && cmake -S connectors/c -B artifacts/connectors/c/linux-x64 -DSONNETDB_C_RID=linux-x64 -DCMAKE_BUILD_TYPE=Release && cmake --build artifacts/connectors/c/linux-x64"
```

## Reporting

Always report:

- Which host tools were present or missing.
- Which Docker image and command were used.
- Whether native library artifacts were required and where they came from.
- Which checks passed, failed, or were skipped.
- The first useful error lines for any failure.

Do not treat reserved connector directories as failures unless they contain real source or test entry points for the requested task.
