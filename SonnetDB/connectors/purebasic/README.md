# SonnetDB PureBasic Connector

The PureBasic connector is a source include file over the stable SonnetDB C ABI from `connectors/c`.

It exposes:

- `SonnetDB_Load` / `SonnetDB_Unload`
- `SonnetDB_Open` / `SonnetDB_Execute` / `SonnetDB_ExecuteNonQuery`
- forward-only result cursor procedures with typed getters
- `SonnetDB_Flush`, `SonnetDB_Version`, and `SonnetDB_LastError`

The connector loads `SonnetDB.Native.dll` or `SonnetDB.Native.so` dynamically at runtime through `OpenLibrary`, so it does not need a separate PureBasic-built wrapper library.

## CI Policy

PureBasic has a command-line compiler and can build executables or dynamic libraries, but the compiler is proprietary and is not preinstalled on GitHub-hosted runners. There is no official GitHub Actions setup action that can provision a licensed PureBasic toolchain for this repository.

SonnetDB therefore keeps the PureBasic connector as source and does not build PureBasic executables or PureBasic-produced dynamic libraries in CI. Teams that own a PureBasic license can build it on a local machine or on a self-hosted runner.

## Requirements

- PureBasic with the same architecture as `SonnetDB.Native`.
- A native SonnetDB C library built for the current platform:
  - Windows: `SonnetDB.Native.dll`
  - Linux: `SonnetDB.Native.so`

Build the C connector first:

```powershell
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release
```

## Run the Quickstart on Windows

Copy or add `SonnetDB.Native.dll` to `PATH`, then compile `examples/quickstart.pb` with PureBasic:

```powershell
$native = (Resolve-Path ../../artifacts/connectors/c/win-x64/Release).Path
$env:PATH = "$native;$env:PATH"
pbcompiler examples\quickstart.pb --console --output quickstart.exe
.\quickstart.exe
```

## Run the Quickstart on Linux

```bash
cmake -S connectors/c --preset linux-x64
cmake --build artifacts/connectors/c/linux-x64

cd connectors/purebasic
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
pbcompiler examples/quickstart.pb --console --output quickstart
LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" ./quickstart
```

## API Sketch

```purebasic
XIncludeFile "src/SonnetDB.pbi"

If SonnetDB_Load()
  *connection = SonnetDB_Open("./data-purebasic")
  SonnetDB_ExecuteNonQuery(*connection, "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)")
  *result = SonnetDB_Execute(*connection, "SELECT time, host, usage FROM cpu LIMIT 10")

  While SonnetDB_ResultNext(*result) = 1
    Debug Str(SonnetDB_ResultValueInt64(*result, 0)) + " " + SonnetDB_ResultValueText(*result, 1)
  Wend

  SonnetDB_ResultFree(*result)
  SonnetDB_Close(*connection)
EndIf
```
