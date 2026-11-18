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

Mở PowerShell và chạy:

```powershell
irm https://raw.githubusercontent.com/umefor-labs/windows-statusbar/main/tools/install.ps1 | iex
```

Windows sẽ hỏi quyền admin một lần. Gỡ cài đặt:

```powershell
irm https://raw.githubusercontent.com/umefor-labs/windows-statusbar/main/tools/uninstall.ps1 | iex
```

Script cài đặt:

- cài .NET 8 Desktop Runtime nếu máy chưa có,
- cài driver PawnIO qua winget (để đọc nhiệt độ CPU),
- tải `WinStatusBar.exe` bản mới nhất từ Releases và chép vào `%LOCALAPPDATA%\Programs\WinStatusBar`,
- cho thanh bar tự chạy khi đăng nhập (quyền thường, qua khóa Run),
- tạo scheduled task "WinStatusBar Sensors" chạy `WinStatusBar.exe --sensors` quyền admin để đọc nhiệt độ CPU.

Cài từ bản tự build: `.\tools\install.ps1 -Source .\publish\WinStatusBar.exe`. Log cài đặt: `%TEMP%\WinStatusBar-install.log`; log của app: `%LOCALAPPDATA%\WinStatusBar\error.log`.

Phát hành bản mới: đẩy tag dạng `v1.0.0`, GitHub Actions sẽ build và tạo Release kèm exe.

Chuột phải vào thanh để chuyển lên trên/xuống dưới hoặc thoát.
