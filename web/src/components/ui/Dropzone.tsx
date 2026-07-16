'use client';

import { useId, useRef, useState, type DragEvent } from 'react';
import { ImagePlus } from 'lucide-react';

import { cn } from '@/lib/utils/cn';

export interface DropzoneProps {
  readonly onFiles: (files: File[]) => void;
  readonly accept?: string;
  readonly disabled?: boolean;
  /** Limits/format hint, wired to the control via aria-describedby. */
  readonly description?: string;
  readonly label?: string;
}

/**
 * VRB-101 — a labelled drop target for file uploads. It's a real `<button>`
 * (keyboard-operable, opens the file picker) plus a drag-and-drop surface; the
 * `description` is associated via `aria-describedby` so screen-reader users hear
 * the size/type limits.
 */
export const Dropzone = ({
  onFiles,
  accept = 'image/jpeg,image/png,image/webp',
  disabled = false,
  description,
  label = 'Drag photos or browse',
}: DropzoneProps) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const descId = useId();

  const emit = (list: FileList | null) => {
    if (list && list.length > 0) onFiles(Array.from(list));
  };

  return (
    <div>
      <button
        type="button"
        disabled={disabled}
        aria-describedby={description ? descId : undefined}
        onClick={() => inputRef.current?.click()}
        onDragOver={(e: DragEvent) => {
          e.preventDefault();
          if (!disabled) setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e: DragEvent) => {
          e.preventDefault();
          setDragOver(false);
          if (!disabled) emit(e.dataTransfer.files);
        }}
        className={cn(
          'flex w-full flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed p-8 text-sm transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          dragOver
            ? 'border-brand-maroon-600 bg-brand-maroon-600/5'
            : 'border-input hover:border-brand-maroon-400',
          disabled && 'cursor-not-allowed opacity-50',
        )}
      >
        <ImagePlus className="h-6 w-6 text-muted-foreground" aria-hidden="true" />
        <span className="font-medium">{label}</span>
      </button>
      {description && (
        <p id={descId} className="mt-1.5 text-xs text-muted-foreground">
          {description}
        </p>
      )}
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        multiple
        className="hidden"
        onChange={(e) => {
          emit(e.target.files);
          e.target.value = '';
        }}
      />
    </div>
  );
};
