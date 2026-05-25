'use client';

const AccountMessagesPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Messages</h1>
      <p className="text-sm text-muted-foreground">
        Thread list + active conversation pane — implemented by Agent F1.
        Real-time via SignalR (proposal §10), persisted via GET /threads &amp;
        POST /threads/{'{id}'}/messages.
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Two-pane messaging UI lands here.
      </div>
    </div>
  );
};

export default AccountMessagesPage;
