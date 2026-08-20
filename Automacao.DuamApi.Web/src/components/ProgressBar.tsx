export function ProgressBar({
  processadas,
  total,
  failed = false,
  size = "sm",
  showLabel = true,
}: {
  processadas: number;
  total: number | null;
  failed?: boolean;
  size?: "sm" | "lg";
  showLabel?: boolean;
}) {
  const percent = total && total > 0 ? Math.min(100, (processadas / total) * 100) : 0;

  return (
    <div className={size === "lg" ? undefined : "progress-row"}>
      <div className={`progress-track${size === "lg" ? " lg" : ""}`}>
        <div
          className={`progress-fill${failed ? " failed" : ""}`}
          style={{ width: `${percent}%` }}
        />
      </div>
      {showLabel && (
        <div className="progress-label">
          {processadas}/{total ?? "?"}
        </div>
      )}
    </div>
  );
}
