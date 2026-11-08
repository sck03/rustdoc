import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

export async function verifyPackagePrint({ api, page, run, output, compose }) {
  const { content } = await api('/api/reports/templates/content?reportType=ExportDocument&templatePath=Templates%2FExport%2Finvoice_template.dtpl');
  const original = JSON.parse(content);
  const cases = [['A4','Portrait',21000,29700],['A5','Portrait',14800,21000],['A4','Landscape',29700,21000]];
  const items = [];
  for (const [size,orientation,width,height] of cases) {
    const design = structuredClone(original);
    Object.assign(design.page,{size,orientation,widthHundredthMm:width,heightHundredthMm:height});
    const layer = design.layers.find(layer=>layer.elements.some(element=>element.type==='Text'));
    const element = layer.elements.find(element=>element.type==='Text');
    Object.assign(element,{text:`${size} ${orientation}`,xHundredthMm:0,yHundredthMm:0,widthHundredthMm:5000,heightHundredthMm:1000});
    layer.role='Body'; layer.print.repeatOnEveryPage=false;
    layer.elements=[element]; layer.designHeightHundredthMm=1000;
    design.layers=[layer];
    items.push(await api('/api/reports/templates/preview',{reportType:'ExportDocument',content:JSON.stringify(design),withSeal:false}));
  }
  const html = compose({items});
  fs.writeFileSync(path.join(output,'mixed-paper.html'),html);
  await run(`document.open();document.write(${JSON.stringify(html)});document.close();`);
  await page.send('Emulation.setEmulatedMedia',{media:'print'});
  const dimensions = await run(`({ids:[...document.querySelectorAll('[id]')].map(node=>node.id),widths:[...document.querySelectorAll('.report-page')].map(node=>parseFloat(getComputedStyle(node).width)),pages:[...document.querySelectorAll('.report-page')].map(node=>getComputedStyle(node).page)})`);
  assert.equal(new Set(dimensions.ids).size,dimensions.ids.length,'Combined reports and SVG resources have unique IDs');
  assert.equal(new Set(dimensions.pages).size,3);
  assert.equal(dimensions.widths.length,3);
  dimensions.widths.forEach((width,index)=>assert(Math.abs(width-cases[index][2]/100*96/25.4)<1,JSON.stringify(dimensions)));
  const pdf=Buffer.from((await page.send('Page.printToPDF',{printBackground:true,preferCSSPageSize:true})).data,'base64');
  fs.writeFileSync(path.join(output,'mixed-paper.pdf'),pdf);
  const boxes=[...pdf.toString('latin1').matchAll(/\/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)/g)].map(match=>match.slice(1).map(Number));
  for (const [, , width,height] of cases) assert(boxes.some(box=>Math.abs(box[0]-width/100*72/25.4)<2 && Math.abs(box[1]-height/100*72/25.4)<2),JSON.stringify(boxes));
  fs.writeFileSync(path.join(output,'mixed-paper-result.json'),JSON.stringify({dimensions,boxes},null,2));
}
