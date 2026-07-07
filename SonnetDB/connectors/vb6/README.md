# SonnetDB Visual Basic 6 Connector

The VB6 connector is a source-level wrapper for classic Visual Basic 6 applications.

VB6 is a 32-bit Windows runtime and its `Declare` calls use the stdcall calling convention. The core SonnetDB native ABI is cdecl, so this connector includes a tiny x86 stdcall bridge DLL:

- `native/sonnetdb_vb6_bridge.c` loads `SonnetDB.Native.dll` and forwards calls to the C ABI.
- `src/SonnetDbNative.bas` declares the bridge functions and handles UTF-16/UTF-8 string conversion through the bridge.
- `src/SonnetDbConnection.cls` and `src/SonnetDbResult.cls` expose a small VB6-friendly API.

## CI Policy

GitHub-hosted runners do not include a licensed VB6 IDE/compiler toolchain, and VB6 is not available through an official GitHub Actions setup action. SonnetDB therefore does not build VB6 projects or VB6-produced dynamic libraries in CI.

The C bridge can be built locally with a Windows x86 C toolchain. It is intentionally separate from the VB6 project so VB6 applications can import the source modules and compile with their own licensed VB6 environment.

## Requirements

- Visual Basic 6 IDE/compiler on Windows.
- Visual Studio C++ toolchain for `Win32` if building the bridge.
- A 32-bit SonnetDB native library:
  - `SonnetDB.Native.dll` built from `connectors/c` with RID `win-x86`.
  - `SonnetDB.VB6.Native.dll` built from this connector.

## Build Native Libraries

Build the SonnetDB C ABI for x86:

```powershell
cmake -S connectors/c --preset windows-x86
cmake --build artifacts/connectors/c/win-x86 --config Release
```

Build the VB6 stdcall bridge:

```powershell
cmake -S connectors/vb6 --preset windows-x86
cmake --build artifacts/connectors/vb6/win-x86 --config Release
```

Place these files next to the VB6 executable or in a directory on `PATH`:

- `SonnetDB.Native.dll`
- `SonnetDB.VB6.Native.dll`

## VB6 API Sketch

```vb
Dim connection As SonnetDbConnection
Dim result As SonnetDbResult

Set connection = New SonnetDbConnection
connection.Open App.Path & "\data-vb6"

connection.ExecuteNonQuery "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)"
connection.ExecuteNonQuery _
    "INSERT INTO cpu (time, host, usage) VALUES (1710000000000, 'edge-1', 0.42)"

Set result = connection.Execute("SELECT time, host, usage FROM cpu LIMIT 10")
Do While result.NextRow
    Debug.Print result.GetInt64Text(0), result.GetString(1), result.GetDouble(2)
Loop

result.Close
connection.Close
```

`GetInt64` returns a `Double` for convenience; `GetInt64Text` preserves the exact 64-bit integer representation for timestamps and large counters.
