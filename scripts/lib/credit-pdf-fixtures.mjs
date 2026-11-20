// Anonymous text + image fixtures, including a native heading on a scanned page.
import fs from 'node:fs';
import { createRequire } from 'node:module';
import { deflateSync } from 'node:zlib';
const require = createRequire(new URL('../../apps/export-doc-web/package.json', import.meta.url));

export function creditPdfFixtures() {
  const { PNG } = require('pngjs');
  const image = PNG.sync.read(fs.readFileSync(new URL('../../tests/OcrFixtures/invoice.png', import.meta.url)));
  const rgb = Buffer.alloc(image.width * image.height * 3);
  for (let pixel = 0; pixel < image.width * image.height; pixel++) image.data.copy(rgb, pixel * 3, pixel * 4, pixel * 4 + 3);
  const scan = deflateSync(rgb);
  const stream = (bytes, metadata = '') => Buffer.concat([Buffer.from(`<< ${metadata} /Length ${bytes.length} >>\nstream\n`), bytes, Buffer.from('\nendstream')]);
  function pdf(samePage) {
    const heading = 'BT /F1 18 Tf 30 800 Td (NATIVE CREDIT COVER) Tj ET';
    const height = 535 * image.height / image.width;
    const raster = `q 535 0 0 ${height} 30 ${760 - height} cm /I1 Do Q`;
    const pages = samePage ? [heading + '\n' + raster] : [heading, raster];
    const bodies = [null, '<< /Type /Catalog /Pages 2 0 R >>', `<< /Type /Pages /Kids [${pages.map((_,i) => `${5 + i * 2} 0 R`).join(' ')}] /Count ${pages.length} >>`,
      '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', stream(scan, `/Type /XObject /Subtype /Image /Width ${image.width} /Height ${image.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode`)];
    for (const [i, content] of pages.entries()) {
      bodies.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> /XObject << /I1 4 0 R >> >> /Contents ${6 + i * 2} 0 R >>`, stream(Buffer.from(content)));
    }
    const chunks = [Buffer.from('%PDF-1.4\n')], offsets = [0];
    let length = chunks[0].length;
    for (let i = 1; i < bodies.length; i++) {
      offsets.push(length);
      const chunk = Buffer.concat([Buffer.from(`${i} 0 obj\n`), Buffer.from(bodies[i]), Buffer.from('\nendobj\n')]);
      chunks.push(chunk); length += chunk.length;
    }
    chunks.push(Buffer.from(`xref\n0 ${bodies.length}\n0000000000 65535 f \n${offsets.slice(1).map(offset => `${String(offset).padStart(10,'0')} 00000 n \n`).join('')}trailer\n<< /Size ${bodies.length} /Root 1 0 R >>\nstartxref\n${length}\n%%EOF\n`));
    return Buffer.concat(chunks);
  }
  return [['mixed-pages', pdf(false)], ['mixed-single-page', pdf(true)]];
}
