const PREFIX = "duam_tour_seen_";

export const TOUR_KEYS = {
  upload: "upload-page",
  jobs: "jobs-page",
} as const;

export function hasSeenTour(key: string): boolean {
  return localStorage.getItem(PREFIX + key) === "true";
}

export function markTourSeen(key: string): void {
  localStorage.setItem(PREFIX + key, "true");
}

export function resetTour(key: string): void {
  localStorage.removeItem(PREFIX + key);
}
