import { readFileSync } from "node:fs";

export const productEditionCatalog = JSON.parse(readFileSync(new URL("../product-editions.json", import.meta.url), "utf8"));

export function normalizeProductEdition(value) {
  const requested = String(value ?? "").trim() || "Full";
  const edition = Object.keys(productEditionCatalog.editions).find((key) => key.toLowerCase() === requested.toLowerCase());
  if (!edition) throw new Error(`Unsupported product edition: ${requested}`);
  return edition;
}
