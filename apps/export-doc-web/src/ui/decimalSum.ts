/** Sum the decimal representations exactly before returning the API's numeric DTO value. */
export function sumDecimalNumbers(values: readonly number[]): number {
  if (values.some(value => !Number.isFinite(value))) return NaN;
  return Number(sumDecimalStrings(values.map(String)));
}

/** Preserve decimal input strings, including optional invoice settlement lines. */
export function sumDecimalStrings(values: readonly string[]): string {
  const parts = values.map(value => {
    if (value.length > 256 || !/^[+-]?\d+(?:\.\d+)?(?:e[+-]?\d{1,3})?$/i.test(value)) throw new Error("金额不是有效小数。");
    const [mantissa, exponent = "0"] = String(value).toLowerCase().split("e");
    const fractionLength = mantissa.split(".")[1]?.length ?? 0;
    return { coefficient: BigInt(mantissa.replace(".", "")), scale: fractionLength - Number(exponent) };
  });
  const scale = Math.max(0, ...parts.map(part => part.scale));
  const total = parts.reduce((sum, part) => sum + part.coefficient * 10n ** BigInt(scale - part.scale), 0n);
  const sign = total < 0n ? "-" : "";
  const digits = (total < 0n ? -total : total).toString().padStart(scale + 1, "0");
  return sign + (scale ? `${digits.slice(0, -scale)}.${digits.slice(-scale)}` : digits);
}
