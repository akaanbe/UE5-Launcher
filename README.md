# UE 5.8 Launcher

A lightweight, minimal Windows desktop launcher (`.NET 8` / WPF) for **Unreal Engine 5.8**.

## Building

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o ..
```
