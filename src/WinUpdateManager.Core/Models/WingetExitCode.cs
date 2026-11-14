namespace WinUpdateManager.Core.Models;

public static class WingetExitCode
{
    public static string GetDescription(int exitCode)
    {
        return exitCode switch
        {
            0 => "Thành công.",
            -1 => "Tiến trình đã bị người dùng hủy.",
            // Winget specific HRESULT error codes
            unchecked((int)0x8A150011) => "Không tìm thấy bản cập nhật phù hợp cho gói này.",
            unchecked((int)0x8A150005) => "Không tìm thấy gói phần mềm với ID được cung cấp.",
            unchecked((int)0x8A150024) => "Gói phần mềm đã bị ghim phiên bản (Pinned), không thể tự động nâng cấp.",
            unchecked((int)0x8A15002B) => "Cập nhật thành công, cần khởi động lại máy để hoàn tất.",
            unchecked((int)0x8A150014) => "Thao tác đã bị hủy bởi người dùng.",
            unchecked((int)0x8A150013) => "Không đồng ý điều khoản nguồn hoặc gói (Agreements).",
            unchecked((int)0x8A15002A) => "Gói phần mềm phụ thuộc (dependency) bị thiếu hoặc không thể cài đặt.",
            unchecked((int)0x8A150023) => "Không thể gỡ bỏ ghim phiên bản (Pin).",
            // Windows Installer standard error codes
            3010 => "Cập nhật thành công, cần khởi động lại máy tính.",
            1602 => "Người dùng đã hủy tiến trình cài đặt.",
            1603 => "Lỗi nghiêm trọng trong quá trình cài đặt của gói phần mềm.",
            1618 => "Một tiến trình cài đặt khác đang chạy. Vui lòng đợi tiến trình đó kết thúc rồi thử lại.",
            1638 => "Một phiên bản khác của sản phẩm này đã được cài đặt trên máy.",
            _ => $"Tiến trình kết thúc với mã lỗi: {exitCode} (0x{exitCode:X8})."
        };
    }
}
