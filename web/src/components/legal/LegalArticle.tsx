import type { ReactNode } from 'react';

export interface LegalSection {
  readonly id: string;
  readonly heading: string;
  readonly body: ReactNode;
}

interface LegalArticleProps {
  readonly title: string;
  readonly updated: string;
  readonly intro?: ReactNode;
  readonly sections: readonly LegalSection[];
}

/**
 * VRB-311 — consistent legal-page shell: an OWNER-PENDING placeholder banner
 * (the copy is VrBook-drafted and awaits owner legal review before prod), an
 * in-page table of contents, and anchored sections with a readable measure.
 */
export const LegalArticle = ({ title, updated, intro, sections }: LegalArticleProps) => (
  <article className="space-y-8">
    <div
      role="note"
      className="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-700 dark:text-amber-300"
    >
      <strong>Placeholder — pending owner legal review.</strong> This is VrBook-drafted boilerplate for
      layout and completeness; it is not final legal text and must be reviewed and approved before
      production launch (VRB-310).
    </div>

    <header className="space-y-1">
      <h1 className="text-3xl font-semibold tracking-tight">{title}</h1>
      <p className="text-sm text-muted-foreground">Last updated: {updated}</p>
    </header>

    {intro && <div className="text-sm leading-relaxed text-muted-foreground">{intro}</div>}

    <nav aria-label="On this page" className="rounded-lg border border-border bg-muted/30 p-4">
      <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">On this page</p>
      <ol className="space-y-1 text-sm">
        {sections.map((s) => (
          <li key={s.id}>
            <a href={`#${s.id}`} className="text-primary underline-offset-4 hover:underline">
              {s.heading}
            </a>
          </li>
        ))}
      </ol>
    </nav>

    <div className="space-y-10">
      {sections.map((s, i) => (
        <section key={s.id} id={s.id} className="scroll-mt-24 space-y-3">
          <h2 className="text-xl font-medium">
            {i + 1}. {s.heading}
          </h2>
          <div className="space-y-3 text-sm leading-relaxed text-muted-foreground">{s.body}</div>
        </section>
      ))}
    </div>
  </article>
);
