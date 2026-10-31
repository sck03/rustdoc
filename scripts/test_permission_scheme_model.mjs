import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const root = path.resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const esbuild = require(path.join(root, 'apps/export-doc-web/node_modules/esbuild'));
const bundle = await esbuild.build({
  stdin: { contents: 'export * as scheme from "./apps/export-doc-web/src/features/settings/permissionSchemeModel.ts"; export * as unit from "./apps/export-doc-web/src/features/settings/unitPermissionModel.ts";', resolveDir: root },
  bundle: true, write: false, format: 'esm', platform: 'node', logLevel: 'silent',
});
const { scheme, unit } = await import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString('base64')}`);
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'crates/export-doc-contracts/src/generated_contract.json'), 'utf8')).permissions.resources;
const resource = key => catalog.find(r => r.key === key);
const invoices = { ...resource('document.invoices'), actions: [{ key: 'view', presetLevel: 'view', unitAssignable: true }, { key: 'operate', presetLevel: 'operate', unitAssignable: true }, { key: 'manage', presetLevel: 'manage', unitAssignable: true }] };
const key = action => scheme.grantKey(invoices.key, action);
const initial = { [key('view')]: 'company', [key('operate')]: 'own', [key('manage')]: 'own' };
assert.deepEqual(scheme.setResourcePreset(initial, invoices, 'operate'), { [key('view')]: 'company', [key('operate')]: 'own' }, 'reducing management preserves the restricted edit scope');
assert.deepEqual(scheme.setResourcePreset({ [key('view')]: 'company' }, invoices, 'manage'), initial, 'new actions start with own scope');
assert.deepEqual(scheme.setResourcePreset(initial, invoices, ''), {}, 'removing a preset removes its actions');
const ceilings = [{ resourceKey: invoices.key, action: 'view', dataScope: 'company' }, { resourceKey: invoices.key, action: 'operate', dataScope: 'own' }];
const editor = unit.unitPermissionEditor([invoices], ceilings);
assert.deepEqual(editor.resources[0].actions.map(a => a.key), ['view', 'operate']);
assert.deepEqual(editor.actionDataScopes[key('view')], ['own', 'department', 'company']);
assert.deepEqual(editor.actionDataScopes[key('operate')], ['own']);
assert.deepEqual(scheme.setResourcePreset({}, editor.resources[0], 'operate', editor.actionDataScopes), { [key('view')]: 'own', [key('operate')]: 'own' });
assert.equal(scheme.initialGrantScope(invoices, key('operate'), { [key('operate')]: ['department'] }), 'department', 'checkbox and preset use the same permitted default');
const full = unit.unitPermissionEditor(catalog, null);
for (const id of ['office.announcements', 'office.notifications']) {
  assert(full.resources.some(r => r.key === id));
  assert.deepEqual(full.grants({ [scheme.grantKey(id, 'view')]: 'all' }), [{ resourceKey: id, action: 'view', dataScope: 'all' }], 'non-record scope stays all within its business boundary');
}
assert(!full.resources.some(r => r.key === 'system.users'));
assert.deepEqual(full.resources.find(r => r.key === 'system.about').actions.map(a => a.key), ['view']);
const collaboration = unit.unitPermissionEditor(catalog, null, true);
assert(!collaboration.resources.some(r => r.key === 'sales.supplier-assessments'), 'business confirmation is not an OA collaboration duty');
assert(collaboration.resources.some(r => r.key === 'office.rooms'));
assert(!collaboration.collaborationScheme([{ resourceKey: 'sales.supplier-assessments', action: 'approve', dataScope: 'own' }]));
assert(collaboration.collaborationScheme([{ resourceKey: 'office.general', action: 'approve', dataScope: 'own' }]));
console.log('Permission scheme scope, unit boundary and collaboration regressions passed.');
