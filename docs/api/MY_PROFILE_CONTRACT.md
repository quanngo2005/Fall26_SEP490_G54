# My Profile contract

Nguồn chuẩn cho BE (`MyProfileController`, `ProfileService`, `AuthService.ChangePasswordAsync`) và FE (màn Employee Detail, xem `EMPLOYEE_CONTRACT.md`). Hồ sơ của chính mình **không** có màn riêng: người dùng mở `/employee-detail/me`, đọc dữ liệu qua `GET /employees/me`, và hai nút "Đổi số điện thoại" / "Đổi mật khẩu" (mỗi nút một popup) chỉ hiện khi đang xem chính mình.
Mọi thay đổi route, mã lỗi hoặc trường phải cập nhật file này. Trạng thái: **implemented** (BE và FE).

## 1. Quy ước

- Route ở gốc host, không prefix, nằm dưới `/me`. Tất cả cần `Authorization: Bearer <accessToken>`.
- Envelope `ApiResponse<T>` cho `/me/profile`. `/me/password` trả `AuthResponseDto` trần (cùng kiểu với `/auth/login`).
- JSON camelCase. Mã lỗi dùng `errorCode` ổn định trong `ErrorCodes`.
- Người dùng chỉ sửa được **số điện thoại** (đúng 10 chữ số, bắt đầu bằng 0). Họ tên, email (cũng là tên đăng nhập), mã nhân viên, phòng ban, chức danh, quản lý là dữ liệu đồng bộ từ HR nên chỉ đọc.

## 2. Routes

| Method | Route | Body | Thành công |
| --- | --- | --- | --- |
| PUT | `/me/profile` | `UpdateMyProfileDto` | 200 `ApiResponse<MyPhoneDto>` |
| POST | `/me/password` | `ChangePasswordRequestDto` | 200 `AuthResponseDto`, `Cache-Control: no-store` |

### 2.1 `MyPhoneDto` (kết quả của `PUT /me/profile`)

```json
{ "phone": "0901000004" }
```

`phone` là `null` khi số đã bị xóa. Không còn `GET /me/profile`: màn hình đọc hồ sơ từ `GET /employees/me` và tải lại sau khi đổi số.

### 2.2 `UpdateMyProfileDto`

```json
{ "phone": "+84 901 000 004" }
```

- `phone: null` hoặc chuỗi rỗng/chỉ khoảng trắng: xóa số điện thoại.
- Có giá trị: cắt khoảng trắng hai đầu rồi phải khớp `^0[0-9]{9}$` (đúng 10 chữ số, bắt đầu bằng 0; cùng quy tắc với `POST/PUT /employees`).
- Chỉ `phone` (và `updatedAt` của nhân viên) bị ghi; `lastSyncAt` không đổi.

### 2.3 `ChangePasswordRequestDto`

```json
{
  "currentPassword": "mật khẩu hiện tại",
  "newPassword": "mật khẩu mới",
  "refreshToken": "refresh token của phiên đang dùng"
}
```

`refreshToken` bắt buộc: dùng để xác định phiên hiện tại sẽ được giữ lại (thời hạn còn lại và cờ "ghi nhớ đăng nhập" được giữ nguyên).

**Chính sách mật khẩu mới** (kiểm tra ở BE, FE kiểm tra lại cho tiện dùng):

| Quy tắc | Giá trị |
| --- | --- |
| Độ dài | 12 đến 128 ký tự |
| Thành phần | Có ít nhất một chữ cái và một chữ số |
| Khác mật khẩu hiện tại | Bắt buộc |
| Không chứa phần trước `@` của email | Bắt buộc (không phân biệt hoa thường; chỉ xét khi phần đó dài từ 3 ký tự) |

**Hành vi khi thành công:**
1. Lưu hash mới, đặt lại bộ đếm đăng nhập sai.
2. Thu hồi **mọi** refresh token (phiên) của tài khoản, kể cả phiên đang dùng.
3. Cấp cặp token mới cho phiên hiện tại (`AuthResponseDto`) và lưu vào session store.
4. Ghi audit `PASSWORD_CHANGED`.

Giới hạn đã biết:
- Access token của các phiên khác vẫn dùng được đến khi hết hạn (tối đa 15 phút); hệ thống chưa thu hồi hàng loạt access token.
- Nếu session store (Redis) lỗi sau khi đã lưu hash mới thì mật khẩu đã đổi nhưng phiên cũ chưa bị thu hồi; API trả lỗi 500 và người dùng nên đăng nhập lại.

## 3. Lỗi

| Tình huống | HTTP | errorCode |
| --- | --- | --- |
| Không có/sai access token | 401 | `UNAUTHORIZED` |
| Tài khoản hoặc nhân viên không còn hoạt động (`PUT /me/profile`) | 403 | `FORBIDDEN` |
| Không tìm thấy tài khoản (`PUT /me/profile`) | 404 | `NOT_FOUND` |
| Số điện thoại sai định dạng | 400 | `VALIDATION_ERROR` (+ `errors[]`) |
| Mật khẩu mới vi phạm chính sách; thiếu trường; quá 128 ký tự | 400 | `VALIDATION_ERROR` (+ `errors[]`) |
| Mật khẩu hiện tại sai | 400 | `INVALID_CURRENT_PASSWORD` |
| Tài khoản đang bị khóa (kể cả bị khóa do nhập sai mật khẩu hiện tại quá ngưỡng) | 403 | `FORBIDDEN` |
| `refreshToken` không hợp lệ, hết hạn, hoặc thuộc tài khoản khác | 401 | `UNAUTHORIZED` |

**Mật khẩu hiện tại sai trả 400, không phải 401.** FE có interceptor coi 401 là phiên hết hạn và tự refresh/đăng xuất; một lỗi nhập sai không được kích hoạt điều đó.

Nhập sai mật khẩu hiện tại được tính vào cùng bộ đếm với đăng nhập (`Authentication:MaxFailedAttempts`, mặc định 5; khóa `Authentication:LockoutMinutes`, mặc định 15 phút). Lần sai chạm ngưỡng trả 403 thay vì 400. Mỗi lần sai ghi audit `PASSWORD_CHANGE_FAILED`.

## 4. Ví dụ

`POST /me/password` thành công: `200`

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "eyJ...",
  "expiresAt": "2026-10-22T10:15:00+00:00",
  "refreshTokenExpiresAt": "2026-10-22T22:00:00+00:00",
  "user": { "id": "guid", "email": "chau.le@progest.local", "fullName": "Lê Minh Châu", "roles": ["EMPLOYEE"], "permissions": [] }
}
```

Mật khẩu hiện tại sai: `400`

```json
{ "success": false, "errorCode": "INVALID_CURRENT_PASSWORD", "message": "Mật khẩu hiện tại không đúng.", "status": 400 }
```
