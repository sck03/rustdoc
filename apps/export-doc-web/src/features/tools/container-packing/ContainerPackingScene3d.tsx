import { useEffect, useMemo, useRef, useState } from "react";
import { Box, Pause, Play, RotateCcw, Square } from "lucide-react";
import { DirectionalLight, Fog, Group, HemisphereLight, PerspectiveCamera, Scene, WebGLRenderer } from "three";
import type { ApiContainerPackingAnalysisDto } from "../../../api/index.ts";
import { buildContainerPackingRoot, buildContainerPackingSceneData, disposeContainerPackingScene } from "./containerPackingSceneObjects.ts";
import type { ContainerPackingVisualizationDimensions } from "./containerPackingVisualizationModel.ts";
import type { ContainerPackingRenderModeValue } from "./containerPackingModel.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";

type ContainerPackingViewPreset = "isometric" | "top" | "door";

type ContainerPackingSceneControlApi = {
  resetView: () => void;
  setViewPreset: (preset: ContainerPackingViewPreset) => void;
  setAutoRotate: (enabled: boolean) => void;
};

const initialViewPreset: ContainerPackingViewPreset = "isometric";
const containerPackingViewPresets: Array<{ value: ContainerPackingViewPreset; label: string }> = [
  { value: "isometric", label: "等轴" },
  { value: "top", label: "俯视" },
  { value: "door", label: "柜门" },
];

const containerPackingViewAngles: Record<ContainerPackingViewPreset, { x: number; y: number }> = {
  isometric: { x: 0.22, y: 0.58 },
  top: { x: 1.18, y: 0 },
  door: { x: 0.12, y: -Math.PI / 2 },
};

export function ContainerPackingScene3d({
  analysis,
  dimensions,
  renderMode,
}: {
  analysis: ApiContainerPackingAnalysisDto;
  dimensions: ContainerPackingVisualizationDimensions;
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
  const sceneData = useMemo(() => buildContainerPackingSceneData(analysis, dimensions), [analysis, dimensions.length, dimensions.width, dimensions.height]);
  const viewRef = useRef({ ...containerPackingViewAngles[initialViewPreset] });

  useEffect(() => {
    const mount = mountRef.current;
    if (!mount) {
      return;
    }

    mount.innerHTML = "";
    const renderer = new WebGLRenderer({
      antialias: true,
      alpha: false,
      // Retaining every frame increases GPU memory use on low-power devices.
      preserveDrawingBuffer: false,
    });
    renderer.setClearColor(0xf7fafc, 1);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
    renderer.domElement.className = "container-packing-3d-canvas";
    renderer.domElement.dataset.packedItems = String(sceneData.boxes.length);
    renderer.domElement.dataset.sceneReady = "false";
    renderer.domElement.setAttribute("aria-label", "装柜三维画布");
    renderer.domElement.setAttribute("role", "img");
    mount.appendChild(renderer.domElement);

    const scene = new Scene();
    const fog = new Fog(0xf7fafc, 1, 100);
    scene.fog = fog;
    const camera = new PerspectiveCamera(36, 1, 0.1, 100);

    const content = buildContainerPackingRoot(sceneData, analysis, renderMode === "FullGrid");
    content.position.y = -sceneData.container.height / 2;
    const root = new Group();
    root.position.y = sceneData.container.height / 2;
    root.add(content);
    scene.add(root);

    const ambientLight = new HemisphereLight(0xffffff, 0xcbd5e1, 2.1);
    scene.add(ambientLight);
    const keyLight = new DirectionalLight(0xffffff, 2.4);
    keyLight.position.set(sceneData.container.length * 0.4, sceneData.container.height * 1.7, sceneData.container.width * 1.1);
    scene.add(keyLight);
    const fillLight = new DirectionalLight(0xbdd7ff, 0.9);
    fillLight.position.set(-sceneData.container.length * 0.7, sceneData.container.height * 1.1, -sceneData.container.width);
    scene.add(fillLight);

    const view = viewRef.current;
    let isDragging = false;
    let activePointerId: number | null = null;
    let lastPointerX = 0;
    let lastPointerY = 0;
    let animationFrame = 0;
    let previousFrameTime: number | null = null;
    let isPageVisible = document.visibilityState === "visible";
    let isInViewport = typeof IntersectionObserver === "undefined";
    const isSceneVisible = () => isPageVisible && isInViewport;

    const renderScene = () => {
      root.rotation.x = view.x;
      root.rotation.y = view.y;
      renderer.render(scene, camera);
      renderer.domElement.dataset.autoRotate = String(autoRotateRef.current);
      renderer.domElement.dataset.viewPreset = viewPresetRef.current;
      renderer.domElement.dataset.renderMode = renderMode;
      renderer.domElement.dataset.sceneReady = "true";
    };

    const cancelAnimation = () => {
      if (animationFrame !== 0) {
        cancelAnimationFrame(animationFrame);
        animationFrame = 0;
      }
      previousFrameTime = null;
    };

    const animate = (time: number) => {
      animationFrame = 0;
      if (!isSceneVisible()) {
        return;
      }
      if (!isDragging && autoRotateRef.current) {
        if (previousFrameTime !== null) view.y += Math.min(time - previousFrameTime, 64) * 0.000108;
      }
      previousFrameTime = time;
      renderScene();
      if (!isDragging && autoRotateRef.current) {
        animationFrame = requestAnimationFrame(animate);
      }
    };

    const scheduleAnimation = () => {
      if (isSceneVisible() && animationFrame === 0) {
        animationFrame = requestAnimationFrame(animate);
      }
    };

    const applyViewPreset = (preset: ContainerPackingViewPreset) => {
      const angles = containerPackingViewAngles[preset];
      view.x = angles.x;
      view.y = angles.y;
      viewPresetRef.current = preset;
      renderer.domElement.dataset.viewPreset = preset;
      scheduleAnimation();
    };

    const setAutoRotate = (enabled: boolean) => {
      autoRotateRef.current = enabled;
      if (enabled) {
        scheduleAnimation();
      } else {
        cancelAnimation();
        scheduleAnimation();
      }
    };

    controlApiRef.current = {
      resetView: () => applyViewPreset(initialViewPreset),
      setViewPreset: applyViewPreset,
      setAutoRotate,
    };

    const resize = () => {
      const rect = mount.getBoundingClientRect();
      const width = Math.max(Math.floor(rect.width), 320);
      const height = Math.max(Math.floor(rect.height), 260);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      const halfFov = Math.atan(Math.tan(camera.fov * Math.PI / 360) * Math.min(1, camera.aspect));
      const radius = Math.hypot(sceneData.container.length, sceneData.container.width, sceneData.container.height) / 2;
      const distance = radius / Math.sin(halfFov) * 1.1;
      camera.position.set(0, sceneData.container.height * 0.95, distance);
      camera.lookAt(0, sceneData.container.height / 2, 0);
      camera.far = distance * 4;
      fog.near = distance * 1.2;
      fog.far = distance * 2.4;
      camera.updateProjectionMatrix();
      scheduleAnimation();
    };

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(mount);
    resize();

    const handlePointerDown = (event: PointerEvent) => {
      if (event.button !== 0 || activePointerId !== null) return;
      activePointerId = event.pointerId;
      isDragging = true;
      cancelAnimation();
      lastPointerX = event.clientX;
      lastPointerY = event.clientY;
      renderer.domElement.setPointerCapture(event.pointerId);
    };

    const handlePointerMove = (event: PointerEvent) => {
      if (!isDragging || event.pointerId !== activePointerId) {
        return;
      }

      const deltaX = event.clientX - lastPointerX;
      const deltaY = event.clientY - lastPointerY;
      lastPointerX = event.clientX;
      lastPointerY = event.clientY;
      view.y += deltaX * 0.008;
      view.x = Math.min(1.45, Math.max(-0.08, view.x + deltaY * 0.005));
      scheduleAnimation();
    };

    const handlePointerUp = (event?: PointerEvent) => {
      if (activePointerId === null || event && event.pointerId !== activePointerId) return;
      const pointerId = activePointerId;
      activePointerId = null;
      isDragging = false;
      try {
        renderer.domElement.releasePointerCapture(pointerId);
      } catch {
        // The pointer may already be released when the window loses focus.
      }
      scheduleAnimation();
    };
    const handleBlur = () => handlePointerUp();

    const handleVisibilityChange = () => {
      isPageVisible = document.visibilityState === "visible";
      if (isSceneVisible()) {
        scheduleAnimation();
      } else {
        cancelAnimation();
        handlePointerUp();
      }
    };

    const intersectionObserver = typeof IntersectionObserver === "undefined"
      ? null
      : new IntersectionObserver(([entry]) => {
          isInViewport = Boolean(entry?.isIntersecting);
          if (isSceneVisible()) {
            scheduleAnimation();
          } else {
            cancelAnimation();
          }
        }, { threshold: 0.01 });

    renderer.domElement.addEventListener("pointerdown", handlePointerDown);
    renderer.domElement.addEventListener("pointermove", handlePointerMove);
    renderer.domElement.addEventListener("pointerup", handlePointerUp);
    renderer.domElement.addEventListener("pointercancel", handlePointerUp);
    renderer.domElement.addEventListener("lostpointercapture", handlePointerUp);
    window.addEventListener("blur", handleBlur);
    document.addEventListener("visibilitychange", handleVisibilityChange);
    intersectionObserver?.observe(mount);
    renderer.domElement.dataset.autoRotate = String(autoRotateRef.current);
    renderer.domElement.dataset.viewPreset = viewPresetRef.current;
    renderer.domElement.dataset.renderMode = renderMode;

    scheduleAnimation();

    return () => {
      cancelAnimation();
      resizeObserver.disconnect();
      intersectionObserver?.disconnect();
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      renderer.domElement.removeEventListener("pointerdown", handlePointerDown);
      renderer.domElement.removeEventListener("pointermove", handlePointerMove);
      renderer.domElement.removeEventListener("pointerup", handlePointerUp);
      renderer.domElement.removeEventListener("pointercancel", handlePointerUp);
      renderer.domElement.removeEventListener("lostpointercapture", handlePointerUp);
      window.removeEventListener("blur", handleBlur);
      controlApiRef.current = null;
      disposeContainerPackingScene(scene);
      renderer.dispose();
      mount.innerHTML = "";
    };
  }, [analysis, renderMode, sceneData]);

  function switchViewPreset(nextPreset: ContainerPackingViewPreset) {
    setAutoRotate(false);
    controlApiRef.current?.setAutoRotate(false);
    setViewPreset(nextPreset);
    controlApiRef.current?.setViewPreset(nextPreset);
  }

  function resetView() {
    setViewPreset(initialViewPreset);
    controlApiRef.current?.resetView();
  }

  function toggleAutoRotate() {
    const next = !autoRotate;
    setAutoRotate(next);
    controlApiRef.current?.setAutoRotate(next);
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
            onClick={toggleAutoRotate}
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

export default ContainerPackingScene3d;
