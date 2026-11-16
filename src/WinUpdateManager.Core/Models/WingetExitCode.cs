namespace WinUpdateManager.Core.Models;

public static class WingetExitCode
{
    public static string GetDescription(int exitCode)
    {
        return exitCode switch
        {
            0 => "Thành công.",
            -1 => "Tiến trình đã bị người dùng hủy.",

            // HTTP / Network error codes (Common when CDNs deprecate old installers like classic Teams)
            unchecked((int)0x80190194) => "Gói cài đặt không còn tồn tại trên máy chủ của nhà phát hành (HTTP 404 - Not Found). Ví dụ: Microsoft Teams cổ điển đã bị khai tử và link tải đã bị gỡ. Vui lòng chuyển sang dùng 'Microsoft Teams (Work or School)'.",
            unchecked((int)0x80190190) => "Yêu cầu tải gói không hợp lệ (HTTP 400). Đối với Microsoft Store, lỗi này thường xảy ra khi gói yêu cầu quyền tài khoản người dùng.",
            unchecked((int)0x80190191) => "Yêu cầu xác thực tài khoản (HTTP 401 - Unauthorized). Cần đăng nhập để tải gói.",
            unchecked((int)0x80190193) => "Bị từ chối truy cập từ máy chủ nhà phát hành (HTTP 403 - Forbidden).",
            unchecked((int)0x801901F4) => "Máy chủ của nhà phát hành gặp sự cố nội bộ (HTTP 500 - Internal Server Error).",

            // Winget specific HRESULT error codes
            unchecked((int)0x8A150076) => "Yêu cầu xác thực tài khoản Microsoft hoặc dịch vụ Windows Update (wuauserv) đang bị chặn bởi công cụ bảo vệ (như WUB). Hãy bấm 'Bật Windows Update' trong phần mềm hoặc nạp file gói .msixbundle offline.",
            unchecked((int)0x8A150011) => "Không tìm thấy bản cập nhật phù hợp cho gói này.",
            unchecked((int)0x8A150005) => "Không tìm thấy gói phần mềm với ID được cung cấp.",
            unchecked((int)0x8A150024) => "Gói phần mềm đã bị ghim phiên bản (Pinned), không thể tự động nâng cấp.",
            unchecked((int)0x8A15002B) => "Cập nhật thành công, cần khởi động lại máy để hoàn tất.",
            unchecked((int)0x8A150014) => "Thao tác đã bị hủy bởi người dùng.",
            unchecked((int)0x8A150013) => "Không đồng ý điều khoản nguồn hoặc gói (Agreements).",
            unchecked((int)0x8A15002A) => "Gói phần mềm phụ thuộc (dependency) bị thiếu hoặc không thể cài đặt.",
            unchecked((int)0x8A150023) => "Không thể gỡ bỏ ghim phiên bản (Pin).",
            unchecked((int)0x8A150044) => "Tải gói cài đặt thất bại. Vui lòng kiểm tra kết nối mạng Internet.",
            unchecked((int)0x8A150056) => "Gói cài đặt Store yêu cầu tài khoản Microsoft để cấp phép tải về.",
            unchecked((int)0x8A150001) => "Lỗi khởi tạo Windows Package Manager.",
            unchecked((int)0x8A150002) => "Nguồn phần mềm (Source) không hợp lệ hoặc không thể kết nối.",
            unchecked((int)0x8A150007) => "Mã băm kiểm tra (SHA256 Hash) của file cài đặt không trùng khớp.",

            // Windows System & Installer error codes
            5 => "Truy cập bị từ chối (Access is denied - Mã lỗi 5). Dịch vụ hoặc khóa Registry đang bị công cụ bảo vệ (WUB) khóa quyền truy cập.",
            87 => "Tham số không hợp lệ (Mã lỗi 87 - ERROR_INVALID_PARAMETER).",
            unchecked((int)0x80070057) => "Tham số không hợp lệ (ERROR_INVALID_PARAMETER - Mã lỗi 87).",
            unchecked((int)0x80070005) => "Truy cập bị từ chối (ERROR_ACCESS_DENIED). Cần chạy với quyền Administrator.",
            unchecked((int)0x80070002) => "Hệ thống không tìm thấy tệp được chỉ định (ERROR_FILE_NOT_FOUND).",
            unchecked((int)0x80240017) => "Bản cập nhật không thể gỡ qua WUSA do là gói cập nhật tích lũy bắt buộc (Cumulative Update).",
            3010 => "Cập nhật thành công, cần khởi động lại máy tính.",
            1602 => "Người dùng đã hủy tiến trình cài đặt.",
            1603 => "Lỗi nghiêm trọng trong quá trình cài đặt của gói phần mềm.",
            1618 => "Một tiến trình cài đặt khác đang chạy. Vui lòng đợi tiến trình đó kết thúc rồi thử lại.",
            1638 => "Một phiên bản khác của sản phẩm này đã được cài đặt trên máy.",
            _ => $"Tiến trình kết thúc với mã lỗi: {exitCode} (0x{exitCode:X8})."
        };
    }
}
