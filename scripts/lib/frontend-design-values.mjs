import { createRequire } from "node:module";
const require = createRequire(new URL("../../apps/export-doc-web/package.json", import.meta.url));
const postcss = require("postcss");

export function validateDesignValues(sources) {
  const failures = [];
  const rules = {
    "font-size": /^(?:inherit|var\(--edm-(?:font-size-(?:xs|sm|md|lg|xl|2xl)|body-size)\))$/u,
    "font-weight": /^(?:inherit|400|500|700|var\(--edm-font-weight-(?:regular|medium|bold)\))$/u,
    "line-height": /^(?:inherit|1|var\(--edm-(?:line-height-(?:xs|sm|md|lg|xl|2xl)|body-line-height)\))$/u,
    "z-index": /^(?:auto|var\(--edm-z-[a-z-]+\))$/u,
    "border-radius": /^(?:inherit|(?:0|var\(--edm-radius-(?:xs|sm|md|lg|xl|2xl|full)\))(?: (?:0|var\(--edm-radius-(?:xs|sm|md|lg|xl|2xl|full)\)))*)$/u,
  };
  const tokenValues = new Map();
  const declaredTokens = new Set();
  const tokenReferences = [];
  for (const {file, source} of sources) {
    const sheet=postcss.parse(source, {from:file});
    sheet.walkAtRules("media",rule=>{
      for (const [,width] of rule.params.matchAll(/(?:min|max)-width:\s*(\d+(?:\.\d+)?)px/gu))
        if (![680,681,1180,1181].includes(Number(width))) failures.push(`${file}:${rule.source.start.line}: 全局宽度断点须使用 680/1180：${rule.params}`);
      const min=rule.params.match(/min-width:\s*(\d+)px/u), max=rule.params.match(/max-width:\s*(\d+)px/u);
      if(min&&max&&Number(min[1])>Number(max[1])) failures.push(`${file}:${rule.source.start.line}: 响应式区间为空：${rule.params}`);
    });
    sheet.walkDecls(decl => {
      const fail = message => failures.push(`${file}:${decl.source.start.line}: ${message}`);
      if (decl.prop.startsWith("--edm-")) declaredTokens.add(decl.prop);
      for (const [,token] of decl.value.matchAll(/var\(\s*(--edm-[\w-]+)/gu)) {
        tokenReferences.push({token, fail});
      }
      if (/[/\\]tokens[/\\]/u.test(file)) {
        tokenValues.set(decl.prop, decl.value);
        if (decl.prop.startsWith("--edm-font-size-") && !/^(?:12|13|14|16|20|26)px$/u.test(decl.value)) fail(`字号令牌不在六档字阶：${decl.value}`);
        return;
      }
      if (rules[decl.prop] && !rules[decl.prop].test(decl.value)) fail(`${decl.prop} 必须使用统一取值：${decl.value}`);
      if (/^(?:margin|padding|gap|row-gap|column-gap)(?:-|$)/u.test(decl.prop)) {
        for (const match of decl.value.matchAll(/(-?\d+(?:\.\d+)?)px\b/gu)) {
          const size=Math.abs(Number(match[1]));
          if(size!==1&&size!==7&&size%4!==0) fail(`间距须为 4px 的倍数（1px/7px 边界除外）：${decl.value}`);
        }
      }
    });
  }
  for (const {token, fail} of tokenReferences) {
    if (!declaredTokens.has(token)) fail(`未声明的设计令牌：${token}`);
  }
  const levels=[...tokenValues].filter(([key])=>key.startsWith("--edm-z-"));
  if(new Set(levels.map(([,value])=>value)).size>8) failures.push("层级令牌超过八档。");
  return failures;
}
