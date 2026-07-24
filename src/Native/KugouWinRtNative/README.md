# KugouWinRtNative

This Windows-only `cdylib` isolates the WinRT system media transport controls
from the NativeAOT desktop executable.

```powershell
cargo build --release
Copy-Item .\target\release\KugouWinRtNative.dll ..\..\Apps\KugouAvaloniaPlayer\Native\ -Force
```

The managed application loads the DLL explicitly after the main window opens.
If the DLL or WinRT is unavailable, system media integration is disabled while
the rest of the application continues running.
