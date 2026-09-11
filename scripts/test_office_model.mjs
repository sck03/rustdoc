import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(repo, ".codex-runtime/office-model-tests");
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "model.mjs");
await require("esbuild").build({ stdin: { contents: `
  export * as office from ${source("features/office/officeModel.ts")};
  export * as personnel from ${source("features/office/personnelModel.ts")};
  export * as organization from ${source("features/organization/organizationModel.ts")};
  export * as navigation from ${source("app/workspaceNavigation.ts")};
  export { getDefaultWorkspaceRoute } from ${source("app/productEdition.ts")};
  export { isRouteAccessAllowed } from ${source("app/routeAccess.ts")};
  export { createRequestKey } from ${source("ui/createRequestKey.ts")};
`, loader: "ts", resolveDir: web }, bundle: true, platform: "node", format: "esm", outfile: bundle, logLevel: "silent" });
const { office, personnel, organization, navigation, getDefaultWorkspaceRoute, isRouteAccessAllowed, createRequestKey } = await import(pathToFileURL(bundle).href);
const grants = scope => ["office.rooms", "office.supplies"].flatMap(resourceKey =>
  ["view", "create", "cancel", "approve", "issue", "return", "restock", "manage"].map(action => ({ resourceKey, action, dataScope: scope })));
const user = { id: 1, companyScope: "C1", departmentId: "D1", businessDate: "2026-09-07",
  capabilities: { enabledModules: ["office.rooms", "office.supplies", "system.about"], permissions: grants("own"), productEdition: "Full" } };
const row = { id: 7, meetingRoomId: 2, ownerUserId: 2, departmentId: "D2", status: "Pending", requiresKey: true,
  startsAt: "2026-09-07T08:00:00Z", endsAt: "2026-09-07T09:00:00Z" };
assert.deepEqual(office.officeRequestActions(row, user, "rooms"), [], "viewing a record does not allow operations beyond own scope");
const manager = { ...user, capabilities: { ...user.capabilities, permissions: grants("company") } };
assert.deepEqual(office.officeRequestActions(row, manager, "rooms").map(entry => entry.action), ["approve", "reject", "cancel"]);
assert(!office.officeRequestActions({ ...row, ownerUserId: 1 }, manager, "rooms").some(entry => entry.action === "approve"), "self approval must not be offered");
assert(!office.officeAccess({ ...user, capabilities: { permissions: grants("unknown") } }, "rooms").allows("approve"));
assert.equal(office.officeStatus({ ...row, status: "InUse" }, user.businessDate, Date.parse(row.endsAt)), "超时未归还");
assert.equal(office.officeStatus({ status: "Issued", isReturnable: true, returnDueDate: "2026-09-06", quantity: 2, returnedQuantity: 1 }, user.businessDate), "逾期未归还");
assert.equal(office.officeStatus({ status: "Issued", isReturnable: true, returnDueDate: "2026-09-08", quantity: 2, returnedQuantity: 1 }, user.businessDate), "部分归还");
assert.deepEqual(office.officeDayRange("2026-09-08", "Asia/Shanghai"), { from: "2026-09-07T16:00:00.000Z", to: "2026-09-08T16:00:00.000Z" });
for (const [day, hours] of [["2026-03-08", 23], ["2026-11-01", 25]]) {
  const range = office.officeDayRange(day, "America/New_York");
  assert.equal((Date.parse(range.to) - Date.parse(range.from)) / 3600000, hours, "calendar days must honor daylight saving time");
}
assert.equal(office.officeDayRange("2026-02-30", "Asia/Shanghai"), null);
assert.equal(office.shiftOfficeBookingEnd("2026-03-08T02:30", "America/New_York"), "");
assert.equal(office.shiftOfficeBookingEnd("2026-09-07T10:30", "Asia/Shanghai"), "2026-09-07T11:30");
assert.equal(getDefaultWorkspaceRoute(user.capabilities), "/office/meeting-rooms", "office-only employees need a usable landing page");
assert(navigation.filterWorkspaceNavGroups(user.capabilities).some(group => group.key === "office"));
assert(!navigation.filterWorkspaceNavGroups({ ...user.capabilities, isDesktopRuntime: true }).some(group => group.key === "office"));
for (const pathname of ["/office/meeting-rooms", "/office/supplies"]) {
  const args = { pathname, user, canManageSystem: false, isFullEdition: true, isDesktopRuntime: false };
  assert(isRouteAccessAllowed(args));
  assert(!isRouteAccessAllowed({ ...args, isDesktopRuntime: true }), "desktop deep links must be denied even with a stale capability snapshot");
  assert(!isRouteAccessAllowed({ ...args, user: { ...user, capabilities: { ...user.capabilities, enabledModules: [] } } }));
}
const descriptor = Object.getOwnPropertyDescriptor(globalThis, "crypto");
const personnelUser = { ...user, capabilities: { ...user.capabilities, enabledModules: ["office.people"], permissions: [{resourceKey:"office.people",action:"view",dataScope:"company"}] } };
assert.equal(getDefaultWorkspaceRoute(personnelUser.capabilities), "/office/directory");
assert(isRouteAccessAllowed({ pathname:"/office/directory",user:personnelUser,canManageSystem:false,isFullEdition:false,isDesktopRuntime:false }));
assert(!isRouteAccessAllowed({ pathname:"/office/people",user:personnelUser,canManageSystem:false,isFullEdition:false,isDesktopRuntime:false }));
assert(!isRouteAccessAllowed({ pathname:"/office/people",user:personnelUser,canManageSystem:true,isFullEdition:true,isDesktopRuntime:true }));
assert(!personnel.canViewPersonnelDetails(personnelUser));
assert(personnel.canViewPersonnelDetails({ ...personnelUser, capabilities:{permissions:[{resourceKey:"office.people",action:"view-details",dataScope:"department"}]} }));
assert.deepEqual(personnel.personnelWorkflows({ canTransition:false,employee:{status:"Active"} }), []);
assert.deepEqual(personnel.personnelWorkflows({ canTransition:true,employee:{status:"Departed"} }), ["rehire"]);
assert.deepEqual(personnel.personnelReminders({ employee:{status:"Probation"},profile:{identityLongTerm:false,identityValidUntil:null},probationEndsOn:"2026-09-07",contractEndsOn:"2026-10-08" },"2026-09-07"), ["试用期即将到期：2026-09-07"]);
assert.deepEqual(personnel.personnelReminders({employee:{status:"Active"},profile:{identityLongTerm:false,identityValidUntil:"2026-09-08"}},"2026-09-07"),["身份证即将到期：2026-09-08"]);
const identityForm = new FormData();
identityForm.set("identityNumber","11010519491231002x");identityForm.set("identityLongTerm","on");identityForm.set("identityValidUntil","2030-01-01");
assert.equal(personnel.readPersonnelProfile(identityForm).identityNumber,"11010519491231002X");
assert.equal(personnel.readPersonnelProfile(identityForm).identityValidUntil,null);
const departments=[{code:"ROOT",name:"总部",isActive:true,managerName:""},{code:"SALES",name:"销售部",parentCode:"ROOT",isActive:true,managerName:"张宁"},
  {code:"TEAM",name:"一组",parentCode:"SALES",isActive:true,managerName:""}];
assert.equal(organization.departmentOptions(departments).find(item=>item.code==="TEAM").label,"总部 / 销售部 / 一组");
assert.deepEqual(organization.parentDepartmentOptions(departments,"SALES").map(item=>item.code),["ROOT"]);
assert.deepEqual(organization.filterDepartmentTree(departments,"一组").map(item=>item.code),["ROOT","SALES","TEAM"]);
assert.deepEqual(organization.filterDepartmentTree(departments,"张宁").map(item=>item.code),["ROOT","SALES"]);
assert.deepEqual(organization.buildDepartmentTreeRows(departments,"",1,{}).map(item=>item.code),["ROOT","SALES"]);
assert.deepEqual(organization.buildDepartmentTreeRows(departments,"",0,{}).map(item=>item.code),["ROOT"]);
assert.deepEqual(organization.buildDepartmentTreeRows(departments,"",Infinity,{}).map(item=>item.code),["ROOT","SALES","TEAM"]);
assert.deepEqual(organization.buildDepartmentTreeRows(departments,"",Infinity,{ROOT:false}).map(item=>item.code),["ROOT"]);
assert.deepEqual(organization.buildDepartmentTreeRows(departments,"一组",0,{ROOT:false}).map(item=>item.code),["ROOT","SALES","TEAM"]);
const deepDepartments=Array.from({length:32},(_,index)=>({code:`D${index}`,name:`部门${index}`,parentCode:index?`D${index-1}`:null,isActive:true,managerName:""}));
const deepMatch=organization.buildDepartmentTreeRows(deepDepartments,"部门31",1,{});
assert.equal(deepMatch.length,32);assert.equal(deepMatch.at(-1).depth,31);assert.equal(deepMatch.at(-1).ancestors.length,31);
assert.throws(()=>organization.departmentOptions([{code:"A",name:"A",parentCode:"B"},{code:"B",name:"B",parentCode:"A"}]),/循环/);
assert.deepEqual(office.officeRequestFocus(new URLSearchParams("requestId=15&applicantUserId=7&employeeId=5")), {requestId:15,applicantUserId:7,employeeId:5});
assert.deepEqual(office.officeRequestFocus(new URLSearchParams("requestId=-1&applicantUserId=2147483648&employeeId=0")), {requestId:undefined,applicantUserId:undefined,employeeId:undefined});
const registerUser = { ...manager, capabilities: { ...manager.capabilities, productEdition:"Administration", usesOfficeRegister:true,
  canManageSettings:true, canManageUsers:true, enabledModules:["office.rooms","office.supplies","office.people","system.about"],
  permissions:[...grants("all"), ...["view","view-details"].map(action=>({resourceKey:"office.people",action,dataScope:"all"}))] } };
assert.equal(getDefaultWorkspaceRoute(registerUser.capabilities), "/office/people");
assert.equal(navigation.filterWorkspaceNavGroups({ ...registerUser.capabilities, isDesktopRuntime:true }).find(group=>group.key==='office').items.length,4);
const fullRegisterUser = { ...registerUser, capabilities: { ...registerUser.capabilities, productEdition: "Full", canUseDocumentWorkspace: true, canUseSalesWorkspace: true } };
for (const pathname of ["/office/people", "/office/meeting-rooms", "/office/supplies"]) {
  assert(isRouteAccessAllowed({ pathname, user: fullRegisterUser, canManageSystem: true, isDesktopRuntime: true }), "Full desktop permits direct administration routes");
  assert(!isRouteAccessAllowed({ pathname, user: { ...fullRegisterUser, capabilities: { ...fullRegisterUser.capabilities, enabledModules: [], permissions: [] } }, canManageSystem: true, isDesktopRuntime: true }), "edition availability never overrides missing grants");
}
for (const pathname of ["/office/people", "/office/meeting-rooms", "/office/supplies", "/system/access-control", "/system/organization"]) {
  assert(isRouteAccessAllowed({ pathname, user:registerUser, canManageSystem:true, isDesktopRuntime:true }));
}
assert(!isRouteAccessAllowed({pathname:"/system/organization",user:personnelUser,canManageSystem:false,isDesktopRuntime:false}));
assert(navigation.searchWorkspaceNavGroups("部门",navigation.filterWorkspaceNavGroups(registerUser.capabilities)).some(group=>group.items.some(item=>item.to==="/system/organization")));
assert(!isRouteAccessAllowed({pathname:"/invoices",user:registerUser,canManageSystem:true,isDesktopRuntime:true}));
assert.equal(office.officeRequestActions({...row,status:"Approved"},registerUser,"rooms").find(item=>item.action==='cancel').label,"取消登记");
const crypto = globalThis.crypto;
try {
  Object.defineProperty(globalThis, "crypto", { configurable: true, value: { getRandomValues: crypto.getRandomValues.bind(crypto) } });
  const keys = Array.from({ length: 32 }, createRequestKey);
  assert.equal(new Set(keys).size, keys.length);
  assert(keys.every(key => /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(key)));
} finally { Object.defineProperty(globalThis, "crypto", descriptor); }
process.stdout.write("Office models, permissions, business time and intranet request keys passed.\n");
