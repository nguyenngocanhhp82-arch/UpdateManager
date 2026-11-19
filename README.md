# Lilbow Manager Update

**Lilbow Manager Update** (trước đây là Win Update Manager) là công cụ quản trị tập trung bản cập nhật Windows và ứng dụng hệ thống, được phát triển bởi tác giả **Lilbow Nguyễn**. Ứng dụng được thiết kế chuyên biệt cho kỹ thuật viên IT quản lý máy tính văn phòng, được đóng gói thành **1 file `.exe` duy nhất**, tự chứa toàn bộ runtime .NET 8 (Self-Contained Single-File), chạy trực tiếp trên Windows 10 và Windows 11 sạch không cần cài đặt thêm bất kỳ phần mềm hay thư viện phụ trợ nào.

---

## 🌟 Tính Năng Nổi Bật

### ⚡ 1. Cập Nhật Ứng Dụng & Giải Pháp Siêu Tốc Cho WhatsApp
- **Nút Cập Nhật Siêu Tốc WhatsApp (3–5 giây):** 
  - Khắc phục triệt để tình trạng máy tính nhân viên bị tắt Windows Update (bằng phần mềm Windows Update Blocker - WUB) dẫn đến việc cập nhật WhatsApp từ Microsoft Store bị treo hoặc thất bại.
  - Tự động kiểm tra file gói cài đặt (`.msixbundle` / `.Appx`) trong thư mục Cache (`C:\ProgramData\WinUpdateManager\Cache`) hoặc đường dẫn chia sẻ mạng nội bộ LAN.
  - Cài đặt trực tiếp qua lệnh native `Add-AppxPackage`, bỏ qua hàng chờ của Store.
  - Nếu chưa có gói cài sẵn, ứng dụng sẽ tạm thời mở khóa dịch vụ `wuauserv`, tự động tải gói mới nhất về và ngay lập tức khóa lại `wuauserv` để đảm bảo máy trạm vẫn luôn chặn Windows Update.
- **Nút "Cài từ tệp Offline":** Cho phép kỹ thuật viên IT cắm USB và chọn trực tiếp file installer của WhatsApp để cập nhật tức thì cho nhân viên.
- **Nút "Tải gói cài về Cache/LAN":** Tải 1 lần duy nhất để tái sử dụng cho toàn bộ máy tính trong văn phòng mà không tiêu tốn băng thông internet.
- **Quản lý nâng cấp toàn bộ ứng dụng qua `winget`:**
  - Bảng danh sách ứng dụng trực quan có checkbox chọn từng app, cột: Tên, ID, Bản hiện tại, Bản mới, Nguồn.
  - Bộ lọc tìm kiếm nhanh theo tên hoặc ID gói.
  - Hỗ trợ cờ `--include-unknown` để tìm các bản cập nhật chưa xác định phiên bản.
  - Nút "Cập nhật đã chọn", "Cập nhật tất cả" và "Nhóm ưu tiên" (chỉ cập nhật WhatsApp, Google Chrome, Zalo theo danh sách tùy chỉnh trong `config.json`).
  - Hỗ trợ Ghim (Pin), Bỏ ghim và Xem danh sách ứng dụng đã ghim để chặn cập nhật ngoài ý muốn.
- **Nút "🚀 Cập nhật mọi thứ (1-Click)":** Thực hiện chu trình nâng cấp trọn gói từ WhatsApp -> Toàn bộ ứng dụng winget -> Quét kiểm tra bản vá Windows Update.

---

### 🛡️ 2. Windows Update (WUA COM API)
- Sử dụng trực tiếp API COM của hệ điều hành (`Microsoft.Update.Session`), hoàn toàn không phụ thuộc module bên ngoài:
  - **Quét bản vá:** Liệt kê đầy đủ Tiêu đề, Mã KB, Dung lượng (MB) và Phân loại (Phần mềm / Driver).
  - **Cài đặt:** Tự động chấp nhận điều khoản EULA, tải về và cài đặt bất đồng bộ trên luồng nền (không gây đơ giao diện).
  - **Ẩn / Bỏ ẩn bản cập nhật:** Cho phép ẩn các bản vá lỗi gây xung đột phần mềm hoặc máy in văn phòng (`Search("IsInstalled=0 and IsHidden=1")`).
  - **Cảnh báo khởi động lại:** Hiển thị thanh cảnh báo màu đỏ và nút "Khởi động lại ngay" có hộp thoại xác nhận khi hệ thống yêu cầu reboot (`RebootRequired`).

---

### 🗑️ 3. Gỡ Bản Cập Nhật Windows (Hotfix & DISM Rollup)
- **Liệt kê danh sách bản vá đã cài đặt:** Truy vấn WMI `Win32_QuickFixEngineering`, sắp xếp các bản KB mới nhất lên đầu kèm ngày cài và người cài đặt.
- **Gỡ bỏ an toàn qua `wusa.exe`:**
  - Tự động kiểm tra mã thoát (`0` thành công; `3010` thành công cần reboot; mã khác cảnh báo rõ ràng).
  - **Tùy chọn tạo điểm khôi phục hệ thống (System Restore Point):** Gọi `Checkpoint-Computer` trước khi gỡ để phòng ngừa rủi ro.
  - **Cơ chế "Gỡ + Chặn cài lại":** Sau khi gỡ KB thành công, ứng dụng tự động tìm kiếm bản vá đó trên dịch vụ Windows Update và đặt cờ `IsHidden = true` để Windows không tự động tải lại bản vá lỗi đó.
- **Gỡ gói hệ thống nâng cao (DISM):**
  - Chuyển đổi sang chế độ xem gói DISM (`dism /Online /Get-Packages /Format:Table`).
  - Cho phép gỡ bỏ các gói `Package_for_RollupFix` cứng đầu không thể gỡ qua `wusa`, đi kèm hộp thoại xác nhận màu đỏ cảnh báo rủi ro cao.

---

### ⏪ 4. Ứng Dụng Đã Cài / Rollback Phiên Bản Cũ
- Liệt kê toàn bộ phần mềm đã cài trên máy trạm qua `winget list`.
- **Gỡ cài đặt phần mềm:** Gỡ âm thầm (`--silent`) với hộp thoại xác nhận tên phần mềm.
- **Xem lịch sử phiên bản (`winget show --versions`):** Hiển thị danh sách các phiên bản cũ có sẵn trong kho winget để người dùng lựa chọn trực tiếp bằng chuột.
- **Rollback về phiên bản cũ:** Ép cài đặt phiên bản được chỉ định (`winget install --version <v> --force`). Tự động gợi ý ghim (Pin) phiên bản sau khi rollback để tránh bị nâng cấp lại.
- Tự động nhận diện và cảnh báo người dùng đối với các ứng dụng Microsoft Store (MS Store không hỗ trợ cài lại bản cũ).

---

### ⚙️ 5. Tùy Chỉnh Chính Sách & Đặt Lịch Tự Động
- **Cấu hình chính sách Windows Update qua Registry (`HKLM`):**
  - Chế độ cập nhật AU: Mặc định, Thông báo trước khi tải (`AUOptions=2`), Tự tải hỏi trước khi cài (`AUOptions=3`), Tự tải & cài (`AUOptions=4`), Tắt hẳn cập nhật (`NoAutoUpdate=1`).
  - Hoãn cập nhật chất lượng (0–30 ngày) và hoãn cập nhật tính năng (0–365 ngày).
  - Khóa phiên bản tính năng hệ điều hành (`TargetReleaseVersion`, `ProductVersion` Windows 10/11, ví dụ khóa ở bản `23H2`).
  - Chặn tự động khởi động lại khi có nhân viên đang đăng nhập (`NoAutoRebootWithLoggedOnUsers`).
  - Chặn Windows Update tự động cài đè driver phần cứng (`ExcludeWUDriversInQualityUpdate`).
  - Thiết lập Giờ hoạt động (Active Hours) để tránh cập nhật trong giờ hành chính.
  - Chặn tự động cập nhật app nền từ Microsoft Store (`SOFTWARE\Policies\Microsoft\WindowsStore\AutoDownload=2`).
  - **Nút "Khôi phục mặc định":** Xóa sạch toàn bộ các key chính sách đã thiết lập về trạng thái ban đầu của Windows.
- **Công tắc khóa/mở dịch vụ Windows Update (`wuauserv`):** 
  - Tắt hẳn hoặc chuyển về Manual, có kiểm tra trạng thái thời gian thực.
- **Lịch tự động nâng cấp ứng dụng (Task Scheduler):**
  - Tạo tác vụ tự động `WinUpdateManager-AutoUpgradeApps` chạy ngầm bằng Windows Task Scheduler.
  - Tùy chọn tần suất: Hằng ngày (Daily) hoặc Hằng tuần (Weekly - chọn thứ trong tuần), giờ chạy định dạng `HH:mm`.
  - Chạy dưới quyền tài khoản hiện tại với đặc quyền cao nhất (`/RL HIGHEST`).
  - Tích hợp nút "Chạy ngay tác vụ", "Xem nhật ký tự động" và "Xóa lịch tự động".

---

### 📜 6. Nhật Ký Thời Gian Thực & Cơ Chế Chạy Tiến Trình An Toàn
- **Khung nhật ký màu:** Phân loại rõ ràng Thông tin (Xanh lam), Cảnh báo (Cam), Lỗi (Đỏ), Thành công (Xanh lá).
- **Hỗ trợ thao tác:** Nút Xóa nhật ký, Sao chép toàn bộ vào Clipboard và Mở thư mục chứa file log.
- **Tự động xoay vòng log file:** Ghi vào file `C:\ProgramData\WinUpdateManager\log.txt`, tự động xoay vòng (rolling log) khi file vượt quá 5 MB, lưu tối đa 5 file backup (`log.txt.1`, `log.txt.2`,...).
- **Chạy tiến trình bất đồng bộ (`ProcessRunner`):**
  - Không gây đơ UI; khóa tương tranh `SemaphoreSlim(1,1)` chỉ cho phép 1 tác vụ nặng chạy tại 1 thời điểm.
  - Hỗ trợ nút **Hủy** tiến trình (`CancelCurrent`) sử dụng tree-kill (`process.Kill(entireProcessTree: true)`).
  - Lọc bỏ triệt để các ký tự rác từ winget: ANSI escape codes, spinner (`- \ | /`), ký tự thanh tiến trình (`█ ▒`).
  - Bảo mật dòng lệnh: 100% tham số (KB number, Package ID, Version) được kiểm tra Regex nghiêm ngặt và truyền qua `ProcessStartInfo.ArgumentList`, triệt tiêu hoàn toàn nguy cơ Command Injection.

---

## 💻 Các Tham Số Dòng Lệnh (CLI Options)

Ứng dụng hỗ trợ các tham số dòng lệnh phục vụ tự động hóa và kiểm tra hệ thống:

```powershell
# Chạy giao diện đồ họa thông thường (WPF GUI)
WinUpdateManager.exe

# Chế độ tự kiểm tra khởi động không cần màn hình (Smoke Test cho CI/CD)
WinUpdateManager.exe --selftest

# Chế độ chạy ngầm nâng cấp tự động (Dành cho Scheduled Task)
# Chế độ này sẽ tự động cập nhật WhatsApp (bỏ qua WUB), nâng cấp các ứng dụng winget
# và khóa lại dịch vụ wuauserv ngay sau khi hoàn tất.
WinUpdateManager.exe --auto-upgrade
```

---

## 🏗️ Cấu Trúc Mã Nguồn

```
WinUpdateManager/
├─ WinUpdateManager.sln
├─ Directory.Build.props
├─ BAN_GIAO_WinUpdateManager.md
├─ README.md
├─ .github/workflows/
│   └─ build.yml                      # GitHub Actions: Windows-latest build, test, smoke test, release
├─ src/
│   ├─ WinUpdateManager.App/          # Dự án giao diện WPF .NET 8
│   │   ├─ Assets/                    # app.ico đa kích thước (256, 64, 48, 32, 16)
│   │   ├─ Converters/                # ValueConverters (LogLevel, Boolean, Visibility)
│   │   ├─ ViewModels/                # MainWindowViewModel (MVVM CommunityToolkit)
│   │   ├─ App.xaml / App.xaml.cs     # Styles & Brushes
│   │   ├─ MainWindow.xaml / .cs      # Giao diện chính 5 Tab
│   │   ├─ Program.cs                 # Xử lý crash toàn cục, log khởi động, cờ CLI
│   │   └─ app.manifest               # requireAdministrator
│   │
│   └─ WinUpdateManager.Core/         # Dự án thư viện nghiệp vụ (không phụ thuộc UI)
│       ├─ Models/                    # LogEntry, WingetPackage, UpdatePolicyModel,...
│       └─ Services/
│           ├─ ProcessRunner.cs       # Quản lý tiến trình async, cancellation, UTF-8
│           ├─ LogService.cs          # Nhật ký in-memory và rolling log file 5MB
│           ├─ ConfigService.cs       # Cấu hình app (priority apps, cache path)
│           ├─ WingetParser.cs        # Parser bảng winget (upgrade, list, pin, versions)
│           ├─ WingetService.cs       # Nghiệp vụ winget & cập nhật siêu tốc WhatsApp
│           ├─ WindowsUpdateService.cs# Windows Update Agent COM API
│           ├─ HotfixService.cs       # WMI QuickFix, wusa.exe, DISM packages
│           ├─ PolicyService.cs       # Ghi/đọc Registry policies, quản lý wuauserv
│           └─ ScheduleService.cs     # Quản lý Windows Task Scheduler (schtasks.exe)
│
└─ tests/
    └─ WinUpdateManager.Tests/        # Bộ kiểm thử đơn vị xUnit (48 tests)
        ├─ XamlValidationTests.cs     # Ngăn ngừa lỗi biên dịch WPF MC3089
        ├─ ProcessRunnerTests.cs      # Kiểm tra clean line, argument list, UTF-8
        ├─ WingetParserTests.cs       # Kiểm tra parser bảng winget với nhiều mẫu output
        ├─ LogServiceTests.cs         # Kiểm tra xoay vòng file log và in-memory buffer
        ├─ HotfixServiceTests.cs      # Kiểm tra parse bảng DISM
        ├─ ScheduleServiceTests.cs    # Kiểm tra parse schtasks tiếng Anh/tiếng Việt
        └─ PolicyModelTests.cs        # Kiểm tra giá trị mặc định của chính sách
```

---

## 🛠️ Hướng Dẫn Biên Dịch & Đóng Gói

### 1. Yêu Cầu Môi Trường
- .NET 8.0 SDK (hoặc mới hơn)
- Hệ điều hành: Windows 10/11 hoặc máy phát triển Linux/macOS có cài .NET 8 SDK

### 2. Chạy Kiểm Thử Đơn Vị (Unit Tests)
```bash
dotnet test WinUpdateManager.sln
```

### 3. Đóng Gói Thành 1 File `.exe` Duy Nhất (Single-File Self-Contained)
Chạy lệnh sau trên máy Windows hoặc máy CI/CD:
```powershell
dotnet publish src/WinUpdateManager.App/WinUpdateManager.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false `
  -o publish
```
File thực thi cuối cùng sẽ nằm tại `publish/WinUpdateManager.App.exe` (hoặc đổi tên thành `WinUpdateManager.exe`). Khi chạy trên bất kỳ máy tính Windows 10/11 nào, file này sẽ tự động giải nén và hoạt động trơn tru mà không cần cài thêm .NET Runtime.

---

## 🔒 Bản Quyền & Người Phát Triển
- **Ứng dụng:** Lilbow Manager Update
- **Tác giả / Người phát triển:** **Lilbow Nguyễn**
- **Giấy phép:** Mã nguồn mở theo giấy phép MIT. Phục vụ nhu cầu quản trị máy trạm hệ thống và tự động hóa IT chuyên nghiệp.
