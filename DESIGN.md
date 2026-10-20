# DESIGN.md — Hệ thống Thiết kế Giao diện Quản trị & Đo lường Hiệu suất Ngân hàng

**Phong cách chủ đạo:** Modern FinTech Modular (Bento Grid + High-Density Tabular)
**Tham chiếu tiêu chuẩn:** Stripe Dashboard, Ant Design Financial, IBM Carbon
**Mục tiêu cốt lõi:** Giảm tải nhận thức (Cognitive Load), tối ưu tốc độ xử lý hồ sơ (TAT/SLA), đảm bảo tính minh bạch kiểm toán (Maker-Checker-Approver & Audit Trail).

> Tài liệu này là **Single Source of Truth** cho UI/UX Designer, Frontend Developer và là ngữ cảnh (Context/System Prompt) cho các công cụ AI lập trình (Cursor, Copilot, v0).

---

## 1. Nguyên tắc Thiết kế Cốt lõi (Core Principles)

1. **Zero-Page-Reload (Không tải lại trang):** Mọi thao tác xem chi tiết, duyệt lệnh, kiểm tra lịch sử đều diễn ra trên cùng một mặt phẳng thông qua cơ chế **Split-View** (Bảng danh sách bên trái + Ngăn kéo chi tiết trượt ra bên phải).
2. **Data-First & Tabular Alignment (Ưu tiên dữ liệu & Căn thẳng hàng tuyệt đối):** Mọi con số tài chính, tỷ lệ KPI, bộ đếm SLA và dấu thời gian bắt buộc sử dụng `font-variant-numeric: tabular-nums`.
3. **Semantic Color Only (Màu sắc mang tính ngữ nghĩa):** Không dùng màu để trang trí. 90% giao diện là màu trung tính; màu nổi bật chỉ dùng để báo hiệu trạng thái SLA, kết quả KPI hoặc nút hành động chính.
4. **Multi-Density Adaptability (Đa mật độ hiển thị):** Bắt buộc hỗ trợ chuyển đổi giữa **Comfort (`44px/dòng`)** cho cấp Quản lý và **Compact (`32px/dòng`)** cho Chuyên viên vận hành.
5. **Instant Feedback (< 100ms):** Không dùng animation kéo dài quá `150ms`; tôn trọng `prefers-reduced-motion` (tắt trượt/fade, giữ nội dung). Ưu tiên thao tác bằng bàn phím (Keyboard-first navigation).
6. **Reversible by Default (Có đường lui):** Mọi thao tác duyệt/chuyển bước tự động đều phải có cách hoàn tác hoặc xác nhận, vì hệ thống phục vụ kiểm toán.

---

## 2. Hệ thống Màu sắc (Color Tokens)

Độ tương phản tối thiểu đạt chuẩn **WCAG 2.1 AA (4.5:1)** cho chữ thường và **3:1** cho thành phần đồ họa/viền.

### 2.1. Nền & Bề mặt (Surfaces & Borders)

| Token | Mã HEX | Quy tắc áp dụng |
| :--- | :--- | :--- |
| `--bg-canvas` | `#F4F6F8` | Nền tổng thể ứng dụng, giảm mỏi mắt khi làm việc 8h/ngày. |
| `--bg-surface` | `#FFFFFF` | Nền Bento Card, Data Grid, Drawer, Modal. |
| `--bg-subtle` | `#F8FAFC` | Nền Table Header, thanh công cụ lọc, dòng dữ liệu khi Hover. |
| `--bg-selected` | `#EFF6FF` | Nền dòng đang được chọn (Active Row) trong Work List. |
| `--border-default` | `#E2E8F0` | Viền trang trí (`1px solid`) cho Card, chia ô bảng, phân cách vùng. Không dùng cho thành phần cần nhận diện (đạt 1,23:1, ngoại lệ có chủ đích). |
| `--border-strong` | `#8494A9` | Viền Input/Select/Checkbox (đạt 3,09:1 trên trắng, WCAG 1.4.11). |
| `--border-hover` | `#CBD5E1` | Viền Card khi Hover (chỉ là phản hồi phụ, không mang thông tin). |
| `--border-focus` | `#2563EB` | Viền khi Focus bằng chuột hoặc `Tab` (kèm `outline: 2px solid #BFDBFE`). |

### 2.2. Văn bản & Số liệu (Typography Colors)

| Token | Mã HEX | Quy tắc áp dụng |
| :--- | :--- | :--- |
| `--text-primary` | `#0F172A` | Số liệu KPI chính, tên khách hàng, tiêu đề trang, giá trị tiền tệ. |
| `--text-secondary` | `#475569` | Label, tiêu đề cột bảng, mô tả nghiệp vụ. |
| `--text-muted` | `#5B6B80` | Timestamp phụ, mã hệ thống ẩn, Placeholder (đạt 5,0:1 trên canvas). Chữ Disabled được miễn trừ tương phản, dùng `#94A3B8` kèm `cursor: not-allowed`. |

### 2.3. Màu Thương hiệu & Ngữ nghĩa Nghiệp vụ (Semantic Status)

Mỗi trạng thái luôn dùng bộ ba **Màu chữ đậm + Nền nhạt + Viền mảnh**:

| Trạng thái (SLA / KPI / Phê duyệt) | Text / Icon | Background | Border |
| :--- | :--- | :--- | :--- |
| **Primary / Hành động chính, dòng chọn, focus** | `#2563EB` | `#EFF6FF` | `#BFDBFE` |
| **Info / Đang xử lý (In Progress / Maker → Checker)** | `#0E7490` | `#ECFEFF` | `#A5F3FC` |
| **Success / Đạt KPI / Đã duyệt / SLA an toàn** | `#047857` | `#ECFDF5` | `#A7F3D0` |
| **Warning / Sắp trễ SLA (< 20% thời gian)** | `#B45309` | `#FFFBEB` | `#FDE68A` |
| **Pending / Chờ duyệt** | `#475569` | `#F1F5F9` | `#CBD5E1` (kèm icon đồng hồ để phân biệt Neutral) |
| **Danger / Vi phạm SLA / Không đạt KPI / Trả lại (Rejected)** | `#B91C1C` | `#FEF2F2` | `#FECACA` |
| **Neutral / Nháp (Draft) / Đóng băng / Hủy** | `#475569` | `#F1F5F9` | `#E2E8F0` |

Tương phản chữ/nền đã kiểm: Info ≈ 5,0:1; Success 5,21:1; Warning 4,84:1; Danger 5,91:1; Primary 4,75:1; Neutral 6,92:1. Màu Primary chỉ dành cho thứ bấm được hoặc đang được chọn; trạng thái xử lý dùng Info để không nhầm với nút.

```css
:root {
  /* Surfaces & Borders */
  --bg-canvas: #F4F6F8;
  --bg-surface: #FFFFFF;
  --bg-subtle: #F8FAFC;
  --bg-selected: #EFF6FF;
  --border-default: #E2E8F0;
  --border-strong: #8494A9;
  --border-hover: #CBD5E1;
  --border-focus: #2563EB;

  /* Text */
  --text-primary: #0F172A;
  --text-secondary: #475569;
  --text-muted: #5B6B80;
  --text-disabled: #94A3B8;

  /* Semantic */
  --primary-fg: #2563EB;  --primary-bg: #EFF6FF;  --primary-border: #BFDBFE;
  --info-fg:    #0E7490;  --info-bg:    #ECFEFF;  --info-border:    #A5F3FC;
  --success-fg: #047857;  --success-bg: #ECFDF5;  --success-border: #A7F3D0;
  --warning-fg: #B45309;  --warning-bg: #FFFBEB;  --warning-border: #FDE68A;
  --danger-fg:  #B91C1C;  --danger-bg:  #FEF2F2;  --danger-border:  #FECACA;
  --neutral-fg: #475569;  --neutral-bg: #F1F5F9;  --neutral-border: #E2E8F0;

  /* Spacing, density, motion, layers */
  --space-1: 4px; --space-2: 8px; --space-3: 12px; --space-4: 16px; --space-6: 24px;
  --row-compact: 32px; --row-comfort: 44px;
  --motion-fast: 100ms; --motion-base: 150ms;
  --z-header: 100; --z-drawer: 200; --z-modal: 300; --z-toast: 400;
}

@media (prefers-reduced-motion: reduce) {
  * { transition-duration: 0ms !important; animation-duration: 0ms !important; }
}
```

---

## 3. Hệ thống Chữ & Số liệu (Typography & Numerals)

### 3.1. Font Family

- **Font giao diện chính (`--font-sans`):** `'Inter', -apple-system, BlinkMacSystemFont, sans-serif` (Inter hỗ trợ đầy đủ dấu tiếng Việt).
- **Font mã định danh & số liệu kỹ thuật (`--font-mono`):** `'JetBrains Mono', 'Roboto Mono', monospace` — bắt buộc cho Mã hồ sơ `#TD-...`, Mã CIF, Mã giao dịch Core Banking, địa chỉ IP trong Audit Log.

### 3.2. Quy tắc Định dạng Số (Mandatory Numeric Rules)

Mọi thành phần hiển thị số tiền, % KPI, thời gian xử lý (TAT), đồng hồ đếm ngược SLA phải khai báo:

```css
.num {
  font-variant-numeric: tabular-nums;
  letter-spacing: -0.01em;
}
```

- Đơn vị tiền tệ và ký hiệu phần trăm đặt cố định vị trí, dùng màu `--text-secondary` để không lấn át con số chính.
- **Tiền VND:** dấu chấm phân cách hàng nghìn, không số lẻ (`1.250.000.000 ₫`); trong bảng hẹp rút gọn `tr`/`tỷ` kèm Tooltip giá trị đầy đủ. Ngày giờ: `DD/MM/YYYY HH:mm:ss`.

### 3.3. Thang kích thước chữ (Type Scale)

| Token / Class | Size / Line-height | Weight | Ứng dụng bắt buộc |
| :--- | :--- | :--- | :--- |
| `text-display-kpi` | `28px` / `36px` | `700` (Bold) | Con số tổng quan trên thẻ Bento KPI (Dashboard Quản lý, 120px). Thẻ Work List 88px dùng `24px` / `32px`. |
| `text-h1-page` | `20px` / `28px` | `600` (SemiBold) | Tiêu đề chính của màn hình. |
| `text-h2-card` | `15px` / `22px` | `600` (SemiBold) | Tiêu đề Bento Card và tiêu đề Drawer. |
| `text-body-table` | `13px` / `20px` | `400` / `500` | Dữ liệu bảng công việc, nội dung form, ghi chú. |
| `text-caption-th` | `12px` / `18px` | `600` (SemiBold) | Tiêu đề cột (`UPPERCASE`, `letter-spacing: 0.04em`), nhãn SLA, Timestamp. Line-height 18px để không cắt dấu tiếng Việt chồng (Ệ, Ằ). |

---

## 4. Lưới, Khoảng cách & Độ nổi (Layout, Spacing & Elevation)

### 4.1. Cấu trúc Khung màn hình (App Shell)

- **Độ phân giải mục tiêu:** `1366x768px` (Laptop/PC quầy giao dịch) đến `1920x1080px+` (Trung tâm Vận hành/Hội sở).
- **Left Navigation Sidebar:** `240px` khi mở, `64px` khi thu gọn. Khi mở Drawer chi tiết, Sidebar tự thu gọn về `64px` để nhường diện tích cho bảng.
- **Top Global Header:** Cao cố định `56px`, chứa thanh tìm kiếm lệnh (`Ctrl + K`), trạng thái ca làm việc (Online/Tạm nghỉ) và thông báo SLA khẩn.
- **Bento Grid System:** 12 cột (`display: grid; grid-template-columns: repeat(12, minmax(0, 1fr));`), `gap: 16px`, lề khung chính `padding: 16px 24px`.

### 4.2. Bo góc (Border Radius) & Đổ bóng (Shadows)

| Token | Giá trị | Áp dụng |
| :--- | :--- | :--- |
| `--radius-card` | `8px` | Bento Card, Data Table container, Modal. |
| `--radius-control` | `6px` | Button, Input, Select, Segmented Tab. |
| `--radius-badge` | `4px` | Nhãn SLA, KPI, Tag nghiệp vụ (chữ nhật bo nhẹ, tiết kiệm diện tích ngang). |
| `--shadow-card` | `none` | Card tách bằng viền `1px solid var(--border-default)`; không đổ bóng cho thành phần đứng yên. |
| `--shadow-drawer` | `-8px 0 24px -4px rgba(15, 23, 42, 0.12)` | Tách lớp rõ rệt khi Drawer trượt đè lên bảng. |

---

## 5. Quy chuẩn Thành phần Giao diện (Component Standards)

### 5.1. Thẻ Bento KPI (Modular KPI Card)

- **Kích thước:** Cao cố định `88px` (Work List chuyên viên) hoặc `120px` (Dashboard Quản lý).
- **Bố cục bên trong:**
  - Góc trên trái: Tên chỉ số (`12px`–`13px`, màu `#475569`).
  - Góc dưới trái: Con số chính (`24px` ở thẻ 88px, `28px` ở thẻ 120px, Bold) + nhãn biến động (`+3.2%` hoặc số hồ sơ khẩn).
  - Góc phải: Sparkline tối giản hoặc nút lọc nhanh.
- **Tương tác:** Mọi thẻ KPI trên Work List đều Clickable để lọc trực tiếp danh sách bên dưới; khi đang kích hoạt lọc, thẻ có viền `2px solid var(--border-focus)`.
- **Ngăn xếp thu gọn:** Trên màn 1366x768, thẻ KPI có nút thu gọn thành một dải 32px để bảng được thêm dòng (xem mục 7).

### 5.2. Bảng Dữ liệu Công việc (High-Density Data Grid)

- **Chiều cao dòng:**
  - `Comfort` (Quản lý): `44px`.
  - `Compact` (Chuyên viên vận hành — mặc định): `32px`.
- **Căn lề cột:**
  - **Trái:** Mã hồ sơ, Tên khách hàng, Loại nghiệp vụ, Người phụ trách, Bước phê duyệt.
  - **Phải:** Hạn mức/Giá trị giao dịch, Điểm KPI, Thời gian xử lý (TAT), Đếm ngược SLA.
  - **Giữa:** Checkbox chọn dòng, Icon cờ ưu tiên (VIP), Nút thao tác nhanh.
- **Ghim cột (Sticky):** `Checkbox + Mã hồ sơ` ghim trái (`position: sticky; left: 0;`); `SLA / Thao tác` ghim phải khi cuộn ngang.
- **Dòng đang chọn (Active Row):** Khi mở hồ sơ trong Drawer, dòng tương ứng giữ nền `--bg-selected` (`#EFF6FF`) và `border-left: 3px solid var(--border-focus)`.
- **Khi Drawer mở:** vùng bảng còn khoảng 742px (1366 − 64 − 560). Thứ tự ẩn cột: Loại nghiệp vụ → Người phụ trách → TAT → Bước phê duyệt. Mã hồ sơ, Khách hàng, Giá trị, SLA luôn hiển thị.
- **Vùng bấm:** checkbox, icon cờ và nút thao tác nhanh có vùng bấm tối thiểu `24×24px` dù dòng chỉ cao 32px.
- **Bàn phím & trợ năng:** bảng dùng roving tabindex (một điểm dừng `Tab`, di chuyển bằng `↑`/`↓`), vòng focus rõ trên dòng, `aria-selected` cho Active Row, `aria-live="polite"` cho Toast và thay đổi SLA khẩn.
- **Trạng thái dữ liệu:** bắt buộc có Loading (skeleton đúng chiều cao dòng), Empty (nêu lý do và nút xóa bộ lọc), Error (nút thử lại), Offline (giữ dữ liệu cũ, hiện banner; bộ đếm SLA vẫn chạy phía client).

### 5.3. Bộ đếm SLA & Tiến trình Maker-Checker

**SLA Countdown Badge** — định dạng `[+/-]XXh YYp`:

| Điều kiện | Nền | Chữ | Ghi chú |
| :--- | :--- | :--- | :--- |
| Còn > 20% thời gian | `#F1F5F9` | `#475569` | — |
| Còn ≤ 20% thời gian | `#FFFBEB` | `#B45309` | Kèm chấm tròn vàng. |
| Quá hạn (< 00h 00p) | `#FEF2F2` | `#B91C1C` | `font-weight: 600`, có dấu trừ (VD: `-01h 15p`). |

**Maker - Checker Micro-Stepper** — hiển thị ngang 1 dòng: `[M] ➔ [C] ➔ [A]`

- Bước đã hoàn thành: nền xanh lá nhạt, chữ `#047857` kèm dấu tích.
- Bước hiện tại đang giữ hồ sơ: nền `#0E7490`, chữ `#FFFFFF` (Info, không dùng Primary để khỏi nhầm với nút).
- Bước bị trả lại (Returned): nền `#FEF2F2`, chữ `#B91C1C`.
- **Quy tắc phân quyền:** Maker, Checker, Approver của cùng một hồ sơ phải là ba tài khoản khác nhau. Nút duyệt bị vô hiệu hóa kèm Tooltip giải thích nếu người dùng đã tham gia bước trước.

### 5.4. Ngăn kéo Xử lý Chi tiết (Right-hand Contextual Drawer)

- **Chiều rộng:** `560px` (màn 1366px) hoặc `640px` (màn ≥ 1920px). **Không** dùng Dark Backdrop che bảng bên trái — chuyên viên vẫn đối chiếu được danh sách.
- **Cấu trúc 3 phần cố định:**
  1. **Sticky Header (`64px`):** Mã hồ sơ, Tên khách hàng, Nhãn SLA, nút Đóng (`Esc`).
  2. **Scrollable Body:** Các Tab `Thông tin & Checklist kiểm soát`, `Tài liệu đính kèm (PDF Viewer)`, `Nhật ký kiểm toán (Audit Trail)`.
  3. **Sticky Footer (`72px`):** Ô nhập lý do/ý kiến phê duyệt (bắt buộc khi Trả lại/Từ chối) + cụm nút `[Trả lại (Ctrl+Shift+R)]` (viền đỏ) và `[Phê duyệt / Trình ký (Ctrl+Enter)]` (nút chính nền `#2563EB`).

---

## 6. Tương tác & Phím tắt (UX Micro-Interactions)

### 6.1. Keyboard-First Navigation

| Phím tắt | Hành động |
| :--- | :--- |
| `Ctrl + K` / `Cmd + K` | Mở thanh tìm kiếm lệnh, tra cứu nhanh hồ sơ theo Mã CIF / Mã giao dịch. |
| `↑` / `↓` | Di chuyển giữa các dòng; Drawer tự cập nhật theo dòng đang chọn. |
| `Ctrl + Enter` (`F9` là phím phụ) | Hoàn tất / Trình Checker / Phê duyệt hồ sơ hiện tại. `F9` có thể bị phím chức năng/media của laptop chiếm. |
| `Ctrl + Shift + R` | Mở hộp thoại Trả lại hồ sơ (bắt buộc nhập lý do, luôn có bước xác nhận). |
| `Esc` | Chỉ đóng Drawer hoặc hộp thoại đang mở. Không bao giờ kích hoạt hành động nghiệp vụ. |

### 6.2. Auto-Advance (Tự động nạp hồ sơ kế tiếp)

Ngay sau khi bấm `Trình Checker` hoặc `Phê duyệt`, hiển thị Toast ở góc dưới trái và **nạp hồ sơ tiếp theo vào Drawer**, không đóng Drawer.

- Toast hiển thị **8 giây** kèm nút `Hoàn tác (Ctrl+Z)`; hoàn tác đưa hồ sơ về bước trước và ghi vào Audit Trail.
- Hồ sơ có giá trị vượt hạn mức cấu hình (ví dụ ≥ 1 tỷ ₫) hiện hộp xác nhận trước khi duyệt và không tự nạp hồ sơ kế tiếp cho đến khi xác nhận.
- Hồ sơ kế tiếp được nạp ở trạng thái chưa thao tác; phím tắt duyệt bị vô hiệu trong 500ms đầu để tránh bấm đúp nhầm.

### 6.3. Khóa Hồ sơ (Pessimistic Locking UI)

Khi hồ sơ đang được User A xử lý, trên màn hình người khác dòng đó có `opacity: 0.75`, icon ổ khóa và Tooltip:
`"Đang được xử lý bởi [Mã NV - Họ Tên] từ [HH:MM]"`.

---

## 7. Danh mục Kiểm tra trước khi Nghiệm thu (Definition of Done)

- [ ] Toàn bộ cột số tiền, tỷ lệ %, bộ đếm SLA đã gắn `font-variant-numeric: tabular-nums` và căn phải.
- [ ] Không màn hình nào yêu cầu tải lại trang khi chuyển từ danh sách sang xem chi tiết và phê duyệt.
- [ ] `Compact Mode (32px)` hiển thị tối thiểu 12 dòng ở viewport thực `1366x768` (đã trừ thanh trình duyệt, thẻ KPI mở) và tối thiểu 18 dòng khi thẻ KPI thu gọn hoặc ở trình duyệt toàn màn hình, không vỡ chữ.
- [ ] `Comfort Mode (44px)` được kiểm tra riêng: không cắt chữ, các cột phải vẫn căn đúng.
- [ ] Mọi hành động `Trả lại / Từ chối` đều validate trường lý do trước khi cho gửi.
- [ ] `Esc` không kích hoạt hành động nghiệp vụ; Auto-Advance có Hoàn tác và hộp xác nhận cho hồ sơ vượt hạn mức.
- [ ] Mọi cặp chữ/nền đạt 4,5:1, viền Input/Checkbox đạt 3:1 (kiểm bằng công cụ, không bằng mắt).
- [ ] Có đủ trạng thái Loading, Empty, Error, Offline và Locked cho Work List và Drawer.
- [ ] Điều hướng hoàn toàn bằng bàn phím, có focus ring rõ, Toast và SLA khẩn được đọc bởi trình đọc màn hình.
- [ ] Hiển thị đúng khi bật `prefers-reduced-motion`.
- [ ] Audit Trail hiển thị đủ 4 thông tin: `Người thực hiện (Account ID)`, `Vai trò (Maker/Checker)`, `Hành động`, `Thời gian (DD/MM/YYYY HH:mm:ss)`.
