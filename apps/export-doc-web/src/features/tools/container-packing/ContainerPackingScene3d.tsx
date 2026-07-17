import { useEffect, useMemo, useRef, useState } from "react";
import { Box, Pause, Play, RotateCcw, Square } from "lucide-react";
import {
  BoxGeometry,
  BufferGeometry,
  Color,
  DirectionalLight,
  DoubleSide,
  EdgesGeometry,
  Fog,
  GridHelper,
  Group,
  HemisphereLight,
  Line,
  LineBasicMaterial,
  LineSegments,
  type Material,
  Mesh,
  MeshStandardMaterial,
  PerspectiveCamera,
  PlaneGeometry,
  Scene,
  SphereGeometry,
  Vector3,
  WebGLRenderer,
} from "three";
import type { ApiContainerPackingAnalysisDto, ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";

type ContainerPackingSceneDimensions = {
  length: number;
  width: number;
  height: number;
};

type ContainerPackingSceneBox = {
  key: string;
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

type ContainerPackingViewPreset = "isometric" | "top" | "door";
type ContainerPackingRenderModeValue = "OutlineOnly" | "FullGrid";

type ContainerPackingSceneControlApi = {
  resetView: () => void;
  setViewPreset: (preset: ContainerPackingViewPreset) => void;
};

const initialViewPreset: ContainerPackingViewPreset = "isometric";
const containerPackingViewPresets: Array<{ value: ContainerPackingViewPreset; label: string }> = [
  { value: "isometric", label: "等轴" },
  { value: "top", label: "俯视" },
  { value: "door", label: "柜门" },
];

const containerPackingViewAngles: Record<ContainerPackingViewPreset, { x: number; y: number }> = {
  isometric: { x: -0.22, y: 0.58 },
  top: { x: -1.18, y: 0 },
  door: { x: -0.12, y: Math.PI / 2 },
};

export function ContainerPackingScene3d({
  analysis,
  dimensions,
  renderMode,
}: {
  analysis: ApiContainerPackingAnalysisDto;
  dimensions: ContainerPackingSceneDimensions;
  renderMode: ContainerPackingRenderModeValue;
}) {
  const mountRef = useRef<HTMLDivElement | null>(null);
  const controlApiRef = useRef<ContainerPackingSceneControlApi | null>(null);
  const [autoRotate, setAutoRotate] = useState(true);
  const [viewPreset, setViewPreset] = useState<ContainerPackingViewPreset>(initialViewPreset);
  const autoRotateRef = useRef(autoRotate);
  const viewPresetRef = useRef(viewPreset);
  autoRotateRef.current = autoRotate;
  viewPresetRef.current = viewPreset;
  const sceneData = useMemo(() => buildContainerPackingSceneData(analysis, dimensions), [analysis, dimensions]);

  useEffect(() => {
    const mount = mountRef.current;
    if (!mount) {
      return;
    }

    mount.innerHTML = "";
    const renderer = new WebGLRenderer({
      antialias: true,
      alpha: false,
      preserveDrawingBuffer: true,
    });
    renderer.setClearColor(0xf7fafc, 1);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.domElement.className = "container-packing-3d-canvas";
    renderer.domElement.dataset.packedItems = String(sceneData.boxes.length);
    renderer.domElement.dataset.sceneReady = "false";
    renderer.domElement.setAttribute("aria-label", "装柜三维画布");
    renderer.domElement.setAttribute("role", "img");
    mount.appendChild(renderer.domElement);

    const scene = new Scene();
    scene.fog = new Fog(0xf7fafc, sceneData.cameraDistance * 1.2, sceneData.cameraDistance * 2.4);

    const camera = new PerspectiveCamera(36, 1, 0.1, sceneData.cameraDistance * 4);
    camera.position.set(0, sceneData.container.height * 0.95, sceneData.cameraDistance);
    camera.lookAt(0, sceneData.container.height * 0.42, 0);

    const root = new Group();
    root.rotation.x = containerPackingViewAngles[initialViewPreset].x;
    root.rotation.y = containerPackingViewAngles[initialViewPreset].y;
    scene.add(root);

    root.add(buildContainerFloor(sceneData.container));
    root.add(buildContainerShell(sceneData.container));
    root.add(buildContainerDoorFrame(sceneData.container));
    root.add(buildContainerAxisMarkers(sceneData.container));
    sceneData.boxes.forEach((box) => root.add(buildPackedCargoMesh(box, renderMode === "FullGrid")));
    const centerOfGravity = buildCenterOfGravityMarker(sceneData, analysis);
    if (centerOfGravity) {
      root.add(centerOfGravity);
    }

    const ambientLight = new HemisphereLight(0xffffff, 0xcbd5e1, 2.1);
    scene.add(ambientLight);
    const keyLight = new DirectionalLight(0xffffff, 2.4);
    keyLight.position.set(sceneData.container.length * 0.4, sceneData.container.height * 1.7, sceneData.container.width * 1.1);
    scene.add(keyLight);
    const fillLight = new DirectionalLight(0xbdd7ff, 0.9);
    fillLight.position.set(-sceneData.container.length * 0.7, sceneData.container.height * 1.1, -sceneData.container.width);
    scene.add(fillLight);

    let rotationY = containerPackingViewAngles[initialViewPreset].y;
    let rotationX = containerPackingViewAngles[initialViewPreset].x;
    let isDragging = false;
    let lastPointerX = 0;
    let lastPointerY = 0;
    let animationFrame = 0;

    const applyViewPreset = (preset: ContainerPackingViewPreset) => {
      const angles = containerPackingViewAngles[preset];
      rotationX = angles.x;
      rotationY = angles.y;
      viewPresetRef.current = preset;
      root.rotation.x = rotationX;
      root.rotation.y = rotationY;
      renderer.domElement.dataset.viewPreset = preset;
      renderer.render(scene, camera);
    };

    controlApiRef.current = {
      resetView: () => applyViewPreset(initialViewPreset),
      setViewPreset: applyViewPreset,
    };

    const resize = () => {
      const rect = mount.getBoundingClientRect();
      const width = Math.max(Math.floor(rect.width), 320);
      const height = Math.max(Math.floor(rect.height), 260);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      renderer.render(scene, camera);
    };

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(mount);
    resize();

    const handlePointerDown = (event: PointerEvent) => {
      isDragging = true;
      lastPointerX = event.clientX;
      lastPointerY = event.clientY;
      renderer.domElement.setPointerCapture(event.pointerId);
    };

    const handlePointerMove = (event: PointerEvent) => {
      if (!isDragging) {
        return;
      }

      const deltaX = event.clientX - lastPointerX;
      const deltaY = event.clientY - lastPointerY;
      lastPointerX = event.clientX;
      lastPointerY = event.clientY;
      rotationY += deltaX * 0.008;
      rotationX = clampNumber(rotationX + deltaY * 0.005, -0.72, 0.08);
    };

    const handlePointerUp = (event: PointerEvent) => {
      isDragging = false;
      try {
        renderer.domElement.releasePointerCapture(event.pointerId);
      } catch {
        // The pointer may already be released when the window loses focus.
      }
    };

    renderer.domElement.addEventListener("pointerdown", handlePointerDown);
    renderer.domElement.addEventListener("pointermove", handlePointerMove);
    renderer.domElement.addEventListener("pointerup", handlePointerUp);
    renderer.domElement.addEventListener("pointercancel", handlePointerUp);
    renderer.domElement.dataset.autoRotate = String(autoRotateRef.current);
    renderer.domElement.dataset.viewPreset = viewPresetRef.current;
    renderer.domElement.dataset.renderMode = renderMode;

    const animate = () => {
      if (!isDragging && autoRotateRef.current) {
        rotationY += 0.0018;
      }

      root.rotation.x = rotationX;
      root.rotation.y = rotationY;
      renderer.render(scene, camera);
      renderer.domElement.dataset.autoRotate = String(autoRotateRef.current);
      renderer.domElement.dataset.viewPreset = viewPresetRef.current;
      renderer.domElement.dataset.renderMode = renderMode;
      renderer.domElement.dataset.sceneReady = "true";
      animationFrame = requestAnimationFrame(animate);
    };
    animate();

    return () => {
      cancelAnimationFrame(animationFrame);
      resizeObserver.disconnect();
      renderer.domElement.removeEventListener("pointerdown", handlePointerDown);
      renderer.domElement.removeEventListener("pointermove", handlePointerMove);
      renderer.domElement.removeEventListener("pointerup", handlePointerUp);
      renderer.domElement.removeEventListener("pointercancel", handlePointerUp);
      controlApiRef.current = null;
      scene.traverse((object) => {
        const mesh = object as Mesh;
        if (mesh.geometry) {
          mesh.geometry.dispose();
        }

        const material = mesh.material as Material | Material[] | undefined;
        if (Array.isArray(material)) {
          material.forEach((entry) => entry.dispose());
        } else {
          material?.dispose();
        }
      });
      renderer.dispose();
      mount.innerHTML = "";
    };
  }, [analysis, renderMode, sceneData]);

  function switchViewPreset(nextPreset: ContainerPackingViewPreset) {
    setAutoRotate(false);
    setViewPreset(nextPreset);
    controlApiRef.current?.setViewPreset(nextPreset);
  }

  function resetView() {
    setViewPreset(initialViewPreset);
    controlApiRef.current?.resetView();
  }

  return (
    <section className="container-packing-3d-section" aria-label="装柜三维可视化">
      <div className="container-packing-3d-header">
        <div>
          <strong>三维装柜</strong>
          <span>
            {formatPlainNumber(dimensions.length)} x {formatPlainNumber(dimensions.width)} x {formatPlainNumber(dimensions.height)} cm
          </span>
        </div>
        <div className="container-packing-3d-toolbar" aria-label="装柜三维视角控制">
          <div className="segmented-control container-packing-view-buttons" role="group" aria-label="三维视角">
            {containerPackingViewPresets.map((preset) => (
              <button
                key={preset.value}
                type="button"
                className={viewPreset === preset.value ? "segmented-active" : ""}
                aria-pressed={viewPreset === preset.value}
                title={`切换到${preset.label}视角`}
                onClick={() => switchViewPreset(preset.value)}
              >
                {preset.value === "isometric" ? <Box size={14} aria-hidden="true" /> : <Square size={14} aria-hidden="true" />}
                {preset.label}
              </button>
            ))}
          </div>
          <button
            className="icon-button"
            type="button"
            aria-label={autoRotate ? "暂停三维自动旋转" : "恢复三维自动旋转"}
            title={autoRotate ? "暂停自动旋转" : "恢复自动旋转"}
            aria-pressed={autoRotate}
            onClick={() => setAutoRotate((current) => !current)}
          >
            {autoRotate ? <Pause size={16} aria-hidden="true" /> : <Play size={16} aria-hidden="true" />}
          </button>
          <button className="icon-button" type="button" aria-label="重置三维视角" title="重置视角" onClick={resetView}>
            <RotateCcw size={16} aria-hidden="true" />
          </button>
        </div>
      </div>
      <div className="container-packing-3d-status">
        <span>{sceneData.boxes.length} 个装载块</span>
        <span>{autoRotate ? "自动旋转" : "手动视角"}</span>
        <span>拖拽旋转</span>
      </div>
      <div
        ref={mountRef}
        className="container-packing-3d-stage"
        data-packed-items={sceneData.boxes.length}
        data-container-length={dimensions.length}
        data-container-width={dimensions.width}
        data-container-height={dimensions.height}
      />
    </section>
  );
}

function buildContainerPackingSceneData(
  analysis: ApiContainerPackingAnalysisDto,
  dimensions: ContainerPackingSceneDimensions,
) {
  const largestDimension = Math.max(dimensions.length, dimensions.width, dimensions.height, 1);
  const scale = 12 / largestDimension;
  const container = {
    length: dimensions.length * scale,
    width: dimensions.width * scale,
    height: dimensions.height * scale,
  };
  const boxes = analysis.packedItems.map((item, index) => buildSceneBox(item, index, dimensions, scale));
  const cameraDistance = Math.max(container.length, container.width, container.height) * 1.75;

  return {
    scale,
    container,
    boxes,
    cameraDistance: Math.max(cameraDistance, 14),
  };
}

function buildSceneBox(
  item: ApiPackedCargoItemDto,
  index: number,
  dimensions: ContainerPackingSceneDimensions,
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
    key: `${item.name || "cargo"}-${index}`,
    name: item.name || `货物 ${index + 1}`,
    color: signedArgbToNumber(item.colorArgb),
    length: footprintLength * scale,
    width: footprintWidth * scale,
    height: occupiedHeight * scale,
    x: (x + footprintLength / 2) * scale - (dimensions.length * scale) / 2,
    y: (baseHeight + occupiedHeight / 2) * scale,
    z: (y + footprintWidth / 2) * scale - (dimensions.width * scale) / 2,
    loadCount: item.unitsRepresented || item.loadCount || 1,
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
      color: 0x334155,
      linewidth: 1,
    }),
  );
  shell.position.y = container.height / 2;
  geometry.dispose();
  return shell;
}

function buildContainerDoorFrame(container: { length: number; width: number; height: number }) {
  const group = new Group();
  const doorMaterial = new LineBasicMaterial({ color: 0x0f766e });
  const doorGeometry = new BufferGeometry().setFromPoints([
    new Vector3(container.length / 2, 0, -container.width / 2),
    new Vector3(container.length / 2, container.height, -container.width / 2),
    new Vector3(container.length / 2, container.height, container.width / 2),
    new Vector3(container.length / 2, 0, container.width / 2),
  ]);
  const door = new Line(doorGeometry, doorMaterial);
  group.add(door);
  return group;
}

function buildContainerAxisMarkers(container: { length: number; width: number; height: number }) {
  const group = new Group();
  const headMarker = buildMarkerPlane(0x64748b, container.width, 0.05);
  headMarker.position.set(-container.length / 2, container.height * 0.02, 0);
  group.add(headMarker);

  const doorMarker = buildMarkerPlane(0x0f766e, container.width, 0.07);
  doorMarker.position.set(container.length / 2, container.height * 0.025, 0);
  group.add(doorMarker);
  return group;
}

function buildMarkerPlane(color: number, width: number, depth: number) {
  const geometry = new BoxGeometry(depth, 0.04, width);
  const material = new MeshStandardMaterial({ color, roughness: 0.7 });
  return new Mesh(geometry, material);
}

function buildPackedCargoMesh(box: ContainerPackingSceneBox, showFullGrid: boolean) {
  const group = new Group();
  const geometry = new BoxGeometry(box.length, box.height, box.width);
  const material = buildPackedCargoMaterials(box.color);
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

  for (let index = 1; index < layerCount; index += 1) {
    const y = minY + (box.height * index) / layerCount;
    const points = [
      new Vector3(minX, y, maxZ + surfaceOffset),
      new Vector3(maxX, y, maxZ + surfaceOffset),
      new Vector3(maxX + surfaceOffset, y, minZ),
      new Vector3(maxX + surfaceOffset, y, maxZ),
      new Vector3(minX, y, minZ - surfaceOffset),
      new Vector3(maxX, y, minZ - surfaceOffset),
      new Vector3(minX - surfaceOffset, y, minZ),
      new Vector3(minX - surfaceOffset, y, maxZ),
    ];
    const layerLines = new LineSegments(new BufferGeometry().setFromPoints(points), material);
    layerLines.renderOrder = 2;
    group.add(layerLines);
  }

  return group;
}

function buildPackedCargoMaterials(color: number) {
  const side = shadeColorNumber(color, -0.08);
  const farSide = shadeColorNumber(color, -0.18);
  const top = shadeColorNumber(color, 0.14);
  const bottom = shadeColorNumber(color, -0.24);
  const front = shadeColorNumber(color, -0.02);

  return [side, farSide, top, bottom, front, side].map(
    (entry) =>
      new MeshStandardMaterial({
        color: entry,
        roughness: 0.72,
        metalness: 0.01,
      }),
  );
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

export default ContainerPackingScene3d;
