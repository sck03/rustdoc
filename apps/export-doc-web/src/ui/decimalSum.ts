/** Exact arithmetic on decimal text; convert to number only at the numeric DTO boundary. */
export class ExactDecimal {
  private static readonly zero = new ExactDecimal(0n, 0);
  private constructor(private readonly coefficient: bigint, private readonly scale: number) {}

  static from(value: number | string): ExactDecimal {
    if (value === 0 || value === "0") return ExactDecimal.zero;
    const text = String(value);
    if (text.length > 256 || !/^[+-]?\d+(?:\.\d+)?(?:e[+-]?\d{1,3})?$/i.test(text)) throw new Error("金额不是有效小数。");
    const [mantissa, exponent = "0"] = text.toLowerCase().split("e");
    const scale = (mantissa.split(".")[1]?.length ?? 0) - Number(exponent);
    const coefficient = BigInt(mantissa.replace(".", ""));
    return scale < 0 ? new ExactDecimal(coefficient * power10(-scale), 0) : new ExactDecimal(coefficient, scale);
  }

  add(value: ExactDecimal | number | string): ExactDecimal {
    const other = ExactDecimal.resolve(value);
    const scale = Math.max(this.scale, other.scale);
    if (other.coefficient === 0n && other.scale <= this.scale) return this;
    if (this.coefficient === 0n && this.scale <= other.scale) return other;
    return new ExactDecimal(this.atScale(scale) + other.atScale(scale), scale);
  }

  subtract(value: ExactDecimal | number | string): ExactDecimal {
    const other = ExactDecimal.resolve(value);
    return this.add(new ExactDecimal(-other.coefficient, other.scale));
  }

  multiply(value: ExactDecimal | number | string): ExactDecimal {
    const other = ExactDecimal.resolve(value);
    return ExactDecimal.result(this.coefficient * other.coefficient, this.scale + other.scale);
  }

  divide(value: ExactDecimal | number | string, digits = 28): ExactDecimal {
    checkDigits(digits);
    const other = ExactDecimal.resolve(value);
    if (other.coefficient === 0n) throw new Error("计算时不能除以零。");
    if (this.coefficient === 0n) return ExactDecimal.zero;
    const shift = other.scale + digits - this.scale;
    const numerator = this.coefficient * power10(Math.max(0, shift));
    const denominator = other.coefficient * power10(Math.max(0, -shift));
    return ExactDecimal.result(roundedQuotient(numerator, denominator), digits);
  }

  round(digits: number): ExactDecimal {
    checkDigits(digits);
    return this.scale <= digits ? this : new ExactDecimal(roundedQuotient(this.coefficient, power10(this.scale - digits)), digits);
  }

  ceil(): number {
    const divisor = power10(this.scale);
    return Number(this.coefficient / divisor + (this.coefficient > 0n && this.coefficient % divisor !== 0n ? 1n : 0n));
  }

  toNumber(): number { return Number(this.toString()); }

  toString(): string {
    const sign = this.coefficient < 0n ? "-" : "";
    const digits = (this.coefficient < 0n ? -this.coefficient : this.coefficient).toString().padStart(this.scale + 1, "0");
    return sign + (this.scale ? `${digits.slice(0, -this.scale)}.${digits.slice(-this.scale)}` : digits);
  }

  private atScale(scale: number): bigint { return scale === this.scale ? this.coefficient : this.coefficient * power10(scale - this.scale); }
  private static resolve(value: ExactDecimal | number | string): ExactDecimal { return value instanceof ExactDecimal ? value : ExactDecimal.from(value); }
  private static result(coefficient: bigint, scale: number): ExactDecimal {
    if (coefficient === 0n) return ExactDecimal.zero;
    while (scale > 0 && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
    return new ExactDecimal(coefficient, scale);
  }
}

const powers = new Map<number, bigint>([[0, 1n]]);
function power10(exponent: number): bigint {
  const cached = powers.get(exponent);
  if (cached !== undefined) return cached;
  const value = 10n ** BigInt(exponent);
  if (exponent <= 128) powers.set(exponent, value);
  return value;
}

function checkDigits(digits: number) {
  if (!Number.isInteger(digits) || digits < 0 || digits > 28) throw new Error("小数位数无效。");
}

function roundedQuotient(numerator: bigint, denominator: bigint): bigint {
  const sign = (numerator < 0n) !== (denominator < 0n) ? -1n : 1n;
  const positive = numerator < 0n ? -numerator : numerator;
  const divisor = denominator < 0n ? -denominator : denominator;
  return sign * (positive / divisor + (positive % divisor * 2n >= divisor ? 1n : 0n));
}

/** Sum the decimal representations exactly before returning the API's numeric DTO value. */
export function sumDecimalNumbers(values: readonly number[]): number {
  if (values.some(value => !Number.isFinite(value))) return NaN;
  return Number(sumDecimalStrings(values.map(String)));
}

/** Preserve decimal input strings, including optional invoice settlement lines. */
export function sumDecimalStrings(values: readonly string[]): string {
  return values.reduce((sum, value) => sum.add(value), ExactDecimal.from(0)).toString();
}
