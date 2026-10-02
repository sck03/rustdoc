/** Sum the decimal representations exactly before returning the API's numeric DTO value. */
export function sumDecimalNumbers(values: readonly number[]): number {
  if (values.some(value => !Number.isFinite(value))) return NaN;
  const parts = values.map(value => {
    const [mantissa, exponent = "0"] = String(value).toLowerCase().split("e");
    const fractionLength = mantissa.split(".")[1]?.length ?? 0;
    return { coefficient: BigInt(mantissa.replace(".", "")), scale: fractionLength - Number(exponent) };
  });
  const scale = Math.max(0, ...parts.map(part => part.scale));
  const total = parts.reduce((sum, part) => sum + part.coefficient * 10n ** BigInt(scale - part.scale), 0n);
  return Number(`${total}e-${scale}`);
}
