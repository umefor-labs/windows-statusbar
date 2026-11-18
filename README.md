# WinStatusBar

Thanh trạng thái mỏng cho Windows, gắn cố định ở cạnh dưới (hoặc trên) màn hình như một taskbar thứ hai. Cửa sổ maximize sẽ chừa chỗ cho nó. Sáng/tối theo Windows.

- Trái: usage của Claude, Codex, Antigravity (thanh trên = 5 giờ, thanh dưới = 7 ngày; di chuột để xem % và giờ reset).
- Giữa: điều khiển nhạc (bài đang phát trên Windows).
- Phải: NET, CPU, RAM, GPU, nhiệt độ CPU, nhiệt độ GPU.

## Nguồn số liệu usage

- Claude: đăng nhập claude.ai trong cửa sổ của app (bấm vào biểu tượng Claude).
- Codex: đọc log cục bộ trong `~/.codex/sessions` (cần dùng Codex ít nhất một lần).
- Antigravity: hỏi tiến trình Antigravity đang chạy trên máy.

## Build

Cần .NET 8 SDK.

```
dotnet publish src\WinStatusBar -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```

## Cài đặt

Chạy `tools\install.ps1` bằng PowerShell quyền admin (một lần). Nó:

- chép exe vào `%LOCALAPPDATA%\Programs\WinStatusBar`,
- cho thanh bar tự chạy khi đăng nhập (quyền thường, qua khóa Run),
- tạo scheduled task "WinStatusBar Sensors" chạy `WinStatusBar.exe --sensors` quyền admin để đọc nhiệt độ CPU (cần driver PawnIO: `winget install namazso.PawnIO`).

Gỡ: `tools\uninstall.ps1`. Log lỗi: `%LOCALAPPDATA%\WinStatusBar\error.log`.

Chuột phải vào thanh để chuyển lên trên/xuống dưới hoặc thoát.
