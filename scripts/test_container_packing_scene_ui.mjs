import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(repo, 'artifacts/container-packing-scene-ui');
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const bundle = path.join(output, 'app.js');
const three = require.resolve('three').replaceAll('\\', '/').replace('/build/three.cjs', '/build/three.module.js');
await require('esbuild').build({
  stdin: { loader: 'tsx', resolveDir: web, contents: `
    import React, { useState } from 'react';
    import { createRoot } from 'react-dom/client';
    import { ContainerPackingScene3d } from './src/features/tools/container-packing/ContainerPackingScene3d.tsx';
    const analysis = { packedItems: [{ name:'纸箱', width:30, height:25, occupiedHeight:60, topHeight:60,
      baseHeight:0, x:0, y:0, colorArgb:-16744448, loadCount:18, unitsRepresented:18 }] };
    function App() {
      const [grid, setGrid] = useState(false), [tick, setTick] = useState(0), [open, setOpen] = useState(true);
      return <><button onClick={()=>setGrid(value=>!value)}>切换框线</button>
        <button onClick={()=>setTick(value=>value+1)}>父级刷新 {tick}</button>
        <button onClick={()=>setOpen(false)}>离开三维</button>
        {open && <ContainerPackingScene3d analysis={analysis} dimensions={{length:1203,width:235,height:269}} renderMode={grid?'FullGrid':'OutlineOnly'}/>}</>;
    }
    createRoot(document.getElementById('root')).render(<App/>);
  ` }, outfile: bundle, bundle: true, jsx: 'automatic', format: 'iife', platform: 'browser', logLevel: 'silent',
  plugins: [{ name: 'observe-public-three-renderer', setup(build) {
    build.onResolve({ filter: /^three$/ }, () => ({ path: 'three', namespace: 'observed' }));
    build.onLoad({ filter: /.*/, namespace: 'observed' }, () => ({ resolveDir: web, contents: `
      export * from ${JSON.stringify(three)};
      import { Vector3, WebGLRenderer as Original } from ${JSON.stringify(three)};
      export class WebGLRenderer extends Original {
        constructor(...args) {
          super(...args);
          const metrics = window.sceneMetrics ??= { created:0, disposed:0, renders:0 };
          metrics.created++;
          const render = this.render.bind(this), dispose = this.dispose.bind(this);
          this.render = (scene, camera) => {
            metrics.renders++; metrics.angles={x:scene.children[0].rotation.x,y:scene.children[0].rotation.y}; metrics.zoom=camera.zoom;
            render(scene, camera); metrics.drawCalls=this.info.render.calls;
            const bounds = { minX:Infinity, maxX:-Infinity, minY:Infinity, maxY:-Infinity };
            scene.children[0].traverse(object => {
              const positions = object.geometry?.attributes.position;
              if (!positions) return;
              for (let index=0;index<positions.count;index++) {
                const point = new Vector3().fromBufferAttribute(positions,index).applyMatrix4(object.matrixWorld).project(camera);
                bounds.minX=Math.min(bounds.minX,point.x); bounds.maxX=Math.max(bounds.maxX,point.x);
                bounds.minY=Math.min(bounds.minY,point.y); bounds.maxY=Math.max(bounds.maxY,point.y);
              }
            });
            metrics.bounds = bounds;
          };
          this.dispose = () => { metrics.disposed++; dispose(); };
        }
      }
    ` }));
  } }],
});
let context;
try {
  context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), {
    executablePath: locateChromeForTesting(repo), headless: true,
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="zh-CN"><title>三维资源验收</title><style>.container-packing-3d-stage{width:640px;height:360px}.container-packing-3d-canvas{width:100%;height:100%}</style><div id="root"></div></html>');
  await page.addScriptTag({ path: bundle });
  await page.waitForSelector('canvas[data-scene-ready=true]');
  const frames = () => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await page.getByRole('button', { name: '柜门', exact: true }).click();
  await frames();
  assert.deepEqual(await page.evaluate(() => window.sceneMetrics.angles), { x: 0, y: -Math.PI / 2 }, 'door view faces the positive-X door opening');
  await page.getByRole('button', { name: '俯视', exact: true }).click();
  await frames();
  const top = await page.evaluate(() => window.sceneMetrics.angles);
  assert.deepEqual(top, { x: Math.PI / 2, y: 0 }, 'top view shows cargo above the floor');
  const projected = await page.evaluate(() => window.sceneMetrics.bounds);
  await page.screenshot({ path: path.join(output, 'packing-top-fit.png') });
  assert(Object.values(projected).every(value => Math.abs(value) < 1), `rotation around the container center keeps the complete model in view: ${JSON.stringify(projected)}`);
  await page.getByRole('button', { name: '切换框线' }).click();
  await page.waitForFunction(() => window.sceneMetrics.created === 2 && document.querySelector('canvas').dataset.renderMode === 'FullGrid');
  assert.deepEqual(await page.evaluate(() => window.sceneMetrics.angles), top, 'changing render mode retains the displayed view');
  await page.getByRole('button', { name: '父级刷新', exact: false }).click();
  await frames();
  assert.equal(await page.evaluate(() => window.sceneMetrics.created), 2, 'equal dimensions do not recreate a WebGL context');
  const canvas = page.locator('canvas');
  const rect = await canvas.boundingBox();
  await page.getByRole('button', { name: '柜门', exact: true }).click();
  await page.mouse.move(rect.x + 120, rect.y + 100);
  await page.mouse.down();
  await page.mouse.move(rect.x + 320, rect.y + 100);
  await page.mouse.up();
  await frames();
  const rotated = await page.evaluate(() => window.sceneMetrics.bounds);
  assert(Object.values(rotated).every(value => Math.abs(value) < 1), `rotating a long container out of the door view keeps the entire model visible: ${JSON.stringify(rotated)}`);
  await page.getByRole('button', { name: '俯视', exact: true }).click();
  await frames();
  await page.mouse.move(rect.x + 120, rect.y + 100);
  await page.mouse.down();
  await page.mouse.move(rect.x + 120, rect.y + 99);
  await page.mouse.up();
  await frames();
  assert(Math.abs((await page.evaluate(() => window.sceneMetrics.angles.x)) - (Math.PI / 2 - 0.005)) < 0.001, 'dragging from top view does not snap to a different angle');
  const beforeRight = await page.evaluate(() => window.sceneMetrics.angles);
  await page.mouse.down({ button: 'right' });
  await page.mouse.move(rect.x + 180, rect.y + 160);
  await page.mouse.up({ button: 'right' });
  await frames();
  assert.deepEqual(await page.evaluate(() => window.sceneMetrics.angles), beforeRight, 'right click does not rotate the model');
  await canvas.evaluate(node => node.addEventListener('pointerdown', event => { window.testPointerId = event.pointerId; }, { once: true }));
  await page.mouse.move(rect.x + 120, rect.y + 100);
  await page.mouse.down();
  const beforeBurst = await page.evaluate(() => window.sceneMetrics.renders);
  await canvas.evaluate(node => {
    const rect = node.getBoundingClientRect();
    node.dispatchEvent(new PointerEvent('pointermove', { pointerId: window.testPointerId + 1, clientX: 0, clientY: 0, bubbles: true }));
    for (let index=1;index<=120;index++) node.dispatchEvent(new PointerEvent('pointermove', {
      pointerId: window.testPointerId, clientX:rect.x+120+index/10, clientY:rect.y+100, bubbles:true,
    }));
  });
  await frames();
  assert.equal(await page.evaluate(() => window.sceneMetrics.renders), beforeBurst + 1, 'a burst of pointer movements renders only once per frame');
  await page.evaluate(() => window.dispatchEvent(new Event('blur')));
  await frames();
  const afterBlur = await page.evaluate(() => window.sceneMetrics.angles);
  await canvas.evaluate(node => node.dispatchEvent(new PointerEvent('pointermove', { pointerId:window.testPointerId, clientX:0, clientY:0, bubbles:true })));
  await page.mouse.up();
  await frames();
  assert.deepEqual(await page.evaluate(() => window.sceneMetrics.angles), afterBlur, 'blur releases the drag and ignores late pointer movement');
  const paused = await page.evaluate(() => window.sceneMetrics.renders);
  await frames();
  assert.equal(await page.evaluate(() => window.sceneMetrics.renders), paused, 'a paused static scene does not schedule continuous renders');
  await page.screenshot({ path: path.join(output, 'packing-top-full-grid.png') });
  await canvas.evaluate(node => node.dispatchEvent(new Event('webglcontextlost', { cancelable:true })));
  await page.getByRole('button', { name:'重试三维视图' }).waitFor();
  assert.equal(await page.locator('canvas').count(), 0, 'a lost context is released');
  await page.getByRole('button', { name:'重试三维视图' }).click();
  await page.waitForSelector('canvas[data-scene-ready=true]');
  assert.equal(await page.evaluate(() => window.sceneMetrics.created), 3, 'retry creates a fresh renderer');
  await page.getByRole('button', { name:'放大三维图', exact:true }).click();
  await frames();
  assert.equal(await page.evaluate(() => window.sceneMetrics.zoom), 1.2);
  await page.getByRole('button', { name:'重置三维视角', exact:true }).click();
  await frames();
  assert.equal(await page.evaluate(() => window.sceneMetrics.zoom), 1);
  await page.getByRole('button', { name: '离开三维' }).click();
  await frames();
  const metrics = await page.evaluate(() => window.sceneMetrics);
  assert.equal(metrics.created, metrics.disposed, 'every replaced or unmounted renderer is disposed');
  assert.deepEqual(errors, []);
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ passed: true, metrics }, null, 2));
  process.stdout.write('Container packing scene UI passed (view, input, idle rendering and cleanup).\n');
} finally { await context?.close(); }
