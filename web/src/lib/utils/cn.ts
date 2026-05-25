import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** Concat Tailwind classes with conflict resolution (shadcn convention). */
export const cn = (...inputs: ClassValue[]): string => twMerge(clsx(inputs));
