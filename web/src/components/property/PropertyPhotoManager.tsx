'use client';

import { useState } from 'react';
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  rectSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useQueryClient } from '@tanstack/react-query';

import { Dropzone } from '@/components/ui/Dropzone';
import { ImageTile } from '@/components/ui/ImageTile';
import { ConfirmActionModal } from '@/components/ui';
import {
  deletePropertyImage,
  reorderPropertyImages,
  uploadPropertyImage,
  type PropertyImage,
} from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';

const MAX_MB = 10;
const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];

interface PropertyPhotoManagerProps {
  readonly propertyId: string;
  readonly initialImages: readonly PropertyImage[];
}

interface PendingTile {
  readonly tempId: string;
  readonly previewUrl: string;
  readonly error: string | null;
}

const problemMessage = (e: unknown, fallback: string): string =>
  e instanceof ApiProblemError ? (e.problem.detail ?? fallback) : fallback;

const SortablePhotoTile = ({
  image,
  index,
  total,
  onDelete,
}: {
  image: PropertyImage;
  index: number;
  total: number;
  onDelete: () => void;
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: image.id,
  });
  return (
    <ImageTile
      url={image.url}
      caption={image.caption}
      isPrimary={image.isPrimary}
      accessibleName={`Photo ${index + 1} of ${total}${image.isPrimary ? ', cover' : ''}`}
      onDelete={onDelete}
      setNodeRef={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      isDragging={isDragging}
      handleProps={{ ...attributes, ...listeners }}
    />
  );
};

/**
 * VRB-101 — owner gallery manager: drag-to-reorder grid (dnd-kit, keyboard
 * sensor for a11y), upload with per-tile optimistic preview + inline
 * validation, and delete via the shared ConfirmActionModal. Reorder announces
 * through an aria-live region. Consumes design-system primitives — no inline
 * bespoke UI beyond layout.
 */
export const PropertyPhotoManager = ({ propertyId, initialImages }: PropertyPhotoManagerProps) => {
  const qc = useQueryClient();
  const [images, setImages] = useState<readonly PropertyImage[]>(
    [...initialImages].sort((a, b) => a.sortOrder - b.sortOrder),
  );
  const [pending, setPending] = useState<readonly PendingTile[]>([]);
  const [deleteTarget, setDeleteTarget] = useState<PropertyImage | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [live, setLive] = useState('');

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const invalidate = () => qc.invalidateQueries({ queryKey: ['admin', 'property', propertyId] });

  const onFiles = (files: File[]) => {
    for (const file of files) {
      const tempId = `${file.name}-${file.size}-${images.length}-${Math.round(performance.now())}`;
      if (!ACCEPTED.includes(file.type)) {
        setPending((p) => [
          ...p,
          { tempId, previewUrl: '', error: 'Only JPEG, PNG or WebP images are allowed.' },
        ]);
        continue;
      }
      if (file.size > MAX_MB * 1024 * 1024) {
        setPending((p) => [...p, { tempId, previewUrl: '', error: `Images must be ≤ ${MAX_MB} MB.` }]);
        continue;
      }
      const previewUrl = URL.createObjectURL(file);
      setPending((p) => [...p, { tempId, previewUrl, error: null }]);
      void uploadPropertyImage(propertyId, file)
        .then((created) => {
          setImages((imgs) => [...imgs, created].sort((a, b) => a.sortOrder - b.sortOrder));
          setPending((p) => p.filter((t) => t.tempId !== tempId));
          setLive('Photo uploaded.');
          void invalidate();
        })
        .catch((e) => {
          setPending((p) =>
            p.map((t) => (t.tempId === tempId ? { ...t, error: problemMessage(e, 'Upload failed.') } : t)),
          );
        })
        .finally(() => URL.revokeObjectURL(previewUrl));
    }
  };

  const dismissPending = (tempId: string) =>
    setPending((p) => p.filter((t) => t.tempId !== tempId));

  const onDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = images.findIndex((i) => i.id === active.id);
    const newIndex = images.findIndex((i) => i.id === over.id);
    if (oldIndex < 0 || newIndex < 0) return;

    const previous = images;
    const reordered = arrayMove([...images], oldIndex, newIndex);
    setImages(reordered.map((img, i) => ({ ...img, sortOrder: i, isPrimary: i === 0 })));
    setLive(`Moved to position ${newIndex + 1} of ${images.length}.`);
    try {
      const fresh = await reorderPropertyImages(propertyId, reordered.map((i) => i.id));
      setImages([...fresh].sort((a, b) => a.sortOrder - b.sortOrder));
      void invalidate();
    } catch (e) {
      setImages(previous); // revert
      setLive(problemMessage(e, 'Reorder failed.'));
    }
  };

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleteBusy(true);
    setDeleteError(null);
    try {
      await deletePropertyImage(propertyId, deleteTarget.id);
      setImages((imgs) => {
        const next = imgs
          .filter((i) => i.id !== deleteTarget.id)
          .sort((a, b) => a.sortOrder - b.sortOrder)
          .map((img, i) => ({ ...img, isPrimary: i === 0 }));
        return next;
      });
      setLive('Photo deleted.');
      setDeleteTarget(null);
      void invalidate();
    } catch (e) {
      setDeleteError(problemMessage(e, 'Delete failed.'));
    } finally {
      setDeleteBusy(false);
    }
  };

  const total = images.length;

  return (
    <div className="md:col-span-2">
      <p className="sr-only" role="status" aria-live="polite">
        {live}
      </p>

      <Dropzone
        onFiles={onFiles}
        description={`JPEG, PNG or WebP · up to ${MAX_MB} MB each. The first photo is your cover.`}
      />

      {(total > 0 || pending.length > 0) && (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
          <SortableContext items={images.map((i) => i.id)} strategy={rectSortingStrategy}>
            <ul role="list" className="mt-4 grid grid-cols-2 gap-3 md:grid-cols-4">
              {images.map((image, index) => (
                <SortablePhotoTile
                  key={image.id}
                  image={image}
                  index={index}
                  total={total}
                  onDelete={() => setDeleteTarget(image)}
                />
              ))}
              {pending.map((t) => (
                <ImageTile
                  key={t.tempId}
                  url={t.previewUrl}
                  accessibleName="Uploading photo"
                  uploading={!t.error}
                  error={t.error}
                  onDelete={t.error ? () => dismissPending(t.tempId) : undefined}
                />
              ))}
            </ul>
          </SortableContext>
        </DndContext>
      )}

      {total === 0 && pending.length === 0 && (
        <p className="mt-3 text-sm text-muted-foreground">
          No photos yet. A listing needs at least one photo before it can be published.
        </p>
      )}

      <ConfirmActionModal
        open={deleteTarget !== null}
        title="Delete this photo?"
        description="It will be removed from the listing. This can't be undone."
        confirmLabel="Delete photo"
        busyLabel="Deleting…"
        confirmVariant="destructive"
        busy={deleteBusy}
        error={deleteError}
        onCancel={() => {
          setDeleteTarget(null);
          setDeleteError(null);
        }}
        onConfirm={confirmDelete}
      />
    </div>
  );
};
