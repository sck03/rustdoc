import { BoxGeometry, BufferAttribute, BufferGeometry, Color, EdgesGeometry, Group, LineBasicMaterial, LineSegments, Mesh, MeshStandardMaterial } from "three";
import type { ContainerPackingSceneBox } from "./containerPackingSceneObjects.ts";

// Cargo has no per-box interaction. Batch faces and outlines into two draw calls.
export function buildCargoGeometry(boxes: ContainerPackingSceneBox[]) {
  const root = new Group();
  if (!boxes.length) return root;
  const cube = new BoxGeometry(1, 1, 1);
  const faces = cube.toNonIndexed(), edges = new EdgesGeometry(cube);
  const source = faces.getAttribute("position"), normals = faces.getAttribute("normal"), outline = edges.getAttribute("position");
  const positions = new Float32Array(boxes.length * source.count * 3);
  const normal = new Float32Array(positions.length), colors = new Float32Array(positions.length);
  const lines = new Float32Array(boxes.length * outline.count * 3);
  const palettes = new Map<number, Color[]>();
  boxes.forEach((box, index) => {
    const palette = palettes.get(box.color) ?? [-0.08, -0.18, 0.14, -0.24, -0.02, -0.08].map(amount => shade(box.color, amount));
    palettes.set(box.color, palette);
    for (let vertex = 0; vertex < source.count; vertex++) {
      const offset = (index * source.count + vertex) * 3;
      positions[offset] = source.getX(vertex) * box.length + box.x;
      positions[offset + 1] = source.getY(vertex) * box.height + box.y;
      positions[offset + 2] = source.getZ(vertex) * box.width + box.z;
      normal[offset] = normals.getX(vertex); normal[offset + 1] = normals.getY(vertex); normal[offset + 2] = normals.getZ(vertex);
      palette[Math.floor(vertex / 6)].toArray(colors, offset);
    }
    for (let vertex = 0; vertex < outline.count; vertex++) {
      const offset = (index * outline.count + vertex) * 3;
      lines[offset] = outline.getX(vertex) * box.length + box.x;
      lines[offset + 1] = outline.getY(vertex) * box.height + box.y;
      lines[offset + 2] = outline.getZ(vertex) * box.width + box.z;
    }
  });
  cube.dispose(); faces.dispose(); edges.dispose();
  const geometry = new BufferGeometry().setAttribute("position", new BufferAttribute(positions, 3))
    .setAttribute("normal", new BufferAttribute(normal, 3)).setAttribute("color", new BufferAttribute(colors, 3));
  const mesh = new Mesh(geometry, new MeshStandardMaterial({ vertexColors: true, roughness: 0.72, metalness: 0.01 }));
  mesh.renderOrder = 1;
  const border = new LineSegments(new BufferGeometry().setAttribute("position", new BufferAttribute(lines, 3)), new LineBasicMaterial({ color: 0x111827 }));
  border.renderOrder = 3;
  root.add(mesh, border);
  return root;
}

export function setCargoGrid(root: Group, boxes: ContainerPackingSceneBox[], visible: boolean) {
  let grid = root.getObjectByName("cargo-layer-grid");
  if (!grid && visible) {
    const layers = (box: ContainerPackingSceneBox) => Math.min(18, Math.max(1, Math.trunc(box.loadCount)));
    const positions = new Float32Array(boxes.reduce((sum, box) => sum + (layers(box) - 1) * 24, 0));
    let offset = 0;
    for (const box of boxes) {
      const count = layers(box), inset = Math.max(Math.min(box.length, box.width, box.height) * 0.0015, 0.006);
      const minX = box.x - box.length / 2, maxX = box.x + box.length / 2;
      const minZ = box.z - box.width / 2, maxZ = box.z + box.width / 2;
      for (let layer = 1; layer < count; layer++) {
        const y = box.y - box.height / 2 + box.height * layer / count;
        positions.set([minX,y,maxZ+inset, maxX,y,maxZ+inset, maxX+inset,y,minZ, maxX+inset,y,maxZ,
          minX,y,minZ-inset, maxX,y,minZ-inset, minX-inset,y,minZ, minX-inset,y,maxZ], offset);
        offset += 24;
      }
    }
    grid = new LineSegments(new BufferGeometry().setAttribute("position", new BufferAttribute(positions, 3)),
      new LineBasicMaterial({ color: 0x0f172a, transparent: true, opacity: 0.62 }));
    grid.name = "cargo-layer-grid"; grid.renderOrder = 2; root.add(grid);
  }
  if (grid) grid.visible = visible;
}

function shade(color: number, amount: number) {
  const target = amount >= 0 ? 255 : 0, ratio = Math.abs(amount);
  const channel = (shift: number) => { const value = color >> shift & 0xff; return Math.round(value + (target - value) * ratio); };
  return new Color(channel(16) << 16 | channel(8) << 8 | channel(0));
}
