import { Localization } from '../../../core/i18n/i18n.models';

/**
 * `targetShared` dictionary used by specs under `shared/target/**` until the real keys land in
 * `core/i18n/localization.json` (see `docs/plans/ta-v3-shared.pending.json`). Keep this in sync with that
 * pending file so a test failure here means the merged dictionary needs the same fix.
 */
export const TARGET_SHARED_I18N_STUB = {
  suggestPopover: {
    title: 'Gợi ý phân bổ',
    basisTitle: 'Căn cứ',
    basisTargetLastYear: 'Chỉ tiêu năm trước',
    basisActualLastYear: 'Thực hiện năm trước',
    mustRoundBadge: 'cần làm tròn',
    noHistoryFallback: 'Đơn vị mới chưa có dữ liệu năm trước: không thể gợi ý.',
    scopeTitle: 'Phạm vi',
    scopeCurrentKpi: 'KPI hiện tại',
    scopeGroup: 'Cả nhóm KPI',
    scopeUnmatched: 'Các ô chưa khớp',
    scopeSelected: 'Các ô đã chọn',
    employeeTitle: 'Nhân viên',
    employeeAll: 'Tất cả',
    employeeNewOnly: 'Chỉ nhân viên mới',
    periodTitle: 'Kỳ áp dụng',
    periodYearAndQuarters: 'Cả năm và 4 quý',
    periodYearOnly: 'Chỉ cả năm',
    periodCurrentQuarter: 'Quý hiện tại',
    applyTitle: 'Áp dụng vào ô',
    applyEmptyOnly: 'Chỉ ô còn trống',
    applyOverwriteUnlocked: 'Ghi đè ô chưa khoá',
    applyConstraintNote: 'Các quý theo đúng tỷ lệ của đơn vị cha; vượt trần ±X% chỉ là cảnh báo.',
    roundingTitle: 'Làm tròn',
    roundingMoneyStepLabel: 'Bước làm tròn tiền',
    roundingCountStepLabel: 'Bước làm tròn số lượng',
    roundingModeLabel: 'Cách làm tròn',
    roundingModeNearest: 'Gần nhất',
    roundingModeUp: 'Lên',
    roundingModeDown: 'Xuống',
    remainderTitle: 'Xử lý phần dư',
    remainderLargest: 'Dồn vào đơn vị lớn nhất',
    remainderBuffer: 'Dồn vào đơn vị đệm',
    bufferUnitLabel: 'Đơn vị đệm',
    previewTitle: 'Xem trước',
    previewCellsToFill: 'ô sẽ được điền',
    previewInKpis: 'trong',
    previewKpis: 'KPI',
    previewSumOk: 'Tổng sau làm tròn khớp với chỉ tiêu năm.',
    previewSumMismatch: 'Tổng sau làm tròn chưa khớp với chỉ tiêu năm.',
    disabledReasonPrefix: 'Không thể gợi ý:',
    cancel: 'Huỷ',
    createSuggestion: 'Tạo gợi ý',
  },
  previewBanner: {
    suggestedPrefix: 'Đã gợi ý',
    cells: 'ô',
    inKpis: 'trong',
    kpis: 'KPI',
    basisLabel: 'căn cứ',
    roundingLabel: 'làm tròn',
    changeBasis: 'Đổi căn cứ',
    undo: 'Hoàn tác',
    apply: 'Áp dụng',
    fallbackTier1:
      '{count} chỉ tiêu chưa có lịch sử năm trước: chia tăng tiến Q1→Q4 (10/20/30/40%).',
    fallbackTier2:
      '{count} chỉ tiêu chưa có lịch sử năm trước: chia theo số người của phòng, quý tăng tiến Q1→Q4.',
    fallbackTier3:
      '{count} chỉ tiêu chưa có lịch sử năm trước: chia đều cho nhân viên, quý tăng tiến Q1→Q4.',
  },
  cellLegend: {
    title: 'Chú giải',
    suggested: 'Đã gợi ý',
    manual: 'Nhập tay',
    mismatch: 'Lệch',
    locked: 'Đã khoá / không áp dụng',
  },
  allocationCell: {
    lockedDefaultReason: 'Ô đã khoá hoặc không áp dụng cho dòng này.',
  },
  missingPanel: {
    title: 'Còn thiếu trước khi phát hành',
    departmentsMissing: 'Chưa có dòng cho phòng',
    otherBlockers: 'KPI chưa đủ điều kiện phát hành',
    fillZero: 'Đặt 0 cho các phòng thiếu',
    fillZeroHint: 'Tạo dòng 0 cho các phòng chưa được giao KPI này, rồi bấm Lưu nháp.',
    goTo: 'Đến KPI',
  },
  tierStatusLine: {
    matchedOf: 'KPI khớp /',
    mismatched: 'lệch',
    notSplit: 'chưa chia',
    filterAll: 'Tất cả',
    filterMismatched: 'Lệch',
    filterNotSplit: 'Chưa chia',
  },
  kpiGroupTable: {
    colKpiName: 'KPI',
    colKpiType: 'Loại KPI',
    expand: 'Mở nhóm',
    collapse: 'Thu nhóm',
    completeBadge: 'Đủ',
    emptyGroup: 'Nhóm không có KPI.',
  },
  paceBar: {
    onTrack: 'Đạt nhịp',
    slow: 'Chậm',
    atRisk: 'Nguy cơ',
    levelPrefix: 'Mức',
    aboveThreshold: 'Đạt ngưỡng',
  },
  rightDrawer: {
    close: 'Đóng',
  },
} as const;

/** Minimal `Localization` stub (only `targetShared`) for specs in `shared/target/**`. */
export function targetSharedLocalizationStub(): Localization {
  return { targetShared: TARGET_SHARED_I18N_STUB } as unknown as Localization;
}
