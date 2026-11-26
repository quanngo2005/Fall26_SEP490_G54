import { QuarterValues } from '../../core/target-assignment.models';

export type QuarterKey = keyof QuarterValues;
export const QUARTERS: readonly QuarterKey[] = ['q1', 'q2', 'q3', 'q4'];
