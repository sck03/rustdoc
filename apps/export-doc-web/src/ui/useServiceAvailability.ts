import { useCallback, useEffect, useState } from "react";
import { buildServiceReadinessUrl, type ServiceAvailability } from "./serviceAvailabilityModel.ts";

const serviceCheckIntervalMs = 30_000;
const serviceCheckTimeoutMs = 4_000;

export function useServiceAvailability({
  apiBaseUrl,
  enabled,
  override,
}: {
  apiBaseUrl: string;
  enabled: boolean;
  override?: ServiceAvailability;
}) {
  const [availability, setAvailability] = useState<ServiceAvailability>(override ?? "checking");
  const [checkVersion, setCheckVersion] = useState(0);
  const retry = useCallback(() => setCheckVersion((current) => current + 1), []);

  useEffect(() => {
    if (override) {
      setAvailability(override);
      return undefined;
    }
    if (!enabled) {
      setAvailability("checking");
      return undefined;
    }

    let disposed = false;
    let activeController: AbortController | null = null;
    let latestCheckId = 0;
    setAvailability("checking");

    const checkService = async () => {
      const checkId = ++latestCheckId;
      activeController?.abort();
      const controller = new AbortController();
      activeController = controller;
      const timeoutId = window.setTimeout(() => controller.abort(), serviceCheckTimeoutMs);
      try {
        const response = await fetch(buildServiceReadinessUrl(apiBaseUrl), {
          cache: "no-store",
          headers: { Accept: "application/json" },
          signal: controller.signal,
        });
        if (!disposed && checkId === latestCheckId) {
          setAvailability(response.ok ? "available" : "unreachable");
        }
      } catch {
        if (!disposed && checkId === latestCheckId) {
          setAvailability("unreachable");
        }
      } finally {
        window.clearTimeout(timeoutId);
        if (activeController === controller) {
          activeController = null;
        }
      }
    };

    const checkWhenVisible = () => {
      if (document.visibilityState === "visible") {
        void checkService();
      }
    };

    void checkService();
    const intervalId = window.setInterval(() => void checkService(), serviceCheckIntervalMs);
    window.addEventListener("online", checkWhenVisible);
    document.addEventListener("visibilitychange", checkWhenVisible);
    return () => {
      disposed = true;
      activeController?.abort();
      window.clearInterval(intervalId);
      window.removeEventListener("online", checkWhenVisible);
      document.removeEventListener("visibilitychange", checkWhenVisible);
    };
  }, [apiBaseUrl, checkVersion, enabled, override]);

  return { availability, retry };
}
