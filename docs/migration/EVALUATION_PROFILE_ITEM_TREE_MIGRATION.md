# Migration note: EvaluationProfileItem → cây KPI, bỏ task_weight, thêm performance_evaluation_history

> Chỉ là ghi chú, chưa sửa code. Quyết định đã chốt:
> - Node cha **không** có KPI (chỉ có `name` + weight), node lá bắt buộc có `kpi_definition_id`.
> - Dữ liệu `evaluation_profile_item` cũ: **xóa, seed lại**.
> - `performance_evaluation_history`: snapshot mỗi lần tính lại / đổi status / appeal.

## 0. Luồng giao & tính điểm KPI (dữ liệu sample)

**Cấu hình** — JobPosition "Sales", EvaluationProfile `SALES_2026` (Active):

```
evaluation_profile_item
├─ [root] "Doanh số"      weight 70   (name, kpi = null)
│   ├─ KPI DS_THANG       weight 60   HigherBetter, Sum,   cap 120
│   └─ KPI KH_MOI         weight 40   HigherBetter, Count, cap 110
└─ [root] "Chất lượng"    weight 30
    └─ KPI TL_KHIEU_NAI   weight 100  LowerBetter, Ratio (%), cap 120
```

Σ weight các node anh em (cùng parent, kể cả cấp root) = 100.

**1. Giao chỉ tiêu** — `target_assignment` cho NV Nguyễn A, kỳ 10/2026: DS_THANG = 500 (tr), KH_MOI = 10, TL_KHIEU_NAI = 5 (%). Điều chỉnh thì tăng version; khi tính lấy version mới nhất.

**2. Thực hiện** — Task/WorkResult → submit → verify → `verified_work_result` + `work_result_item` (value, denominator) → cộng dồn vào `actual_progress` (status Valid, theo ngày):

- DS_THANG: 3 kết quả 200 + 150 + 100 = **450**
- KH_MOI: **12**
- TL_KHIEU_NAI: 2 khiếu nại / 50 đơn → value 2, denominator 50 = **4%**
- Trước đây `task_weight` = 1.5 cho 1 item value 100 cộng thêm +50 vào DS. **Sau migration bỏ**: actual = tổng value thuần.

**3. Tính điểm KPI (node lá)** — `PerformanceScoreFormula.CalculateKpi`:

| KPI | Công thức | Raw | Capped |
|---|---|---|---|
| DS_THANG | 450 / 500 | 90 | 90 |
| KH_MOI | 12 / 10 | 120 | **110** |
| TL_KHIEU_NAI | target / actual = 5 / 4 | 125 | **120** |

Mỗi lá ghi 1 `performance_contribution` (snapshot target/actual, achievement, `weight_snapshot`).

**4. Gộp lên cha (mới: bình quân có trọng số)**

- Doanh số = 90×60% + 110×40% = **98**
- Chất lượng = 120×100% = **120**
- Total = 98×70% + 120×30% = 68.6 + 36 = **104.6**

Cách cũ (điểm group = trung bình cộng KPI): Doanh số = (90 + 110) / 2 = 100 → Total = **106**. Tức là điểm các kỳ cũ sẽ thay đổi nếu tính lại, nên chỉ recalc các evaluation chưa Approved.

**5. Lưu lịch sử** — `performance_evaluation_history`:

| action | from → to | old → new score | actor |
|---|---|---|---|
| Calculated | Draft → Calculated | null → 104.6 | manager |
| Approved | Calculated → Approved | 104.6 → 104.6 | director |
| Disputed | Approved → Disputed | 104.6 → 104.6 | Nguyễn A |
| AppealResolved | Disputed → Approved | 104.6 → 106.2 | HR |

**Edge case**: KH_MOI chưa có target thì lá bị bỏ qua, nhánh Doanh số chỉ còn DS_THANG. Phải chuẩn hóa lại weight (90 × 60 / 60 = 90); nếu không sẽ ra 54 (sai).

## 1. Schema đích

### `evaluation_profile_item`
- DROP: FK `kpi_group_id`, index `uq_evaluation_profile_item (evaluation_profile_id, kpi_group_id)`, cột `kpi_group_id`.
- ADD:
  - `kpi_definition_id uuid NULL` → FK `kpi_definition(id)` ON DELETE RESTRICT.
  - `parent_item_id uuid NULL` → FK `evaluation_profile_item(id)` (self). EF: `DeleteBehavior.ClientCascade`.
  - `name varchar(200) NULL` (tên node cha).
- CHECK `ck_evaluation_profile_item_node`: `num_nonnulls(name, kpi_definition_id) = 1`.
- Giữ check `weight_percent > 0 AND weight_percent <= 100`, `display_order > 0`.
- UNIQUE partial `uq_evaluation_profile_item_kpi (evaluation_profile_id, kpi_definition_id) WHERE kpi_definition_id IS NOT NULL` (EF `HasFilter`).
- INDEX `ix_evaluation_profile_item_parent_item_id`.

### `work_result_item`
- DROP `task_weight`.

### `performance_evaluation_history` (mới)
| cột | kiểu | ghi chú |
|---|---|---|
| id | uuid PK | `gen_random_uuid()` |
| performance_evaluation_id | uuid NOT NULL | FK cascade |
| action | varchar/enum | Calculated, StatusChanged, Approved, Disputed, DisputeWithdrawn, AppealResolved |
| from_status | enum NULL | |
| to_status | enum NOT NULL | |
| old_total_score | numeric NULL | cùng precision với `total_score` |
| new_total_score | numeric NULL | |
| actor_user_id | uuid NULL | FK user, SET NULL |
| comment | text NULL | |
| created_at | timestamptz | default `now()` |

INDEX `(performance_evaluation_id, created_at)`.

## 2. Thứ tự bước trong migration `RestructureEvaluationProfileItemTree`

1. `DELETE FROM evaluation_profile_item;` (data cũ bỏ, seed lại).
2. Profile đang Active không còn item: chuyển `Draft` (trừ khi đã có evaluation Approved).
3. Drop index `uq_evaluation_profile_item`, FK `kpi_group_id`, cột `kpi_group_id`.
4. Add cột `kpi_definition_id`, `parent_item_id`, `name`, các FK, check, index.
5. Drop `work_result_item.task_weight`.
6. Create `performance_evaluation_history` + index.
7. `Down()`: tạo lại `kpi_group_id` (nullable), `task_weight` (default 1), drop history và các cột mới. **Mất dữ liệu, không khôi phục được item cũ.**

Sau đó: cập nhật `Scripts/seed_dev_data.sql` (seed cây item như sample ở mục 0), `Scripts/progest_erd_v0_10_postgresql.sql`, kiểm tra DbMigrator chạy migration + seed.

## 3. File cần sửa

**DAL**
- `Entities/EvaluationProfileItem.cs`: bỏ `KpiGroupId`/`KpiGroup`; thêm `KpiDefinitionId?`, `ParentItemId?`, `Name?`, nav `Parent`, `Children`, `KpiDefinition`.
- `Entities/KpiGroup.cs`: bỏ `EvaluationProfileItems`.
- `Entities/KpiDefinition.cs`: thêm `EvaluationProfileItems`.
- `Entities/WorkResultItem.cs`: bỏ `TaskWeight`.
- `Entities/PerformanceEvaluation.cs`: thêm `Histories`.
- Entity mới `Entities/PerformanceEvaluationHistory.cs` + enum action.
- `AppDbContext.cs`: `:593` config item, `:1658` config TaskWeight, thêm DbSet + config history.
- `Migrations/AppDbContextModelSnapshot.cs` (generated).

**BLL** (ảnh hưởng, sửa ở bước sau)
- `PerformanceEvaluationService.cs:40-170`: Include theo KPI, dựng cây, tính cha theo trọng số. `:334-354`: xóa adjustment TaskWeight.
- `IPerformanceScoreFormula.cs:40-45`: `GroupScoreInput`/`GroupScoreEntry` key theo `ItemId` thay `KpiGroupId`.
- `EvaluationService.cs:159, 394, 719, 1465, 1640, 1846`: vòng lặp theo group, qualitative score key `KpiGroupId`.
- `EvaluationProfileService.cs` + DTO `EvaluationProfileItem*Dto`, `Create/UpdateEvaluationProfileDto`.
- `JobPositionService.cs:264-296`, `PerformanceDashboardService.Personal.cs:90-143`, `PerformanceDashboardService.Unit.cs:109`.
- `AppealService` / approve / dispute: ghi history.
- Contract `docs/api/EVALUATION_PROFILE_CONTRACT.md` + frontend models dùng `kpiGroupId`/`taskWeight`.

**Tests**: `EvaluationProfileItemServiceTests`, `EvaluationProfileServiceTests`, `EvaluationServiceTests`, `PerformanceEvaluationServiceTests`, `PerformanceCalculationEdgeCaseTests`, `PerformanceDashboardServiceTests`, `KpiDefinitionServiceTests`, `WorkTypeServiceTests`, `JobPositionServiceTests`, `EnumApiFormatContractTests`...

## 4. Expected bugs và phương án xử lý

| # | Bug dự kiến | Phương án khuyến nghị |
|---|---|---|
| 1 | Profile Active mất hết item, calc ra total null, dashboard rỗng | Migration chuyển về Draft; `CalculateAsync` throw lỗi nghiệp vụ khi profile không có item |
| 2 | Chu trình cha-con / parent thuộc profile khác → đệ quy vô hạn | API nhận cây lồng (`children`), server tự gán `ParentItemId`; giới hạn depth ≤ 3 |
| 3 | Σ weight sai cấp → điểm lệch | Validate từng nhóm anh em Σ = 100 (±0.01); chỉ round ở total |
| 4 | Cha không có con / lá thiếu KPI → chia 0 hoặc null | Check DB `num_nonnulls = 1` + BLL: node có name phải ≥ 1 con, node KPI không có con |
| 5 | KPI trùng trong 1 profile → đếm 2 lần | Partial unique index + BLL báo lỗi thân thiện trước khi SaveChanges |
| 6 | Xóa/deactivate KPI đang dùng → FK RESTRICT ném `DbUpdateException` → 500 | `KpiDefinitionService` chặn trước (409); khi tính bỏ lá inactive và chuẩn hóa weight anh em |
| 7 | Xóa profile/cha → lỗi thứ tự cascade trong EF | Self-FK `ClientCascade`; update profile = xóa toàn bộ item + insert cây mới trong 1 transaction |
| 8 | Bỏ task_weight → điểm kỳ cũ đổi khi recalc; code/FE còn gửi `taskWeight` | Không recalc evaluation Approved; grep xóa sạch `taskWeight` ở BE/FE/docs |
| 9 | Qualitative JSON (regex trong comment) key theo `KpiGroupId` → không khớp, `catch` rỗng nuốt lỗi | Đổi key `EvaluationProfileItemId`; log warning thay `catch` rỗng |
| 10 | Dashboard group theo `KpiDefinition.KpiGroupId` nhưng weight lấy từ item → sai nhóm | Map contribution → lá → root item. Tốt hơn: thêm `evaluation_profile_item_id` (FK SET NULL) vào `performance_contribution` |
| 11 | History lệch với status khi SaveChanges fail | Thêm history vào cùng context, commit chung `SaveChangesAsync`; dựa vào xmin concurrency đã có |
| 12 | `Down()` không khôi phục được dữ liệu | Ghi chú rõ trong migration; backup DB trước khi deploy |
| 13 | `EnumApiFormatContractTests` fail do enum mới | Thêm enum history action vào test |
| 14 | Include cây nhiều cấp → query nặng / N+1 | Load phẳng item theo profile + `Include(KpiDefinition)`, dựng cây bằng `ToLookup(ParentItemId)` |

## 5. Checklist verify migration

- [ ] `dotnet ef migrations add RestructureEvaluationProfileItemTree -p src/G54.DAL -s src/G54.Api`
- [ ] `dotnet ef migrations script <prev> RestructureEvaluationProfileItemTree`: review SQL (thứ tự DELETE → drop → add)
- [ ] Backup DB, chạy DbMigrator trên DB local (`./run.ps1 -Docker`)
- [ ] `psql \d evaluation_profile_item`, `\d performance_evaluation_history`, `\d work_result_item` đúng schema
- [ ] Seed dev chạy được; tính điểm NV sample ra **104.6**
- [ ] Test Down() trên DB copy
- [ ] `./scripts/codereview-backend.ps1` → `## ALL CHECKS PASSED`

## 6. Trạng thái hiện tại (2026-10-29): dừng ở bước migration

### Đã làm
- Migration `backend/src/G54.DAL/Migrations/20261029042206_RestructureEvaluationProfileItemTree.cs` + Designer + snapshot (`has-pending-model-changes`: không còn thay đổi).
  - Up: xóa item cũ, profile ACTIVE → DRAFT, drop `kpi_group_id`/FK/unique (dùng `DROP ... IF EXISTS` vì DB tạo từ ERD script có `uq_evaluation_profile_item` là CONSTRAINT, còn `IX_evaluation_profile_item_kpi_group_id` chỉ có trong snapshot), drop `task_weight` + `ck_work_result_item_task_weight`, thêm cột cây + check/index/FK, tạo enum + bảng history.
  - Down: xóa item rồi dựng lại cấu trúc cũ (mất dữ liệu cây).
- Entities: `EvaluationProfileItem` (ParentItemId, KpiDefinitionId, Name, ParentItem, ChildItems, KpiDefinition), `KpiGroup` (bỏ nav item), `KpiDefinition` (thêm nav item), `WorkResultItem` (bỏ TaskWeight), `PerformanceEvaluation` (Histories), mới `PerformanceEvaluationHistory` (cột `changed_by`), enum `PerformanceEvaluationHistoryAction`.
- `AppDbContext.cs`, `ServiceCollectionExtensions.cs` (MapEnum), `Scripts/seed_dev_data.sql` (cây item + bật lại ACTIVE cho 2 profile seed).

### Chưa sửa (solution hiện KHÔNG build được ở BLL/Api/Tests)
| Khu vực | File / vị trí | Việc cần làm |
|---|---|---|
| Tính điểm | `PerformanceEvaluationService.cs:40-170` | Include `EvaluationProfileItems.KpiDefinition`, dựng cây, cha = Σ(con×weight)/Σweight, chuẩn hóa khi lá bị bỏ qua; ghi history |
| Bỏ TaskWeight | `PerformanceEvaluationService.cs:334-354` | Xóa khối adjustment `TaskWeight` |
| Formula | `IPerformanceScoreFormula.cs:40-45`, `PerformanceScoreFormula` | Group key theo `ItemId`; `CalculateGroup` dùng trọng số thay trung bình cộng |
| Evaluation cũ | `EvaluationService.cs:165, 235-243, 399, 721, 1472, 1641, 1846` | Bỏ `item.KpiGroup`; qualitative score key `KpiGroupId` → `EvaluationProfileItemId` |
| CRUD profile | `EvaluationProfileService.cs:112, 311, 332, 416, 428, 584-627` + DTO `EvaluationProfileItem*Dto`, `Create/UpdateEvaluationProfileDto`, `IEvaluationProfileService` | Input dạng cây; validate chu trình, Σ anh em = 100, cha phải có con, KPI không trùng |
| Job position | `JobPositionService.cs:264-296`, `JobPositionDetailDto` | Lấy tên nhóm từ `item.Name` thay `KpiGroup` |
| Dashboard | `PerformanceDashboardService.Personal.cs:90-143`, `.Unit.cs:109` | Group theo item gốc thay `KpiGroupId` |
| History | Approve / dispute / withdraw / `AppealService` | Thêm `PerformanceEvaluationHistory` cùng `SaveChangesAsync` |
| KPI | `KpiDefinitionService` | Chặn xóa/deactivate KPI đang nằm trong profile ACTIVE (409) thay vì để FK ném 500 |
| ERD script | `Scripts/progest_erd_v0_10_postgresql.sql:420-440` | Cập nhật bảng item, bỏ `task_weight`, thêm bảng history |
| Contract | `docs/api/EVALUATION_PROFILE_CONTRACT.md` | Cập nhật DTO cây trước khi sửa FE |
| Frontend | models/form profile, chỗ dùng `kpiGroupId`/`taskWeight` | Theo contract mới, localize vi/en |
| Tests | `AppDbContextModelTests` (enum count 32 → 35), `EvaluationProfile*Tests`, `EvaluationServiceTests`, `PerformanceEvaluationServiceTests`, `PerformanceCalculationEdgeCaseTests`, `PerformanceDashboardServiceTests`, `JobPositionServiceTests`, `WorkTypeServiceTests`, `KpiMappingServiceTests`, `VerifiedResultLedgerTests`, ... | Đổi seed `KpiGroupId` → cây KPI; thêm test cây/chu trình/Σ weight/history |

### Đã verify bằng `dotnet ef database update` (Postgres docker, 2026-10-29)
- Up → Down → Up đều chạy được trên DB đang ở `20261028090000`.
- Lỗi phát hiện và đã sửa trong migration: `trg_validate_profile_weight_sum` / `trg_validate_profile_activation` là constraint trigger DEFERRED, kiểm tra tổng weight **phẳng** = 100.
  - `DELETE` để lại pending trigger event → `ALTER TABLE` lỗi `55006`. Sửa: `SET CONSTRAINTS ALL IMMEDIATE`, đổi profile sang DRAFT trước rồi mới DELETE.
  - Viết lại 2 function theo cây qua helper `evaluation_profile_tree_error(uuid)`: Σ weight anh em (và root) = 100, nhóm phải có con, lá KPI không có con, parent cùng profile. Down khôi phục bản cũ.
  - ERD script cần cập nhật tương tự.
- Chạy riêng khối seed item: 2 profile ACTIVE × 9 item. Trigger/constraint chặn đúng: weight lệch, nhóm rỗng, item vừa name vừa KPI, KPI trùng.

### Chưa verify
- Seed đầy đủ qua DbMigrator: `docker compose --env-file docker/.env -f docker/docker-compose.yml up migrator` hoặc `dotnet run --project backend/src/G54.DbMigrator` (DbMigrator phụ thuộc BLL → phải build được BLL trước).
- Chưa chạy `./scripts/codereview-backend.ps1`.
- Lưu ý: **không dùng `dotnet ef migrations remove`** — migration `20261028090000_SeedJobPositionManageAccess` không có Designer nên lệnh này ghi đè snapshot thành gần rỗng.
- Lệnh tạo migration dùng DAL làm startup: `dotnet ef migrations add <Name> --project src/G54.DAL --startup-project src/G54.DAL --context AppDbContext`.
