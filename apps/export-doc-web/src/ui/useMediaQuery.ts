import { useEffect, useState } from "react";

export function useMediaQuery(query: string) {
  const [matches, setMatches] = useState(() => readMatches(query));

  useEffect(() => {
    if (typeof window === "undefined") {
      return undefined;
    }

    const mediaQuery = window.matchMedia(query);
    const handleChange = () => setMatches(mediaQuery.matches);
    handleChange();
    mediaQuery.addEventListener("change", handleChange);
    return () => mediaQuery.removeEventListener("change", handleChange);
  }, [query]);

  return matches;
}

function readMatches(query: string) {
  return typeof window !== "undefined" && window.matchMedia(query).matches;
}
