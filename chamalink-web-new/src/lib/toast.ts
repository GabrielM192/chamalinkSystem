import { createContext, useContext } from 'react';
import type { StatusTone } from './status';

export type PushToast = (text: string, tone?: StatusTone) => void;
export const ToastCtx = createContext<PushToast>(() => {});

export function useToast(): PushToast {
  return useContext(ToastCtx);
}
