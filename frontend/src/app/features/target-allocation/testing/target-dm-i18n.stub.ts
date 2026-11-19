import { Localization } from '../../../core/i18n/i18n.models';
import {
  TARGET_SHARED_I18N_STUB,
  targetSharedLocalizationStub,
} from '../../../shared/target/testing/target-shared-i18n.stub';

/**
 * `targetDm` dictionary used by specs under `features/target-allocation/**` (W9/W10/W11) until the real
 * keys land in `core/i18n/localization.json` (see `docs/plans/ta-v3-dm.pending.json`). Keep this in sync
 * with that pending file so a test failure here means the merged dictionary needs the same fix.
 */
export const TARGET_DM_I18N_STUB = {
  breadcrumb: 'Chỉ tiêu & chiến dịch',
  w9Title: 'Tổng quan mục tiêu',
  w10Title: 'Phân bổ mục tiêu nhân viên',
  w9Heading: 'Tổng quan mục tiêu · Phòng {department}',
  w10Heading: 'Phân bổ mục tiêu · Phòng {department}',
  draftBadge: 'Nháp',
  yearLabel: 'Năm',
  wholeYear: 'Cả năm',
  loading: 'Đang tải dữ liệu…',
  loadFailed: 'Không tải được dữ liệu. Vui lòng thử lại.',
  retry: 'Thử lại',
  noPublishedTier2: 'Phòng chưa có chỉ tiêu phòng ban đã công bố cho năm này.',
  exportFile: 'Xuất file',
  exportDisabledReason: 'Chưa có API xuất file cho màn này (cần bổ sung ở backend).',
  importExcel: 'Nhập từ Excel',
  importDisabledReason: 'Chưa có API nhập Excel ở cấp nhân viên (cần bổ sung ở backend).',
  goToAllocation: 'Phân bổ cho nhân viên',
  suggestAllocation: 'Gợi ý phân bổ',
  groupOther: 'Khác',
  positionOther: 'Khác',
  employeesShort: 'NV',
  kpisShort: 'KPI',
  kpisAllocated: 'KPI chia đủ',
  statusMismatch: '{mismatch} / {total} KPI {tab} còn lệch',
  newEmployeeNote: '{count} nhân viên mới chưa có mục tiêu',
  newEmployeeCardTitle: 'Nhân viên mới chưa có mục tiêu',
  feedbackCardTitle: 'Phản hồi nhân viên',
  feedbackCardUnavailable: 'Chưa có API',
  feedbackPanelTitle: 'Phản hồi nhân viên',
  feedbackChipOpen: 'Góp ý',
  feedbackChipWaiting: 'Chờ',
  feedbackChipNone: 'Chưa có',
  feedbackPanelUnavailable:
    'Chưa có API liệt kê phản hồi nhân viên theo phòng; cần bổ sung ở backend.',
  colDeptTarget: 'Mục tiêu phòng',
  colAllocatedBar: 'Đã chia',
  colRemaining: 'Còn lại',
  colAchieved: 'Đạt',
  colKpi: 'KPI',
  colYear: 'Cả năm',
  achievedUnavailable: '—',
  ratioNotSplit: 'Không chia',
  filterAll: 'Tất cả',
  filterMismatched: 'Còn lệch',
  searchPlaceholder: 'Tìm KPI',
  compareLastYear: 'So với năm trước',
  newBadge: 'Mới',
  lockedNotResponsible: 'Nhân viên không giữ vị trí phụ trách KPI này.',
  readyToPublish: 'Mọi KPI đã khớp 5 kỳ (cả năm, Q1-Q4). Sẵn sàng công bố.',
  notReadyToPublish: 'Còn KPI chưa khớp đủ 5 kỳ; không thể công bố.',
  publish: 'Công bố',
  published: 'Đã công bố mục tiêu nhân viên.',
  drawerSubtitle: 'Năm',
  drawerRebalanceNote:
    'Sửa một quý thì các quý còn lại tự cân đối để 4 quý bằng cả năm. KPI số dư: mỗi quý là một mức số dư, không cộng.',
  historyTitle: 'Lịch sử',
  historyEmpty: 'Chưa có thay đổi.',
  historyManualEdit: 'Sửa tay {kpi} của {employee}: {value}',
  historyQuarterEdit: 'Sửa Q{quarter} {kpi} của {employee}',
  historySuggested: 'Gợi ý · căn cứ {basis} · {count} ô đã điền',
  employeeFeedbackTitle: 'Góp ý của nhân viên',
  employeeFeedbackUnavailable:
    'Chưa có (chưa phát hành) hoặc chưa có API lấy góp ý theo nhân viên.',
  close: 'Đóng',
  saveForEmployee: 'Lưu cho nhân viên này',
  savedForEmployee: 'Đã lưu mục tiêu cho {name}.',
  saveFailed: 'Lưu không thành công. Vui lòng thử lại.',
  traceId: 'Mã lỗi {traceId}',
} as const;

/** Minimal `Localization` stub (`targetDm` + the `targetShared` components this feature reuses). */
export function targetDmLocalizationStub(): Localization {
  const shared = targetSharedLocalizationStub();
  return {
    ...shared,
    targetDm: TARGET_DM_I18N_STUB,
    targetShared: TARGET_SHARED_I18N_STUB,
  } as unknown as Localization;
}
