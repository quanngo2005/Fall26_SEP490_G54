import { DossierInspection } from './review-verify.model';

export const DEFAULT_MOCK_DOSSIER: DossierInspection = {
  submissionId: 'f104d588-41e2-45e0-8fa8-617066e4a2d8',
  submissionCode: '#SUB-WRK-2026-089-V1',
  version: 'Phiên bản V1',
  workCode: 'WRK-2026-089',
  workTitle: 'Mở thẻ tín dụng KH cá nhân',
  submitter: {
    employeeId: 'e0a786e2-2a2b-4223-95c5-34bf38f4d999',
    fullName: 'Nguyễn Tiến Thành',
    jobPosition: 'Giao dịch viên Quầy (Teller)',
    department: 'P. Khách hàng cá nhân',
    employeeCode: 'EMP-0219',
  },
  submittedAtText: '05/10/2026 16:30',
  submittedAtIso: '2026-10-05T16:30:15Z',
  attemptNumber: 1,
  deadlineText: '10/10/2026',
  kpiItem: {
    kpiCode: 'KPI-CARD-01',
    kpiName: 'Số lượng thẻ tín dụng phát hành',
    declaredValue: 15,
    unit: 'Thẻ',
  },
  narrative:
    'Đã hoàn tất tiếp cận, kiểm tra điều kiện CIC và phát hành thành công 15 thẻ tín dụng JCB/Visa. Danh sách hồ sơ và hợp đồng ký nhận đã scan và đối chiếu đầy đủ lên hệ thống Core Banking.',
  evidenceFiles: [
    {
      fileId: 'file-xlsx-001',
      fileName: 'Bao_cao_danh_sach_15_the_da_phat_hanh.xlsx',
      fileSizeText: '1.4 MB',
      previewUrl: '#',
      downloadUrl: '#',
    },
    {
      fileId: '8b849202-b2d2-4309-8438-2f1bfb8ec012',
      fileName: 'Ho_so_scan_chu_ky_khach_hang_dot1.pdf',
      fileSizeText: '5.2 MB',
      previewUrl: '#',
      downloadUrl: '#',
    },
  ],
  controlStandards: [
    {
      id: 'STD-01',
      name: '1. Số lượng khớp với báo cáo đối soát Core Banking',
      passed: true,
    },
    {
      id: 'STD-02',
      name: '2. Báo cáo nộp đúng hạn thỏa thuận SLA',
      passed: true,
    },
    {
      id: 'STD-03',
      name: '3. Tuân thủ chữ ký pháp lý trên hợp đồng tín dụng',
      passed: false,
    },
  ],
  documentPreview: {
    fileName: 'Hop_dong_mo_the_tin_dung_CIF_8812.pdf',
    docTitle: 'HỢP ĐỒNG PHÁT HÀNH THẺ TÍN DỤNG QUỐC TẾ - NGÂN HÀNG BPMS',
    bankParty: 'Ngân hàng TMCP BPMS - Chi nhánh Hội Sở',
    customerParty: 'NGUYỄN VĂN HOÀNG',
    cif: '88129038',
    product: 'JCB Ultimate Platinum',
    creditLimit: '50.000.000 VNĐ',
    officer: 'Nguyễn Tiến Thành (Mã NV: EMP-0219)',
    totalPages: 3,
    currentPage: 1,
    zoomPercent: 100,
    auditPinAnnotation: {
      page: 3,
      message: 'Phát hiện: Trang 3 thiếu chữ ký xác nhận của chủ thẻ (CIF #8812 và #8819)',
    },
  },
  officialVerifiedValue: 15,
  verificationCode: 'VER-2026-089-R1',
  status: 'SUBMITTED',
};

export const APPROVED_MOCK_DOSSIER: DossierInspection = {
  ...DEFAULT_MOCK_DOSSIER,
  submissionId: 'd205e699-52f3-46f1-9ba9-728177f5b3e9',
  submissionCode: '#SUB-WRK-2026-089-V2',
  version: 'Phiên bản V2',
  attemptNumber: 2,
  controlStandards: [
    {
      id: 'STD-01',
      name: '1. Số lượng khớp với báo cáo đối soát Core Banking',
      passed: true,
    },
    {
      id: 'STD-02',
      name: '2. Báo cáo nộp đúng hạn thỏa thuận SLA',
      passed: true,
    },
    {
      id: 'STD-03',
      name: '3. Tuân thủ chữ ký pháp lý trên hợp đồng tín dụng',
      passed: true,
    },
  ],
  documentPreview: {
    ...DEFAULT_MOCK_DOSSIER.documentPreview,
    auditPinAnnotation: undefined,
  },
  status: 'APPROVED',
};

export const MAKER_CHECKER_VIOLATION_DOSSIER: DossierInspection = {
  ...DEFAULT_MOCK_DOSSIER,
  submitter: {
    ...DEFAULT_MOCK_DOSSIER.submitter,
    employeeId: 'EMP-MGR-001',
    fullName: 'Trần Văn Quản Lý',
    jobPosition: 'Trưởng phòng / Dept Manager',
    employeeCode: 'EMP-MGR-001',
  },
};
