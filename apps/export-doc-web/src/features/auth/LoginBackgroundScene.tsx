import { useEffect, useRef } from "react";

type Vec3 = readonly [number, number, number];

type ProjectedPoint = {
  x: number;
  y: number;
  z: number;
};

type FlightPanel = {
  points: Vec3[];
  fill: string;
  stroke: string;
  opacity: number;
  phase: number;
};

type EscortGlyph = {
  radius: number;
  phase: number;
  y: number;
  color: string;
};

type SceneState = {
  panels: FlightPanel[];
  stars: Vec3[];
  starLinks: Array<readonly [number, number]>;
  escorts: EscortGlyph[];
};

export function LoginBackgroundScene() {
  const mountRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const mount = mountRef.current;
    if (!mount) {
      return undefined;
    }

    return createLoginScene(mount);
  }, []);

  return <div className="login-3d-layer" ref={mountRef} aria-hidden="true" />;
}

function createLoginScene(mount: HTMLDivElement) {
  clearMount(mount);
  mount.dataset.sceneStatus = "loading";

  const canvas = document.createElement("canvas");
  canvas.className = "login-3d-canvas";
  canvas.dataset.sceneReady = "false";
  canvas.dataset.sceneRenderer = "canvas2d";
  canvas.dataset.sceneTheme = "canvas-folded-flight";
  mount.appendChild(canvas);

  const context = canvas.getContext("2d", {
    alpha: true,
    desynchronized: true,
  } as CanvasRenderingContext2DSettings);
  if (!context) {
    mount.dataset.sceneStatus = "unavailable";
    return () => clearMount(mount);
  }

  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const lowPower = isLikelyLowPowerDevice();
  const targetFps = reducedMotion ? 0 : lowPower ? 15 : 24;
  const frameIntervalMs = targetFps > 0 ? 1000 / targetFps : Number.POSITIVE_INFINITY;
  canvas.dataset.sceneMotion = reducedMotion ? "reduced" : "animated";
  canvas.dataset.sceneFps = targetFps.toString();

  const scene = createSceneState();
  let animationFrame = 0;
  let lastRenderAt = 0;
  let size = resizeCanvas(canvas, context);

  const resize = () => {
    size = resizeCanvas(canvas, context);
    drawScene(context, scene, size, 0);
    canvas.dataset.sceneReady = "true";
  };

  const resizeObserver = new ResizeObserver(resize);
  resizeObserver.observe(mount);
  resize();

  const animate = (time: number) => {
    if (!document.hidden && time - lastRenderAt >= frameIntervalMs) {
      drawScene(context, scene, size, time / 1000);
      canvas.dataset.sceneReady = "true";
      lastRenderAt = time;
    }

    animationFrame = requestAnimationFrame(animate);
  };

  if (!reducedMotion) {
    animationFrame = requestAnimationFrame(animate);
  }

  mount.dataset.sceneStatus = "ready";

  return () => {
    cancelAnimationFrame(animationFrame);
    resizeObserver.disconnect();
    clearMount(mount);
    mount.dataset.sceneStatus = "disposed";
  };
}

function createSceneState(): SceneState {
  const panels: FlightPanel[] = [
    {
      points: [
        [1.48, 0.08, 0.02],
        [0.05, 0.36, 0.16],
        [-1.62, 0.16, -0.14],
        [-0.36, -0.02, 0.08],
      ],
      fill: "rgba(37, 99, 235, 0.54)",
      stroke: "rgba(255, 255, 255, 0.48)",
      opacity: 0.92,
      phase: 0.2,
    },
    {
      points: [
        [1.34, 0.02, -0.08],
        [0.0, 0.18, -0.42],
        [-1.22, -0.1, -0.64],
        [-0.18, -0.18, -0.22],
      ],
      fill: "rgba(15, 118, 110, 0.52)",
      stroke: "rgba(236, 253, 245, 0.42)",
      opacity: 0.86,
      phase: 1.4,
    },
    {
      points: [
        [1.26, -0.05, 0.05],
        [-0.14, -0.12, 0.26],
        [-1.2, -0.66, 0.1],
        [-0.24, -0.42, -0.04],
      ],
      fill: "rgba(20, 184, 166, 0.48)",
      stroke: "rgba(255, 255, 255, 0.36)",
      opacity: 0.82,
      phase: 2.1,
    },
    {
      points: [
        [1.08, 0.1, -0.02],
        [0.22, 0.56, 0.04],
        [-0.7, 0.56, -0.2],
        [0.0, 0.24, -0.04],
      ],
      fill: "rgba(245, 158, 11, 0.48)",
      stroke: "rgba(255, 250, 235, 0.4)",
      opacity: 0.76,
      phase: 2.8,
    },
    {
      points: [
        [-0.2, -0.22, 0.18],
        [-0.84, -0.82, 0.26],
        [-1.46, -0.88, 0.02],
        [-0.74, -0.46, -0.02],
      ],
      fill: "rgba(239, 111, 97, 0.42)",
      stroke: "rgba(255, 255, 255, 0.34)",
      opacity: 0.7,
      phase: 3.3,
    },
  ];

  const stars: Vec3[] = [];
  for (let index = 0; index < 64; index += 1) {
    const angle = index * 2.399963;
    const radius = 1.04 + (index % 8) * 0.13;
    const height = ((index % 11) - 5) * 0.09;
    stars.push([Math.cos(angle) * radius - 0.18, height, Math.sin(angle) * radius * 0.56 - 0.4]);
  }

  const starLinks: Array<readonly [number, number]> = [];
  for (let index = 0; index < 14; index += 1) {
    starLinks.push([index * 4, index * 4 + 7]);
  }

  const escortColors = ["#0f766e", "#2563eb", "#f59e0b", "#14b8a6"];
  const escorts: EscortGlyph[] = Array.from({ length: 10 }, (_, index) => ({
    radius: 1.12 + (index % 5) * 0.12,
    phase: index * 0.74,
    y: (index % 6) * 0.15 - 0.38,
    color: escortColors[index % escortColors.length],
  }));

  return { panels, stars, starLinks, escorts };
}

function resizeCanvas(canvas: HTMLCanvasElement, context: CanvasRenderingContext2D) {
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(Math.floor(rect.width), 1);
  const height = Math.max(Math.floor(rect.height), 1);
  const pixelRatio = Math.min(window.devicePixelRatio || 1, 1);
  canvas.width = Math.max(Math.floor(width * pixelRatio), 1);
  canvas.height = Math.max(Math.floor(height * pixelRatio), 1);
  context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  return { width, height, pixelRatio };
}

function drawScene(
  context: CanvasRenderingContext2D,
  scene: SceneState,
  size: { width: number; height: number; pixelRatio: number },
  elapsed: number,
) {
  const { width, height } = size;
  context.clearRect(0, 0, width, height);
  context.save();
  context.globalCompositeOperation = "source-over";
  drawStars(context, scene, width, height, elapsed);
  drawDataRails(context, width, height, elapsed);
  drawFlightForm(context, scene, width, height, elapsed);
  drawEscorts(context, scene, width, height, elapsed);
  drawGlassFrame(context, width, height, elapsed);
  context.restore();
}

function drawStars(
  context: CanvasRenderingContext2D,
  scene: SceneState,
  width: number,
  height: number,
  elapsed: number,
) {
  const transform = createTransform(elapsed, width, height);
  context.save();
  context.lineWidth = 1;
  context.strokeStyle = `rgba(15, 118, 110, ${0.11 + Math.cos(elapsed * 0.7) * 0.035})`;
  scene.starLinks.forEach(([from, to]) => {
    const first = project(scene.stars[from], transform);
    const second = project(scene.stars[to], transform);
    context.beginPath();
    context.moveTo(first.x, first.y);
    context.lineTo(second.x, second.y);
    context.stroke();
  });

  context.fillStyle = `rgba(37, 99, 235, ${0.25 + Math.sin(elapsed * 0.9) * 0.05})`;
  scene.stars.forEach((star, index) => {
    const point = project(star, transform);
    const radius = 1.2 + (index % 3) * 0.35;
    context.beginPath();
    context.arc(point.x, point.y, radius, 0, Math.PI * 2);
    context.fill();
  });
  context.restore();
}

function drawDataRails(context: CanvasRenderingContext2D, width: number, height: number, elapsed: number) {
  const transform = createTransform(elapsed, width, height);
  context.save();
  context.lineWidth = 1.2;
  for (let lane = 0; lane < 5; lane += 1) {
    const opacity = lane === 2 ? 0.5 : 0.3;
    context.strokeStyle = lane === 2 ? `rgba(245, 158, 11, ${opacity})` : `rgba(15, 118, 110, ${opacity})`;
    context.beginPath();
    for (let pointIndex = 0; pointIndex < 8; pointIndex += 1) {
      const x = -2.25 + pointIndex * 0.38 + Math.sin(elapsed * 1.1 + lane) * 0.05;
      const y = lane * 0.22 - 0.46 + Math.sin(pointIndex * 0.74 + lane + elapsed * 1.2) * 0.08;
      const z = lane % 2 === 0 ? -0.34 : 0.34;
      const point = project([x, y, z], transform);
      if (pointIndex === 0) {
        context.moveTo(point.x, point.y);
      } else {
        context.lineTo(point.x, point.y);
      }
    }
    context.stroke();
  }
  context.restore();
}

function drawFlightForm(
  context: CanvasRenderingContext2D,
  scene: SceneState,
  width: number,
  height: number,
  elapsed: number,
) {
  const transform = createTransform(elapsed, width, height);
  const panels = scene.panels
    .map((panel) => ({
      panel,
      points: panel.points.map((point) => project(addPanelPulse(point, elapsed, panel.phase), transform)),
    }))
    .sort((left, right) => averageZ(left.points) - averageZ(right.points));

  context.save();
  panels.forEach(({ panel, points }) => {
    context.globalAlpha = panel.opacity;
    context.fillStyle = panel.fill;
    context.strokeStyle = panel.stroke;
    context.lineWidth = 1;
    drawPolygon(context, points);
    context.fill();
    context.stroke();
  });

  const body = [
    [-1.16, -0.02, 0.02],
    [1.26, 0.06, 0.04],
  ] as const;
  const first = project(body[0], transform);
  const second = project(body[1], transform);
  context.globalAlpha = 0.64;
  context.strokeStyle = "rgba(31, 41, 55, 0.58)";
  context.lineWidth = 8;
  context.lineCap = "round";
  context.beginPath();
  context.moveTo(first.x, first.y);
  context.lineTo(second.x, second.y);
  context.stroke();

  context.strokeStyle = "rgba(255, 255, 255, 0.44)";
  context.lineWidth = 2;
  context.beginPath();
  context.moveTo(first.x, first.y - 2);
  context.lineTo(second.x, second.y - 2);
  context.stroke();
  context.restore();
}

function drawEscorts(
  context: CanvasRenderingContext2D,
  scene: SceneState,
  width: number,
  height: number,
  elapsed: number,
) {
  const transform = createTransform(elapsed, width, height);
  context.save();
  scene.escorts.forEach((glyph, index) => {
    const orbit = elapsed * (0.3 + (index % 3) * 0.04) + glyph.phase;
    const center = project(
      [-0.45 + Math.cos(orbit) * glyph.radius, glyph.y + Math.sin(elapsed * 1.1 + glyph.phase) * 0.09, -0.28 + Math.sin(orbit) * 0.48],
      transform,
    );
    const angle = orbit * 0.36;
    context.save();
    context.translate(center.x, center.y);
    context.rotate(angle);
    context.fillStyle = glyph.color;
    context.globalAlpha = 0.34;
    context.beginPath();
    context.moveTo(8, 0);
    context.lineTo(-6, 4);
    context.lineTo(-4, -4);
    context.closePath();
    context.fill();
    context.restore();
  });
  context.restore();
}

function drawGlassFrame(context: CanvasRenderingContext2D, width: number, height: number, elapsed: number) {
  const transform = createTransform(elapsed, width, height);
  const frame = [
    [-1.9, 0.72, -0.62],
    [0.25, 0.6, -0.58],
    [0.12, -0.68, -0.58],
    [-2.02, -0.54, -0.62],
  ] as const;
  const points = frame.map((point) => project(point, transform));
  context.save();
  context.globalAlpha = 0.16;
  context.fillStyle = "rgba(255, 255, 255, 0.84)";
  context.strokeStyle = "rgba(31, 41, 55, 0.28)";
  context.lineWidth = 1;
  drawPolygon(context, points);
  context.fill();
  context.stroke();
  context.restore();
}

function addPanelPulse(point: Vec3, elapsed: number, phase: number): Vec3 {
  return [point[0], point[1] + Math.sin(elapsed * 1.15 + phase) * 0.035, point[2] + Math.cos(elapsed * 0.9 + phase) * 0.03];
}

function createTransform(elapsed: number, width: number, height: number) {
  return {
    centerX: width * (width < 860 ? 0.5 : 0.49) + Math.sin(elapsed * 0.18) * 38,
    centerY: height * (width < 860 ? 0.44 : 0.5) + Math.sin(elapsed * 0.31) * 18,
    scale: Math.min(width, height) * (width < 860 ? 0.34 : 0.4),
    rotationX: -0.18 + Math.sin(elapsed * 0.25) * 0.07,
    rotationY: -0.48 + Math.sin(elapsed * 0.34) * 0.34,
    rotationZ: -0.08 + Math.sin(elapsed * 0.22) * 0.055,
  };
}

function project(point: Vec3, transform: ReturnType<typeof createTransform>): ProjectedPoint {
  const rotated = rotate(point, transform.rotationX, transform.rotationY, transform.rotationZ);
  const perspective = 3.8 / (4.6 + rotated[2]);
  return {
    x: transform.centerX + rotated[0] * transform.scale * perspective,
    y: transform.centerY - rotated[1] * transform.scale * perspective,
    z: rotated[2],
  };
}

function rotate(point: Vec3, rotationX: number, rotationY: number, rotationZ: number): Vec3 {
  const sinX = Math.sin(rotationX);
  const cosX = Math.cos(rotationX);
  const sinY = Math.sin(rotationY);
  const cosY = Math.cos(rotationY);
  const sinZ = Math.sin(rotationZ);
  const cosZ = Math.cos(rotationZ);

  const y1 = point[1] * cosX - point[2] * sinX;
  const z1 = point[1] * sinX + point[2] * cosX;
  const x2 = point[0] * cosY + z1 * sinY;
  const z2 = -point[0] * sinY + z1 * cosY;
  const x3 = x2 * cosZ - y1 * sinZ;
  const y3 = x2 * sinZ + y1 * cosZ;
  return [x3, y3, z2];
}

function drawPolygon(context: CanvasRenderingContext2D, points: ProjectedPoint[]) {
  context.beginPath();
  points.forEach((point, index) => {
    if (index === 0) {
      context.moveTo(point.x, point.y);
    } else {
      context.lineTo(point.x, point.y);
    }
  });
  context.closePath();
}

function averageZ(points: ProjectedPoint[]) {
  return points.reduce((total, point) => total + point.z, 0) / Math.max(points.length, 1);
}

function isLikelyLowPowerDevice() {
  const navigatorWithMemory = navigator as Navigator & { deviceMemory?: number };
  return (
    window.innerWidth < 860 ||
    (navigator.hardwareConcurrency > 0 && navigator.hardwareConcurrency <= 4) ||
    (typeof navigatorWithMemory.deviceMemory === "number" && navigatorWithMemory.deviceMemory <= 4)
  );
}

function clearMount(mount: HTMLDivElement) {
  while (mount.firstChild) {
    mount.removeChild(mount.firstChild);
  }
}
