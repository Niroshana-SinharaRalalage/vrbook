/**
 * VRB-210 — scaffolding for a section whose domain UI lands in a later story.
 * The route + nav entry exist (so the shell is complete); the panel is filled by
 * the referenced story.
 */
export const SettingsPlaceholder = ({ story }: { readonly story: string }) => (
  <div className="rounded-lg border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
    This section&rsquo;s controls are delivered in <span className="font-medium">{story}</span>. The
    settings framework (validation, save/discard, audit trail) is ready for it.
  </div>
);
