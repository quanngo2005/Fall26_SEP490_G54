/** Mirrors G54.BLL.Dtos.Profile.MyPhoneDto: the phone number after a change (null when cleared). */
export interface PhoneUpdate {
  phone: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** Stable error code of a wrong current password; it is a 400 on purpose so the interceptor does not treat it as a 401. */
export const INVALID_CURRENT_PASSWORD = 'INVALID_CURRENT_PASSWORD';

/** Same rule as the backend (PhoneRules): exactly 10 digits, starting with 0. An empty value clears the number. */
export const PHONE_PATTERN = /^0[0-9]{9}$/;
