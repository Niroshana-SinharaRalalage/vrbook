const Loading = () => {
  return (
    <div className="container py-10">
      <div className="space-y-6">
        <div className="h-8 w-1/2 animate-pulse rounded bg-muted" />
        <div className="aspect-[2/1] animate-pulse rounded-xl bg-muted" />
        <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
          <div className="space-y-3 md:col-span-2">
            <div className="h-4 w-3/4 animate-pulse rounded bg-muted" />
            <div className="h-4 w-2/3 animate-pulse rounded bg-muted" />
            <div className="h-4 w-full animate-pulse rounded bg-muted" />
          </div>
          <div className="h-64 animate-pulse rounded-xl bg-muted" />
        </div>
      </div>
    </div>
  );
};

export default Loading;
