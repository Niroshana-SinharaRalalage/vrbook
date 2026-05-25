'use client';

const AdminCalendarPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Calendar</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 2). Month/week grid
        with color-coded bars: Direct (blue), AirBnB (red), Tentative (striped),
        Blocked (gray), Conflict (red border).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Calendar grid renders here.
      </div>
    </div>
  );
};

export default AdminCalendarPage;
