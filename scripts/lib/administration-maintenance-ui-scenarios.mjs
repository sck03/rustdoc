import assert from "node:assert/strict";
import path from "node:path";

export async function runAdministrationMaintenanceUi({page,open,read,waitFor,clickText,input,audit,results,output,captureScreenshot}) {
  for (const width of [1024,390]) {
    await open("directory",width,"register");
    assert(await read(page,"document.body.innerText.includes('工作邮箱') || document.body.innerText.includes('zhang.ning@example.test')"));
    assert.equal(await read(page,"document.body.innerText.includes('身份证') || document.body.innerText.includes('13800000000')"),false);
    assert.equal(await read(page,"window.__officeCalls.some(item=>item.name==='getPerson')"),false);
    await audit(page,`work-directory-${width}`);
    await captureScreenshot(page,path.join(output,`work-directory-${width}.png`));
  }
  await open("people",1024,"register");await clickText(page,"人员档案");
  await waitFor(page,"document.querySelector('.personnel-facts')");await clickText(page,"编辑档案");
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
    await open(kind,390,"register");await clickText(page,kind==="rooms"?"预约与钥匙交接记录":"领用与归还记录");
    await waitFor(page,"document.querySelector('.office-request-card')");await clickText(page,"修改");
    if(kind==="rooms")await input(page,'input[name="title"]',"修正后的会议");
    else await input(page,'input[name="quantity"]',"3");
    await audit(page,`${kind}-request-edit-mobile`);await clickText(page,"保存修改");
    const action=kind==="rooms"?"updateBooking":"updateRequest";
    await waitFor(page,`window.__officeCalls.some(item=>item.name==='${action}') && !document.querySelector('.office-dialog')`);
    const body=await read(page,`window.__officeCalls.find(item=>item.name==='${action}').input.body`);
    assert.equal(body.expectedVersion,1);assert.equal(kind==="rooms"?body.title:body.quantity,kind==="rooms"?"修正后的会议":3);
    results.push(`${kind}-request-edit-contract`);
  }
  await open("organization",390);await read(page,"document.querySelector('[aria-label=删除业务部]').click()");
  await input(page,'textarea[name="reason"]',"测试误建目录");await read(page,"document.querySelector('.office-dialog input[type=checkbox]').click()");
  await clickText(page,"确认删除");await waitFor(page,"window.__officeCalls.some(item=>item.name==='deleteDepartment') && !document.querySelector('.office-dialog')");
  assert.equal((await read(page,"window.__officeCalls.find(item=>item.name==='deleteDepartment').input.body")).expectedVersion,1);
  results.push("organization-delete-version-contract");
}
