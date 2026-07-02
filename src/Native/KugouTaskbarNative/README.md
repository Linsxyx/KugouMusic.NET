# KugouTaskbarNative


Build:

```powershell
cargo build --release
Copy-Item .\target\release\KugouTaskbarNative.dll ..\..\Apps\KugouAvaloniaPlayer\Native\ -Force
```

The exported ABI intentionally stays aligned with `WindowsTaskbarThumbnailToolbar.cs`:

- `KgTaskbarToolbar_Create`
- `KgTaskbarToolbar_UpdatePlayPause`
- `KgTaskbarToolbar_UpdateEnabled`
- `KgTaskbarToolbar_UpdateLike`
- `KgTaskbarToolbar_Destroy`
