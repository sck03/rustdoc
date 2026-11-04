import { useLayoutEffect, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { Button } from "../../src/ui/Button.tsx";
import { Field } from "../../src/ui/primitives/Field.tsx";
import { Tabs } from "../../src/ui/primitives/Tabs.tsx";
import { useScopedSave } from "../../src/ui/useScopedSave.ts";

/** Exercises the real React controls, including late completion after cancellation. */
export async function runSharedControlContracts() {
  const failures: string[] = [];
  let checks = 0;
  const check = (value: boolean, message: string) => { checks++; if (!value) failures.push(message); };
  const previousFocus = document.activeElement;
  const mount = document.createElement("div");
  mount.style.cssText = "position:fixed;inset:0;max-width:720px;overflow:auto;background:white;z-index:1";
  document.body.append(mount);
  const root = createRoot(mount);
  let mounted = true;
  let controls!: ReturnType<typeof useScopedSave>;
  let clicks = 0;
  const owner = {};
  function Fixture({ scope, owner, busy = false, selection }: { scope: string; owner: unknown; busy?: boolean; selection?: string }) {
    const boundary = useScopedSave(scope, owner);
    const [selected, setSelected] = useState("first");
    useLayoutEffect(() => { controls = boundary; });
    return <>
      <p id="shared-control-extra">原有说明</p>
      <Field label="字段" required error="字段错误"><input id="shared-control-field" required aria-describedby="shared-control-extra" /></Field>
      <Field label="说明" description="字段说明">{hint => <input id="shared-control-described" aria-describedby={hint} />}</Field>
      <Field label="只读"><input readOnly defaultValue="只读内容" /></Field>
      <Button busy={busy} onClick={() => clicks++} data-control="saving">保存</Button>
      <Button data-control="zero">{0}</Button>
      <Tabs label="测试分区" idPrefix="shared-control" value={selection ?? selected} onChange={setSelected}
        items={[{ key: "first", label: "第一项" }, { key: "disabled", label: "停用项", disabled: true }, { key: "last", label: "末项" }]}>
        当前内容
      </Tabs>
    </>;
  }
  const render = (scope = "original", currentOwner: unknown = owner, busy = false, selection?: string) =>
    flushSync(() => root.render(<Fixture scope={scope} owner={currentOwner} busy={busy} selection={selection} />));
  function pendingSave() {
    let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    const result = controls.run(() => gate).then(() => "success", error => error.name);
    return { release, result };
  }
  try {
    render();
    const input = mount.querySelector<HTMLInputElement>("#shared-control-field")!;
    const ids = input.getAttribute("aria-describedby")!.split(/\s+/);
    check(ids.includes("shared-control-extra") && ids.some(id => document.getElementById(id)?.textContent === "字段错误"), "错误说明和原有说明必须同时关联");
    check(input.getAttribute("aria-invalid") === "true" && input.getAttribute("aria-required") === "true", "必填和错误状态必须公开");
    const describedIds = mount.querySelector("#shared-control-described")!.getAttribute("aria-describedby")!.split(/\s+/);
    check(describedIds.length === new Set(describedIds).size, "说明编号不得重复");
    check(mount.querySelector('[data-control="zero"]')!.textContent === "0", "数值零标签必须显示");
    const button = mount.querySelector<HTMLButtonElement>('[data-control="saving"]')!;
    const width = button.getBoundingClientRect().width;
    const background = getComputedStyle(button).backgroundColor;
    render("original", owner, true);
    const loading = mount.querySelector<HTMLButtonElement>('[data-control="saving"]')!;
    loading.click();
    check(loading.disabled && loading.getAttribute("aria-busy") === "true" && clicks === 0, "加载动作必须阻止再次提交");
    check(Math.abs(loading.getBoundingClientRect().width - width) < 0.1 && getComputedStyle(loading).backgroundColor === background, "加载状态必须保留宽度和底色");
    render();
    const first = mount.querySelector<HTMLButtonElement>("#shared-control-tab-first")!;
    flushSync(() => first.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })));
    check(document.activeElement?.id === "shared-control-tab-last" && mount.querySelector("#shared-control-tab-last")!.getAttribute("aria-selected") === "true", "方向键必须跳过停用页签并选中");
    for (const [key, target] of [["Home", "first"], ["End", "last"], ["ArrowRight", "first"], ["ArrowLeft", "last"]]) {
      flushSync(() => document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
      check(document.activeElement?.id === `shared-control-tab-${target}`, `${key} 必须移动到正确的可用页签`);
    }
    const minimum = matchMedia("(max-width:680px)").matches ? 44 : document.documentElement.dataset.interfaceDensity === "compact" ? 32 : 36;
    check(parseFloat(getComputedStyle(first).minHeight) === minimum, "页签必须遵守密度和触控高度");
    render("original", owner, false, "disabled");
    check(mount.querySelector("#shared-control-tab-first")!.getAttribute("tabindex") === "0", "当前项停用后仍须有键盘入口");
    render("original", owner, false, "removed");
    check(mount.querySelector("#shared-control-tab-first")!.getAttribute("tabindex") === "0" && mount.querySelector('[role="tabpanel"]')!.getAttribute("aria-label") === "测试分区", "当前项移除后须保留键盘入口和面板名称");

    render();
    let pending = pendingSave();
    controls.cancel(); pending.release();
    check(await pending.result === "AbortError", "显式取消后的迟到成功不得返回成功");
    pending = pendingSave();
    render("another"); pending.release();
    check(await pending.result === "AbortError", "切换记录后的迟到成功不得返回成功");
    pending = pendingSave();
    render("another", {}); pending.release();
    check(await pending.result === "AbortError", "切换会话后的迟到成功不得返回成功");
    render(); pending = pendingSave();
    let duplicateCalls = 0;
    const duplicate = await controls.run(async () => { duplicateCalls++; }).then(() => "success", error => error.name);
    check(duplicate === "AbortError" && duplicateCalls === 0, "被拦下的重复保存不得触发成功回调");
    pending.release(); check(await pending.result === "success", "正常保存必须成功");
    await controls.run(async ({ continueInScope }) => { continueInScope("saved"); render("saved"); });
    check(true, "新建编号衔接必须保留当前保存");
    pending = pendingSave();
    flushSync(() => root.unmount()); mounted = false; pending.release();
    check(await pending.result === "AbortError", "卸载后的迟到成功不得返回成功");
  } finally {
    if (mounted) flushSync(() => root.unmount());
    mount.remove();
    if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus({ preventScroll: true });
  }
  return { passed: failures.length === 0, checks, failures };
}
