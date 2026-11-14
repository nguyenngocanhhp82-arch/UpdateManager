# Win Update Manager

Công cụ quản trị tập trung bản cập nhật Windows và ứng dụng, đóng gói thành 1 file `.exe` tự chứa (self-contained), không cần cài đặt thêm runtime .NET, giao diện trực quan bằng tiếng Việt cho IT quản lý máy trạm văn phòng.

---

## 1. Cấu trúc Solution

```
WinUpdateManager/
├─ WinUpdateManager.sln
├─ Directory.Build.props
├─ BAN_GIAO_WinUpdateManager.md
├─ .github/workflows/build.yml        # CI/CD GitHub Actions (Windows runner)
├─ src/
│   ├─ WinUpdateManager.App/          # WPF UI (MVVM CommunityToolkit, Views, ViewModels, app.manifest)
│   │   ├─ Views/
│   │   ├─ ViewModels/
│   │   ├─ Converters/
│   │   ├─ App.xaml / App.xaml.cs
│   │   ├─ MainWindow.xaml / MainWindow.xaml.cs
│   │   ├─ Program.cs                 # Xử lý crash, ghi log khởi động, cờ --selftest
│   │   └─ app.manifest               # requireAdministrator
│   └─ WinUpdateManager.Core/         # Lớp dịch vụ và mô hình dữ liệu (thuần C#, độc lập UI)
│       ├─ Models/                    # LogLevel, LogEntry, ProcessResult
│       └─ Services/                  # ProcessRunner, LogService, WingetService,...
└─ tests/
    └─ WinUpdateManager.Tests/        # xUnit: kiểm tra ProcessRunner, LogService, XamlValidation
```

## 2. Các tính năng chính (5 Tabs)

1. **Cập nhật ứng dụng**: Quét và cập nhật phần mềm qua `winget`, hỗ trợ nút riêng cho WhatsApp (Store / Win32), danh sách ưu tiên.
2. **Windows Update**: Quét, tải và cài đặt bản cập nhật Windows qua Windows Update Agent COM API (`Microsoft.Update.Session`).
3. **Gỡ bản cập nhật Windows**: Liệt kê KB đã cài (WMI), gỡ bản vá qua `wusa` hoặc `dism`, tự động ẩn KB để tránh tự cài lại, tạo Restore Point.
4. **Ứng dụng đã cài / Rollback**: Liệt kê phần mềm, gỡ bỏ và hỗ trợ quay về phiên bản cũ (rollback) kèm chức năng ghim (pin).
5. **Tùy chỉnh cập nhật**: Cấu hình chính sách Windows Update qua Registry (hoãn cập nhật, khóa phiên bản tính năng, giờ hoạt động), quản lý dịch vụ `wuauserv`, tạo lịch tự động cập nhật phần mềm (Task Scheduler).

Khung nhật ký thời gian thực (Realtime Logs) ở dưới cùng hiển thị thông tin màu sắc theo mức độ (Info, Warning, Error, Success), hỗ trợ Xóa, Sao chép và Mở thư mục chứa log file (`C:\ProgramData\WinUpdateManager\log.txt`).

## 3. Lệnh biên dịch & Kiểm thử

### Chạy Unit Test:
```bash
dotnet test WinUpdateManager.sln
```

### Chạy thử chế độ kiểm tra khởi động tự động:
```bash
dotnet run --project src/WinUpdateManager.App/WinUpdateManager.App.csproj -- --selftest
```

### Đóng gói 1 file `.exe` tự chứa (chạy trên máy Windows):
```powershell
dotnet publish src/WinUpdateManager.App/WinUpdateManager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
