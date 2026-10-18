# UE5 Launcher

A lightweight, minimal Windows desktop launcher (`.NET 8` / WPF) for **Unreal Engine 5**.

- Automatically detects all installed Unreal Engine 5 versions across Epic Games Launcher, Windows Registry, and standard installation directories.
- Launches immediately if a single UE5 version is installed, or lets you pick which installed UE5 version to launch if multiple versions are found.

## Building

```powershell
dotnet publish UE5Launcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .
```
