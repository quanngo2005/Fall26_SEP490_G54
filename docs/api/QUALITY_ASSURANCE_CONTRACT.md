# Quality Assurance Contract

Trạng thái: gắn cờ chất lượng, yêu cầu làm lại (rework) và QA re-verify mẫu 5% (BR-32) **đã triển khai** (backend). Unit-test với InMemory (`QualityAssuranceServiceTests`); chưa chạy qua HTTP / Postgres thật, chưa có frontend.

Mọi response dùng envelope `ApiResponse` (xem `API_CONVENTIONS.md`). Entity: `quality_flag`, `qa_sample`. Migration: `AddQualityAssurance`.

## 1. Khái niệm

- **Quality flag**: QA gắn cờ một kết quả **đã duyệt** (`result_submission.status = APPROVED`). Cờ gắn vào submission và truy ngược ra nhân viên nộp (`employeeId`).
- **Rework**: QA gửi công việc về cho nhân viên làm lại.
- **QA sample**: khi verifier **duyệt thủ công** một kết quả (`POST /verifications/{id}/decision` = `APPROVED`), hệ thống bốc ngẫu nhiên 5% và tạo một mẫu `PENDING` cho QA kiểm tra lại. Mỗi submission tối đa một mẫu. Tỷ lệ là hằng số `RandomQaSamplingPolicy.SampleRate` (chưa có bảng system parameter). Việc bốc mẫu độc lập với người duyệt, không ảnh hưởng hay phạt người duyệt.

## 2. Vòng đời

```
quality_flag:  OPEN ──request-rework──▶ REWORK_REQUESTED
                 └────dismiss─────────▶ DISMISSED

qa_sample:     PENDING ──decision PASSED──▶ PASSED
                  └──────decision FAILED──▶ FAILED  (tạo quality_flag, tuỳ chọn rework ngay)
```

- `REWORK_REQUESTED`, `DISMISSED`, `PASSED`, `FAILED` là trạng thái cuối. Chuyển sai trạng thái là 409.
- Mỗi submission chỉ có **một cờ OPEN** tại một thời điểm (409 nếu gắn thêm).
- Người thao tác **không được là nhân viên nộp kết quả**: 403 `FORBIDDEN`.

## 3. Rework làm gì

`POST /quality-flags/{id}/request-rework` (hoặc `decision` `FAILED` kèm `requestRework: true`), trong một transaction serializable:

1. `verified_work_result` còn `VALID` của submission → `INVALID` + `invalidated_at`. Trigger Postgres `invalidate_source_actual_progress` trừ phần đóng góp khỏi `actual_progress` (xem `AlignRelationshipsWithErd`), nên điểm KPI của công việc này bị rút lại.
2. `work.status` → `IN_PROGRESS` (action `Rework` của `WorkStateMachine`, cho phép từ `VERIFIED` và `DONE`). Nhân viên nộp lại như bình thường (submission mới, `SubmissionNo` + 1); submission cũ giữ `APPROVED` làm lịch sử.
3. Cờ → `REWORK_REQUESTED` (`reworkComment`, `reworkRequestedAt`).
4. Thông báo `REWORK_REQUESTED` (liên kết `WORK`) cho nhân viên nộp và người được giao việc.

Điều kiện / lỗi:

| Tình huống | Lỗi |
|---|---|
| Submission không `APPROVED` | 409 `VERIFICATION_INVALID_STATE` |
| Không còn verified result `VALID` | 409 `VERIFICATION_INVALID_STATE` |
| Công việc ở trạng thái không thể mở lại (vd `CANCELLED`) | 409 `VERIFICATION_INVALID_STATE` |
| Tiến độ KPI thuộc kỳ đã khoá | 409 `PERIOD_LOCKED` |

Duyệt lại sau rework: kiểm tra `KPI_ALREADY_CONTRIBUTED` (BR-08) chỉ tính verified result `VALID`, nên kết quả đã bị rework không chặn lần duyệt mới.

## 4. Quyền

| Endpoint | Quyền (`resource:action`, scope) |
|---|---|
| `POST /submissions/{submissionId}/quality-flags` | `quality_flag:create`, `All` |
| `GET /quality-flags`, `GET /quality-flags/{id}` | `quality_flag:read`, `All` |
| `POST /quality-flags/{id}/request-rework` | `quality_flag:rework`, `All` |
| `POST /quality-flags/{id}/dismiss` | `quality_flag:dismiss`, `All` |
| `GET /qa-samples` | `qa_sample:read`, `All` |
| `POST /qa-samples/{id}/decision` | `qa_sample:decide`, `All` |

Migration tạo role `QA` (nếu chưa có) và cấp toàn bộ sáu quyền trên cho `QA`, `ADMIN`, `SYSTEM_ADMIN`.

## 5. Endpoint

### POST /submissions/{submissionId}/quality-flags → 201

```json
{ "reason": "Số liệu trên chứng từ không khớp với báo cáo.", "severity": "MEDIUM" }
```

`reason` bắt buộc, 10–2000 ký tự. `severity`: `LOW` / `MEDIUM` (mặc định) / `HIGH`. Trả `QualityFlagDto`.

### GET /quality-flags

Query: `status` (`OPEN` / `REWORK_REQUESTED` / `DISMISSED`), `employeeId`, `page` (1), `pageSize` (20, tối đa 100). Mới nhất trước. Trả `PagedResult<QualityFlagDto>`.

### POST /quality-flags/{id}/request-rework → 200

```json
{ "comment": "Làm lại báo cáo và đính kèm chứng từ." }
```

`comment` bắt buộc, 10–2000 ký tự. Trả `QualityFlagDto`.

### POST /quality-flags/{id}/dismiss → 200

Không body. Trả `QualityFlagDto`.

### GET /qa-samples

Query: `status` (`PENDING` / `PASSED` / `FAILED`), `page`, `pageSize`. `PENDING` lên trước, rồi mới nhất trước. Trả `PagedResult<QaSampleDto>`.

### POST /qa-samples/{id}/decision → 200

```json
{ "result": "FAILED", "comment": "Chứng từ không hợp lệ.", "severity": "HIGH", "requestRework": true }
```

- `result`: `PASSED` hoặc `FAILED`.
- `FAILED`: `comment` bắt buộc (10–2000 ký tự); tạo `quality_flag` liên kết mẫu (`severity` mặc định `MEDIUM`); `requestRework: true` gửi công việc về làm lại ngay trong cùng lời gọi.
- `PASSED` kèm `requestRework: true` là 400.

Trả `{ "sample": QaSampleDto, "flag": QualityFlagDto | null }`.

## 6. DTO

`QualityFlagDto`: `id`, `resultSubmissionId`, `submissionNo`, `workId`, `workCode`, `workTitle`, `employeeId`, `employeeCode`, `employeeName`, `flaggedByEmployeeId`, `flaggedByName`, `qaSampleId`, `severity`, `reason`, `status`, `reworkComment`, `reworkRequestedAt`, `createdAt`.

`QaSampleDto`: `id`, `resultSubmissionId`, `submissionNo`, `workId`, `workCode`, `workTitle`, `employeeId`, `employeeCode`, `employeeName`, `status`, `sampledAt`, `reviewedByEmployeeId`, `reviewedByName`, `reviewedAt`, `comment`, `qualityFlagId`.

## 7. Chưa làm / lưu ý

- Chưa có frontend; chưa có sidebar item cho QA.
- Tỷ lệ 5% là hằng số, chưa đọc từ system parameter.
- Hiện **mọi** công việc đều đi qua duyệt thủ công (API tạo work bắt buộc có WorkType, chưa có luồng work không thuộc WorkType), nên mẫu 5% bốc trên mọi lần duyệt thủ công. Khi `BE_STATUS.md` mục 3 được làm (chỉ work ngoài danh mục WorkType mới duyệt tay), mẫu sẽ tự chỉ còn rơi vào nhóm đó. Kết quả auto-match từ giao dịch import không bị bốc mẫu.
- `actual_progress.value` là tổng số **thô** (`raw_value`) của các kết quả trong ngày; hệ số chỉ áp khi tính điểm (từ `work_result_item.value`). Migration `FixInvalidateProgressUsesRawValue` sửa trigger `invalidate_source_actual_progress` trừ `raw_value` (trước đó trừ giá trị đã nhân hệ số) và chỉ đánh dòng `INVALID` khi không còn kết quả `VALID` nào trỏ vào. Một ngày có nhiều hệ số khác nhau vẫn đúng. Duyệt mới vào dòng đã `INVALID` sẽ bật lại dòng đó.
- Rework không tạo bút toán đảo (BR-16); chặn bằng `PERIOD_LOCKED` khi kỳ đã khoá.
