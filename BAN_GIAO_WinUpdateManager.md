# BÀN GIAO CÔNG VIỆC: WIN UPDATE MANAGER

> Dành cho: Antigravity (AI coding agent)
> Mục tiêu: biến bản nháp PowerShell `WinUpdateManager.ps1` thành một ứng dụng Windows hoàn chỉnh, chạy bằng 1 file `.exe`, dễ dùng với người không biết lập trình.

---

## 1. Bối cảnh

- Người dùng làm IT, quản lý máy tính và máy in cho nhân viên trong công ty. Không có nền tảng lập trình, nên giao diện phải đơn giản, tiếng Việt, mọi thao tác nguy hiểm đều có hộp thoại xác nhận.
- Cần một công cụ duy nhất để:
  1. Cập nhật trực tiếp **WhatsApp** và mọi ứng dụng Windows khác.
  2. Cập nhật **Windows** (quét, cài, ẩn, bỏ ẩn).
  3. **Gỡ bản cập nhật** (Windows KB và ứng dụng) hoặc rollback về phiên bản cũ một cách thuận tiện.
  4. **Tùy chỉnh** việc cập nhật: chế độ, hoãn, khóa phiên bản, giờ hoạt động, lịch tự động.
- File `WinUpdateManager.ps1` đi kèm là **bản nháp tham chiếu** (prototype). Nó chưa được chạy thử trên máy Windows thật. Dùng nó để hiểu chức năng và lệnh, **không giữ nguyên code của nó**.

## 2. Công nghệ yêu cầu

| Hạng mục | Quyết định |
|---|---|
| Ngôn ngữ / framework | C# **.NET 8**, giao diện **WPF** (cùng stack với dự án LanShare đã làm) |
| Kiến trúc | MVVM đơn giản (CommunityToolkit.Mvvm), tách lớp Service khỏi UI |
| Đóng gói | 1 file `.exe` tự chứa: `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` |
| Quyền | Manifest `requireAdministrator` (app luôn chạy quyền admin) |
| Build | GitHub Actions trên `windows-latest`, upload artifact `.exe`; có workflow tag `v*` để tạo Release |
| Ngôn ngữ giao diện | Tiếng Việt (chuẩn bị sẵn file resource để thêm tiếng Anh sau) |
| Không dùng | Thư viện cần cài thêm trên máy người dùng, kết nối internet ngoài `winget`/Windows Update/Store |

Cấu trúc solution đề xuất:

```
WinUpdateManager/
├─ WinUpdateManager.sln
├─ src/WinUpdateManager.App/          # WPF: Views, ViewModels, app.manifest
├─ src/WinUpdateManager.Core/         # Services, Models (không phụ thuộc UI)
│   ├─ Services/ProcessRunner.cs
│   ├─ Services/WingetService.cs
│   ├─ Services/WindowsUpdateService.cs
│   ├─ Services/HotfixService.cs
│   ├─ Services/PolicyService.cs
│   ├─ Services/ScheduleService.cs
│   └─ Services/LogService.cs
├─ tests/WinUpdateManager.Tests/      # xUnit: parser winget, PolicyService (mock registry)
└─ .github/workflows/build.yml
```

## 3. Chức năng chi tiết

### 3.1. Dịch vụ chạy tiến trình (`ProcessRunner`)
- Chạy tiến trình con **bất đồng bộ** (`async/await`), không làm đơ UI, hỗ trợ **nút Hủy** (CancellationToken, kill process tree).
- Đọc stdout/stderr theo từng dòng, đẩy realtime lên khung nhật ký.
- Đặt `StandardOutputEncoding = UTF8`. Lọc dòng nhiễu của winget: spinner (`- \ | /`), thanh tiến trình (`█ ▒`).
- Trả về `ExitCode`, toàn bộ output.
- Chỉ cho **một tác vụ nặng chạy một lúc**; các nút khác bị vô hiệu khi đang bận.

### 3.2. Tab "Cập nhật ứng dụng" (winget)

| Chức năng | Lệnh |
|---|---|
| Quét app có bản mới | `winget upgrade --accept-source-agreements` (+ `--include-unknown` nếu tick) |
| Cập nhật 1 app | `winget upgrade --id "<ID>" --exact --silent --accept-package-agreements --accept-source-agreements` |
| Cập nhật tất cả | `winget upgrade --all --silent --accept-package-agreements --accept-source-agreements` |
| Pin / bỏ pin / xem pin | `winget pin add --id "<ID>" --exact` / `winget pin remove --id "<ID>" --exact` / `winget pin list` |
| **Nút riêng WhatsApp** | `winget upgrade --id 9NKSQGP7F2NH --exact --source msstore ...`; nếu báo không có gói thì thử `WhatsApp.WhatsApp`. Nếu WhatsApp chưa cài, báo rõ và hỏi có muốn cài mới không |

Yêu cầu giao diện:
- Danh sách có checkbox, cột: Tên, ID, Phiên bản hiện tại, Phiên bản mới, Nguồn. Có "Chọn tất cả / Bỏ chọn".
- Sau mỗi lần cập nhật xong, tự quét lại.
- Thanh tiến trình tổng (đang cập nhật 3/10...) và nút Hủy.
- Cho phép "danh sách app ưu tiên" (ví dụ WhatsApp, Chrome, Zalo): một nút để chỉ cập nhật nhóm này. Lưu danh sách vào file cấu hình JSON.

**Phân tích bảng kết quả winget (quan trọng):**
- `winget` không có output JSON cho `upgrade`/`list`. Phải parse bảng văn bản: tìm dòng `-----`, dòng ngay trên là header, lấy vị trí bắt đầu của từng cột từ header rồi cắt từng dòng dữ liệu theo vị trí đó.
- Dòng header/separator có thể dính ký tự spinner phía trước, chỉ lấy phần sau ký tự `\r` cuối cùng.
- Dừng khi gặp dòng trống hoặc dòng tổng kết dạng `N upgrades available.`
- Hỗ trợ header khác ngôn ngữ: nếu không thấy cột `Id`, gán theo thứ tự (Name, Id, Version, [Available], Source).
- Ưu tiên cân nhắc thêm đường dự phòng bằng module `Microsoft.WinGet.Client` hoặc COM API của winget nếu parse không ổn định. Viết unit test cho parser với nhiều mẫu output thật (có Unicode, tên dài, có/không cột Available).
- Mã thoát winget: đối chiếu tài liệu chính thức của winget để hiển thị thông báo tiếng Việt dễ hiểu (ví dụ không có bản cập nhật phù hợp, không tìm thấy gói, cần khởi động lại).

### 3.3. Tab "Windows Update"
Dùng **Windows Update Agent API** (COM `Microsoft.Update.Session`), không phụ thuộc module ngoài.

- Quét: `Search("IsInstalled=0 and IsHidden=0")`. Hiển thị Tiêu đề, KB, Dung lượng, Loại (Phần mềm/Driver).
- Cài: AcceptEula → `UpdateDownloader.Download()` → `UpdateInstaller.Install()`. Hiển thị tiến trình tải/cài theo callback nếu làm được, báo `ResultCode` và `RebootRequired`.
- Ẩn: `IsHidden = true`. Xem danh sách đã ẩn: `Search("IsInstalled=0 and IsHidden=1")`. Bỏ ẩn: `IsHidden = false`.
- Mọi thao tác chạy trên thread nền, UI không đơ.
- Có nút "Khởi động lại ngay" (có xác nhận) khi `RebootRequired`.

### 3.4. Tab "Gỡ bản cập nhật Windows"
- Liệt kê KB đã cài: WMI `Win32_QuickFixEngineering` (HotFixID, Description, InstalledOn, InstalledBy), sắp xếp mới nhất lên đầu.
- Gỡ: `wusa.exe /uninstall /kb:<số> /quiet /norestart`.
  - Mã `0` thành công; `3010` thành công, cần khởi động lại; mã khác thì báo lỗi và gợi ý dùng DISM.
- **Gỡ + chặn cài lại**: gỡ xong, tìm KB đó trong danh sách chờ của Windows Update rồi ẩn đi (dùng 3.3). Nếu chưa thấy trong danh sách chờ, báo rõ và nhắc người dùng quét lại sau khi khởi động lại.
- Mục nâng cao "Gói hệ thống (DISM)": `dism /Online /Get-Packages /Format:Table`, cho phép gỡ gói `Package_for_RollupFix` bằng `dism /Online /Remove-Package /PackageName:<tên>` có cảnh báo rõ ràng.
- **Điểm hồi phục:** trước khi gỡ, đề xuất tạo restore point (`Checkpoint-Computer`), có checkbox bật/tắt, mặc định bật.
- Luôn hiển thị hộp thoại xác nhận liệt kê các KB sắp gỡ.

### 3.5. Tab "Ứng dụng đã cài / Rollback"
- `winget list --accept-source-agreements` → bảng ứng dụng, có ô lọc theo tên/ID.
- Gỡ ứng dụng: `winget uninstall --id "<ID>" --exact --silent`, có xác nhận.
- Xem phiên bản có sẵn: `winget show --id "<ID>" --exact --versions` → hiển thị dạng danh sách chọn được (không chỉ đổ ra log).
- Rollback: `winget install --id "<ID>" --exact --version "<V>" --force --silent --accept-package-agreements --accept-source-agreements`. Nếu lỗi, đề xuất quy trình gỡ ứng dụng rồi cài lại phiên bản cũ. Sau khi cài xong, hỏi có muốn **pin** để không tự lên bản mới.
- Lưu ý hiển thị: app từ Microsoft Store thường **không** chọn được phiên bản cũ; app phải báo rõ giới hạn này.

### 3.6. Tab "Tùy chỉnh cập nhật" (chính sách qua registry)

Đọc giá trị hiện tại khi mở tab, ghi khi bấm "Áp dụng", có nút "Khôi phục mặc định".

| Tùy chọn | Khóa registry (HKLM) | Giá trị |
|---|---|---|
| Chế độ cập nhật | `SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` | Mặc định = xóa `NoAutoUpdate`, `AUOptions`; Thông báo trước khi tải: `NoAutoUpdate=0`, `AUOptions=2`; Tự tải, hỏi trước khi cài: `AUOptions=3`; Tự tải và cài: `AUOptions=4`; Tắt hẳn: `NoAutoUpdate=1` |
| Hoãn bản chất lượng | `...\WindowsUpdate` | `DeferQualityUpdates=1`, `DeferQualityUpdatesPeriodInDays` (0-30) |
| Hoãn bản tính năng | `...\WindowsUpdate` | `DeferFeatureUpdates=1`, `DeferFeatureUpdatesPeriodInDays` (0-365) |
| Khóa phiên bản tính năng | `...\WindowsUpdate` | `TargetReleaseVersion=1`, `ProductVersion` ("Windows 10"/"Windows 11" theo build ≥ 22000), `TargetReleaseVersionInfo` (vd "23H2") |
| Không tự khởi động lại khi có người đăng nhập | `...\WindowsUpdate\AU` | `NoAutoRebootWithLoggedOnUsers=1` |
| Không cài driver qua WU | `...\WindowsUpdate` | `ExcludeWUDriversInQualityUpdate=1` |
| Giờ hoạt động | `...\WindowsUpdate` | `SetActiveHours=1`, `ActiveHoursStart`, `ActiveHoursEnd` (0-23, tối đa 18 giờ) |
| Tắt tự cập nhật app Store | `SOFTWARE\Policies\Microsoft\WindowsStore` | `AutoDownload=2` |

Thêm: nút Tắt/Bật dịch vụ `wuauserv` (Disabled/Manual), kèm cảnh báo "nhớ bật lại".

**Lịch tự động cập nhật ứng dụng:**
- Tạo Scheduled Task tên `WinUpdateManager-AutoUpgradeApps` chạy `winget upgrade --all --silent --accept-package-agreements --accept-source-agreements`, ghi log vào `C:\ProgramData\WinUpdateManager\auto-upgrade.log`.
- Chu kỳ: hằng ngày hoặc hằng tuần (chọn thứ), giờ nhập dạng `HH:mm`.
- Chạy dưới tài khoản người dùng hiện tại, `Interactive`, `Highest`. **Không** dùng SYSTEM vì winget dưới SYSTEM không ổn định với app Store.
- Nút tạo/cập nhật, xóa lịch, xem trạng thái (lần chạy gần nhất, kết quả), xem log.

### 3.7. Nhật ký và cấu hình
- Khung nhật ký ở dưới cùng, tô màu theo mức (thông tin / cảnh báo / lỗi), có nút Xóa, Sao chép, Mở thư mục log.
- Ghi file `C:\ProgramData\WinUpdateManager\log.txt` (xoay vòng khi quá 5 MB, giữ 5 file).
- Cấu hình người dùng (danh sách app ưu tiên, tùy chọn restore point...) lưu `C:\ProgramData\WinUpdateManager\config.json`.

## 4. Giao diện và trải nghiệm
- Cửa sổ có 5 tab như trên, kích thước thay đổi được, ghi nhớ kích thước.
- Trang đầu (Tổng quan, tùy chọn): hiển thị số app có bản mới, số bản Windows chờ cài, lần cập nhật gần nhất, và nút lớn "Cập nhật mọi thứ".
- Nhãn nút rõ ràng, tiếng Việt. Hành động nguy hiểm (gỡ KB, gỡ app, tắt dịch vụ, khôi phục mặc định, khởi động lại) luôn có hộp thoại xác nhận.
- Khi không có winget: hiện thông báo thân thiện và nút hướng dẫn cài/cập nhật "App Installer", các tab khác vẫn dùng được.
- Khi không có mạng: báo lỗi rõ ràng, không treo.

## 5. Bảo mật và an toàn
- Không bao giờ ghép chuỗi lệnh từ dữ liệu người dùng nhập mà không kiểm tra. ID gói, KB, phiên bản phải được **validate** bằng regex (ví dụ ID: `^[A-Za-z0-9._\\{}\-]+$`; KB: `^\d{6,8}$`) và truyền qua `ProcessStartInfo.ArgumentList`, không dùng shell.
- Không thu thập hay gửi dữ liệu ra ngoài. Ứng dụng chỉ gọi `winget`, Windows Update, Task Scheduler và registry.
- Chỉ ghi vào các khóa chính sách đã liệt kê ở mục 3.6; không ghi nơi khác.
- Mọi thay đổi registry phải đọc lại được và khôi phục được bằng nút "Khôi phục mặc định".

## 6. Lộ trình thực hiện (giao theo từng giai đoạn)

**Giai đoạn 1: Khung dự án và ProcessRunner**
- Tạo solution, 2 project, manifest admin, `ProcessRunner` + `LogService`, cửa sổ chính với 5 tab trống và khung nhật ký.
- Workflow GitHub Actions build và publish `.exe` đơn file. Kiểm tra app **khởi động được** trên máy sạch trước khi làm tiếp (lưu ý: ở dự án LanShare trước đây build qua nhưng app không lên, nên cần bắt lỗi khởi động và ghi log lỗi ra file).

**Giai đoạn 2: Ứng dụng (winget)**
- `WingetService` + parser + unit test; tab 1 và tab 4; nút WhatsApp; pin.

**Giai đoạn 3: Windows Update và gỡ KB**
- `WindowsUpdateService`, `HotfixService`; tab 2 và tab 3; restore point; gỡ + ẩn.

**Giai đoạn 4: Tùy chỉnh và lịch**
- `PolicyService`, `ScheduleService`; tab 5; khôi phục mặc định.

**Giai đoạn 5: Hoàn thiện**
- Tab Tổng quan, "Cập nhật mọi thứ", cấu hình JSON, icon, thông tin phiên bản, README, trình cài đặt (tùy chọn: Inno Setup hoặc MSIX).

Sau mỗi giai đoạn: build qua, chạy được, và tự kiểm tra theo mục 7 trước khi báo hoàn thành.

## 7. Tiêu chí nghiệm thu

- [ ] `.exe` đơn file chạy được trên Windows 10 và Windows 11 sạch (không cài .NET), tự xin quyền admin.
- [ ] Quét, cập nhật 1 app, cập nhật tất cả hoạt động; UI không bị đơ; nút Hủy dừng được tiến trình.
- [ ] Nút WhatsApp cập nhật được WhatsApp bản Store; báo rõ nếu chưa cài.
- [ ] Parser winget đúng với output tiếng Anh, tên có Unicode, có/không cột Available (có unit test).
- [ ] Windows Update: quét, cài, ẩn, bỏ ẩn hoạt động; hiển thị đúng khi cần khởi động lại.
- [ ] Gỡ KB: thành công và cần khởi động lại được xử lý đúng; "Gỡ + chặn cài lại" ẩn được KB; có restore point.
- [ ] Rollback app về phiên bản cũ qua winget; hiển thị giới hạn với app Store.
- [ ] Tab Tùy chỉnh đọc đúng giá trị hiện tại, ghi đúng registry, "Khôi phục mặc định" xóa sạch chính sách do tool đặt.
- [ ] Lịch tự động: tạo, chạy, xem log, xóa được.
- [ ] Mọi lỗi hiển thị bằng tiếng Việt dễ hiểu và có trong file log; không crash khi thiếu winget hoặc mất mạng.
- [ ] Không có lệnh nào ghép chuỗi từ dữ liệu chưa kiểm tra.

## 8. Điểm cần lưu ý từ bản nháp PowerShell

- Bản nháp chưa được chạy thử trên Windows thật, có thể còn lỗi nhỏ. Hãy coi các lệnh ở mục 3 là nguồn chuẩn và tự kiểm chứng từng lệnh trên máy thật.
- Ba chỗ khó nhất: parse bảng winget, tiến trình cài của Windows Update API, và việc gỡ Cumulative Update bằng `wusa` (nhiều bản không gỡ được, cần DISM).
- Cần kiểm chứng ID WhatsApp (`9NKSQGP7F2NH` ở nguồn msstore, hoặc `WhatsApp.WhatsApp`) bằng `winget search whatsapp` trên máy thật trước khi cố định trong code.
