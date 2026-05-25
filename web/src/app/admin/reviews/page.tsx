'use client';

const AdminReviewsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Reviews</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §6.2 (GET /admin/reviews/moderation,
        POST /admin/reviews/{'{id}'}/approve|reject) and §11.1 (moderation modes).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Pending review queue + approve/reject + owner-response composer.
      </div>
    </div>
  );
};

export default AdminReviewsPage;
