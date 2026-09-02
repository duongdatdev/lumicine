# Kế hoạch Phase 1 — nền tảng LumiCine

## Mục tiêu

Tạo nền tảng backend có thể chạy và kiểm chứng được cho LumiCine, với cấu trúc bốn project .NET 10: `LumiCine.API`, `LumiCine.Application`, `LumiCine.Domain` và `LumiCine.Infrastructure`. Dữ liệu được lưu trong PostgreSQL 18 qua EF Core 10 và provider Npgsql.

## Phạm vi

- API ASP.NET Core dùng Controllers; cấu hình log JSON qua `ILogger`.
- Endpoint `GET /health/live` để xác nhận tiến trình đang chạy.
- Endpoint `GET /health/ready` trả `200` khi dịch vụ sẵn sàng và `503` khi phụ thuộc cần thiết chưa sẵn sàng.
- Lỗi validation request trả HTTP `400` với `ProblemDetails`.
- Lỗi API dùng `ProblemDetails`, có `traceId` để tra log.
- OpenAPI/Swagger chỉ bật ở môi trường Development.
- EF Core/Npgsql và migration cho mô hình dữ liệu Phase 1 theo [thiết kế cơ sở dữ liệu](database-design.md).
- Unit test dùng xUnit; kiểm thử tích hợp cơ sở dữ liệu chạy với PostgreSQL cục bộ chuyên dụng, không dùng EF InMemory.
- Quy ước cấu hình trong `.editorconfig`, bỏ qua output build, file môi trường và cấu hình cục bộ có thể chứa bí mật trong `.gitignore`.

## Ngoài phạm vi

Phase 1 chưa có frontend, xác thực/người dùng, API CRUD nghiệp vụ, đặt chỗ/giữ ghế, xử lý thanh toán, phát hành vé hay logic khóa ghế. Chưa tạo cấu hình chứa thông tin kết nối hoặc bí mật thật trong repository.

## Tiêu chí nghiệm thu

1. Solution có đúng bốn project backend nêu trên, với tham chiếu phụ thuộc theo chiều API → Application/Infrastructure, Infrastructure → Application/Domain và Application → Domain.
2. Ứng dụng khởi động với cấu hình môi trường; không cần bí mật được commit.
3. `GET /health/live` trả trạng thái thành công khi tiến trình hoạt động.
4. `GET /health/ready` trả `200` khi DB sẵn sàng và `503` khi DB chưa sẵn sàng.
5. Request sai trả HTTP `400`; lỗi API trả `ProblemDetails` và cung cấp `traceId`; log có thể ghi JSON qua `ILogger`.
6. Swagger/OpenAPI chỉ được đăng ký hoặc phục vụ trong Development.
7. EF Core 10/Npgsql kết nối PostgreSQL 18; migration khớp ràng buộc và quan hệ trong thiết kế dữ liệu.
8. Bộ test xUnit chạy được; kiểm thử cần DB dùng PostgreSQL chuyên dụng cục bộ, không dùng InMemory.
9. Không có frontend, authentication, CRUD nghiệp vụ, booking engine hay thông tin bí mật trong phần bàn giao Phase 1.
