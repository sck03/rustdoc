export const exchangeRateSources = [
  { value: "BankOfChinaHtml", label: "中国银行", url: "https://www.boc.cn/sourcedb/whpj/" },
  { value: "Icbc", label: "中国工商银行", url: "https://www.icbc.com.cn/column/1438058341489590354.html" },
  { value: "Cmb", label: "招商银行", url: "https://fx.cmbchina.com/hq/" },
] as const;

export function exchangeRateSourceName(format?: string, url?: string | null) {
  return exchangeRateSources.find(source => source.value === format && source.url.replace(/\/$/, "") === url?.replace(/\/$/, ""))?.label ?? url ?? "未配置";
}
