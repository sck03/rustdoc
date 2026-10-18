import {
  BoxGeometry, BufferGeometry, Color, DoubleSide, EdgesGeometry, GridHelper, Group,
  Line, LineBasicMaterial, LineSegments, type Material, Mesh, MeshStandardMaterial,
  type Object3D, PlaneGeometry, SphereGeometry, Vector3,
} from "three";
import type { ApiContainerPackingAnalysisDto, ApiPackedCargoItemDto } from "../../../api/index.ts";
import type { ContainerPackingVisualizationDimensions } from "./containerPackingVisualizationModel.ts";

type ContainerPackingSceneBox = {
  name: string;
  color: number;
  length: number;
  width: number;
  height: number;
  x: number;
  y: number;
  z: number;
  loadCount: number;
};

export function buildContainerPackingRoot(data: ReturnType<typeof buildContainerPackingSceneData>, analysis: ApiContainerPackingAnalysisDto, fullGrid: boolean) {
  const root = new Group();
  root.add(buildContainerFloor(data.container), buildContainerShell(data.container),
    buildContainerDoorFrame(data.container), buildContainerAxisMarkers(data.container));
  const materials = new Map<number, MeshStandardMaterial[]>();
  data.boxes.forEach(box => root.add(buildPackedCargoMesh(box, fullGrid, materials)));
  const marker = buildCenterOfGravityMarker(data, analysis);
  if (marker) root.add(marker);
  return root;
}

export function disposeContainerPackingScene(root: Object3D) {
  const geometries = new Set<BufferGeometry>(), materials = new Set<Material>();
  root.traverse(object => {
    const mesh = object as Mesh;
    if (mesh.geometry) geometries.add(mesh.geometry);
    if (Array.isArray(mesh.material)) mesh.material.forEach(material => materials.add(material));
    else if (mesh.material) materials.add(mesh.material);
  });
  geometries.forEach(geometry => geometry.dispose());
  materials.forEach(material => material.dispose());
}

export function buildContainerPackingSceneData(
  analysis: ApiContainerPackingAnalysisDto,
  dimensions: ContainerPackingVisualizationDimensions,
) {
  const largestDimension = Math.max(dimensions.length, dimensions.width, dimensions.height, 1);
  const scale = 12 / largestDimension;
  const container = {
    length: dimensions.length * scale,
    width: dimensions.width * scale,
    height: dimensions.height * scale,
  };
  const boxes = analysis.packedItems.map((item, index) => buildSceneBox(item, index, dimensions, scale));
  return {
    scale,
    container,
    boxes,
  };
}

function buildSceneBox(
  item: ApiPackedCargoItemDto,
  index: number,
  dimensions: ContainerPackingVisualizationDimensions,
  scale: number,
): ContainerPackingSceneBox {
  const footprint = readPackedCargoFootprint(item);
  const footprintLength = clampNumber(footprint.length, 1, dimensions.length);
  const footprintWidth = clampNumber(footprint.width, 1, dimensions.width);
  const occupiedHeight = clampNumber(item.occupiedHeight || item.topHeight - item.baseHeight, 1, dimensions.height);
  const x = clampNumber(item.x, 0, dimensions.length - footprintLength);
  const y = clampNumber(item.y, 0, dimensions.width - footprintWidth);
  const baseHeight = clampNumber(item.baseHeight, 0, dimensions.height - occupiedHeight);

  return {
    name: item.name || `货物 ${index + 1}`,
    color: signedArgbToNumber(item.colorArgb),
    length: footprintLength * scale,
    width: footprintWidth * scale,
    height: occupiedHeight * scale,
    x: (x + footprintLength / 2) * scale - (dimensions.length * scale) / 2,
    y: (baseHeight + occupiedHeight / 2) * scale,
    z: (y + footprintWidth / 2) * scale - (dimensions.width * scale) / 2,
    loadCount: item.isPalletized ? 1 : item.loadCount || 1,
  };
}

function readPackedCargoFootprint(item: ApiPackedCargoItemDto) {
  return {
    length: item.width,
    width: item.height,
  };
}

function buildContainerFloor(container: { length: number; width: number; height: number }) {
  const group = new Group();
  const floorGeometry = new PlaneGeometry(container.length, container.width);
  const floorMaterial = new MeshStandardMaterial({
    color: 0xe8eef4,
    metalness: 0.02,
    roughness: 0.86,
    side: DoubleSide,
  });
  const floor = new Mesh(floorGeometry, floorMaterial);
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = 0;
  group.add(floor);

  const grid = new GridHelper(
    Math.max(container.length, container.width),
    12,
    new Color(0x94a3b8),
    new Color(0xd4dee8),
  );
  grid.scale.z = container.width / Math.max(container.length, container.width);
  grid.position.y = 0.01;
  group.add(grid);
  return group;
}

function buildContainerShell(container: { length: number; width: number; height: number }) {
  const geometry = new BoxGeometry(container.length, container.height, container.width);
  const edges = new EdgesGeometry(geometry);
  const shell = new LineSegments(
    edges,
    new LineBasicMaterial({
      color: 0x849792,
      linewidth: 1,
    }),
  );
  shell.position.y = container.height / 2;
  geometry.dispose();
  return shell;
}

function buildContainerDoorFrame(container: { length: number; width: number; height: number }) {
  const group = new Group();
  const doorMaterial = new LineBasicMaterial({ color: 0xb76223, depthTest: false });
  const doorGeometry = new BufferGeometry().setFromPoints([
    new Vector3(container.length / 2, 0, -container.width / 2),
    new Vector3(container.length / 2, container.height, -container.width / 2),
    new Vector3(container.length / 2, container.height, container.width / 2),
    new Vector3(container.length / 2, 0, container.width / 2),
  ]);
  const door = new Line(doorGeometry, doorMaterial);
  door.renderOrder = 4;
  group.add(door);
  return group;
}

function buildContainerAxisMarkers(container: { length: number; width: number; height: number }) {
  const group = new Group();
  const headMarker = buildMarkerPlane(0x64748b, container.width, 0.05);
  headMarker.position.set(-container.length / 2, container.height * 0.02, 0);
  group.add(headMarker);

  const doorMarker = buildMarkerPlane(0xb76223, container.width, 0.07);
  doorMarker.position.set(container.length / 2, container.height * 0.025, 0);
  group.add(doorMarker);
  return group;
}

function buildMarkerPlane(color: number, width: number, depth: number) {
  const geometry = new BoxGeometry(depth, 0.04, width);
  const material = new MeshStandardMaterial({ color, roughness: 0.7 });
  return new Mesh(geometry, material);
}

function buildPackedCargoMesh(box: ContainerPackingSceneBox, showFullGrid: boolean, materials: Map<number, MeshStandardMaterial[]>) {
  const group = new Group();
  const geometry = new BoxGeometry(box.length, box.height, box.width);
  const material = materials.get(box.color) ?? buildPackedCargoMaterials(box.color);
  materials.set(box.color, material);
  const mesh = new Mesh(geometry, material);
  mesh.position.set(box.x, box.y, box.z);
  mesh.name = box.name;
  mesh.renderOrder = 1;
  group.add(mesh);

  const edges = new LineSegments(
    new EdgesGeometry(geometry),
    new LineBasicMaterial({ color: 0x111827 }),
  );
  edges.position.copy(mesh.position);
  edges.renderOrder = 3;
  group.add(edges);
  if (showFullGrid) {
    const grid = buildPackedCargoLayerLines(box);
    grid.position.copy(mesh.position);
    grid.renderOrder = 2;
    group.add(grid);
  }

  return group;
}

function buildPackedCargoLayerLines(box: ContainerPackingSceneBox) {
  const group = new Group();
  const layerCount = clampInteger(box.loadCount, 1, 18);
  if (layerCount <= 1) {
    return group;
  }

  const material = new LineBasicMaterial({
    color: 0x0f172a,
    transparent: true,
    opacity: 0.62,
  });
  const minX = -box.length / 2;
  const maxX = box.length / 2;
  const minZ = -box.width / 2;
  const maxZ = box.width / 2;
  const minY = -box.height / 2;
  const surfaceOffset = Math.max(Math.min(box.length, box.width, box.height) * 0.0015, 0.006);

  const points: Vector3[] = [];
  for (let index = 1; index < layerCount; index += 1) {
    const y = minY + (box.height * index) / layerCount;
    points.push(
      new Vector3(minX, y, maxZ + surfaceOffset),
      new Vector3(maxX, y, maxZ + surfaceOffset),
      new Vector3(maxX + surfaceOffset, y, minZ),
      new Vector3(maxX + surfaceOffset, y, maxZ),
      new Vector3(minX, y, minZ - surfaceOffset),
      new Vector3(maxX, y, minZ - surfaceOffset),
      new Vector3(minX - surfaceOffset, y, minZ),
      new Vector3(minX - surfaceOffset, y, maxZ),
    );
  }
  const layerLines = new LineSegments(new BufferGeometry().setFromPoints(points), material);
  layerLines.renderOrder = 2;
  group.add(layerLines);
  return group;
}

function buildPackedCargoMaterials(color: number) {
  const side = shadeColorNumber(color, -0.08);
  const farSide = shadeColorNumber(color, -0.18);
  const top = shadeColorNumber(color, 0.14);
  const bottom = shadeColorNumber(color, -0.24);
  const front = shadeColorNumber(color, -0.02);

  const materials = [side, farSide, top, bottom, front].map(
    (entry) =>
      new MeshStandardMaterial({
        color: entry,
        roughness: 0.72,
        metalness: 0.01,
      }),
  );
  return [...materials, materials[0]];
}

function buildCenterOfGravityMarker(
  sceneData: ReturnType<typeof buildContainerPackingSceneData>,
  analysis: ApiContainerPackingAnalysisDto,
) {
  if (!Number.isFinite(analysis.centerOfGravityX) || !Number.isFinite(analysis.centerOfGravityY)) {
    return null;
  }

  const group = new Group();
  const markerColor = analysis.isCenterOfGravityWithinTolerance ? 0x0f766e : 0xb42318;
  const markerMaterial = new MeshStandardMaterial({ color: markerColor, roughness: 0.42 });
  const sphere = new Mesh(new SphereGeometry(0.1, 18, 12), markerMaterial);
  sphere.position.set(
    analysis.centerOfGravityX * sceneData.scale - sceneData.container.length / 2,
    sceneData.container.height + 0.18,
    analysis.centerOfGravityY * sceneData.scale - sceneData.container.width / 2,
  );
  group.add(sphere);

  const lineGeometry = new BufferGeometry().setFromPoints([
    new Vector3(sphere.position.x, 0, sphere.position.z),
    new Vector3(sphere.position.x, sceneData.container.height + 0.15, sphere.position.z),
  ]);
  group.add(new Line(lineGeometry, new LineBasicMaterial({ color: markerColor })));
  return group;
}

function signedArgbToNumber(value: number) {
  return value >>> 0 & 0x00ffffff;
}

function shadeColorNumber(color: number, amount: number) {
  const normalized = color & 0x00ffffff;
  const target = amount >= 0 ? 255 : 0;
  const ratio = Math.min(Math.abs(amount), 1);
  const red = normalized >> 16 & 0xff;
  const green = normalized >> 8 & 0xff;
  const blue = normalized & 0xff;
  const shade = (channel: number) => Math.round(channel + (target - channel) * ratio);

  return shade(red) << 16 | shade(green) << 8 | shade(blue);
}

function clampNumber(value: number, min: number, max: number) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(Math.max(value, min), Math.max(min, max));
}

function clampInteger(value: number, min: number, max: number) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(Math.max(Math.trunc(value), min), Math.max(min, max));
}
