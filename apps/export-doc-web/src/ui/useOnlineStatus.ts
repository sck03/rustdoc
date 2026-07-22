import { useEffect, useState } from "react";

export function useOnlineStatus(override?: "online" | "offline") {
  const [isOnline, setIsOnline] = useState(() => override ? override === "online" : readBrowserOnlineStatus());

  useEffect(() => {
    if (override) {
      setIsOnline(override === "online");
      return undefined;
    }

    const updateStatus = () => setIsOnline(readBrowserOnlineStatus());
    window.addEventListener("online", updateStatus);
    window.addEventListener("offline", updateStatus);
    updateStatus();
    return () => {
      window.removeEventListener("online", updateStatus);
      window.removeEventListener("offline", updateStatus);
    };
  }, [override]);

  return isOnline;
}

function readBrowserOnlineStatus() {
  return typeof navigator === "undefined" || navigator.onLine;
}
