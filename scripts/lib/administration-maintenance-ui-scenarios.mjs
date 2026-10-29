import assert from "node:assert/strict";
import path from "node:path";

export async function runAdministrationMaintenanceUi({page,open,read,waitFor,clickText,input,audit,results,output,captureScreenshot}) {
  for (const width of [1024,390]) {
    await open("directory",width);
    assert(await read(page,"document.body.innerText.includes('工作邮箱') || document.body.innerText.includes('zhang.ning@example.test')"));
    assert.equal(await read(page,"document.body.innerText.includes('身份证') || document.body.innerText.includes('13800000000')"),false);
    assert.equal(await read(page,"window.__officeCalls.some(item=>item.name==='getPerson')"),false);
    await audit(page,`work-directory-${width}`);
    await captureScreenshot(page,path.join(output,`work-directory-${width}.png`));
  }
  await open("people",1024,"unlinked");await clickText(page,"人员档案");
  await waitFor(page,"document.querySelector('.personnel-facts')");await clickText(page,"编辑档案");
  assert.equal(await read(page,"Boolean(document.querySelector('input[name=employeeNumber]'))"),false);
  await clickText(page,"纠正登记信息（须说明原因）","label");
  await input(page,'textarea[name="correctionNote"]',"核对入职登记后纠正");
  await input(page,'input[name="employeeNumber"]',"CORRECTED-001");await input(page,'input[name="jobTitle"]',"业务主管");
  await clickText(page,"保存档案");await waitFor(page,"window.__officeCalls.some(item=>item.name==='updatePerson') && !document.querySelector('input[name=employeeNumber]')");
  const correction=await read(page,"window.__officeCalls.find(item=>item.name==='updatePerson').input.body.registration");
  assert.equal(correction.employeeNumber,"CORRECTED-001");assert.equal(correction.jobTitle,"业务主管");
  await clickText(page,"删除误录档案");await waitFor(page,"document.querySelector('textarea[name=reason]')");
  await input(page,'textarea[name="reason"]',"重复登记");
  await clickText(page,"确认删除");
  assert.equal(await read(page,"window.__officeCalls.some(item=>item.name==='deletePerson')"),false,"deletion requires explicit checkbox confirmation");
  await read(page,"document.querySelector('.office-dialog-backdrop:last-of-type input[type=checkbox]').click()");
  await audit(page,"personnel-delete-confirmation");await clickText(page,"确认删除");
  await waitFor(page,"window.__officeCalls.some(item=>item.name==='deletePerson') && !document.querySelector('.office-dialog')");
  assert.equal((await read(page,"window.__officeCalls.find(item=>item.name==='deletePerson').input.body")).expectedVersion,2);
  results.push("personnel-correction-and-confirmed-deletion");
  for (const kind of ["rooms","supplies"]) {
    await open(kind,390);await clickText(page,kind==="rooms"?"预约记录与审批":"领用记录与审批");
    await waitFor(page,"document.querySelector('.office-request-card')");
    assert.equal(await read(page,"[...document.querySelectorAll('.office-request-card button')].some(button=>button.textContent.trim()==='修改')"),false,"Submitted resource requests remain frozen");
    await clickText(page,"取消申请");await input(page,'textarea[name="note"]',"内容调整，取消后重新申请");
    await audit(page,`${kind}-request-cancel-mobile`);await clickText(page,"取消申请",".office-dialog button");
    const action=kind==="rooms"?"cancelBooking":"cancelRequest";
    await waitFor(page,`window.__officeCalls.some(item=>item.name==='${action}') && !document.querySelector('.office-dialog')`);
    const body=await read(page,`window.__officeCalls.find(item=>item.name==='${action}').input.body`);
    assert.equal(body.expectedVersion,1);assert.equal(body.note,"内容调整，取消后重新申请");
    results.push(`${kind}-frozen-request-cancel-contract`);
  }
  await open("organization",390);await read(page,"document.querySelector('[aria-label=删除业务部]').click()");
  await input(page,'textarea[name="reason"]',"测试误建目录");await read(page,"document.querySelector('.office-dialog input[type=checkbox]').click()");
  await clickText(page,"确认删除");await waitFor(page,"window.__officeCalls.some(item=>item.name==='deleteDepartment') && !document.querySelector('.office-dialog')");
  assert.equal((await read(page,"window.__officeCalls.find(item=>item.name==='deleteDepartment').input.body")).expectedVersion,1);
  results.push("organization-delete-version-contract");
}
