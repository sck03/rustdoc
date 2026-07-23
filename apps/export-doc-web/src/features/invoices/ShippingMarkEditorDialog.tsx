import { useEffect, useMemo, useRef, useState, type PointerEvent } from "react";
import { Circle, Diamond, Eraser, Minus, MousePointer2, Save, Square, Trash2, Triangle, Type, X } from "lucide-react";
import { buildPortableCanvasFont } from "../../app/typographyPolicy.ts";

type ShippingMarkTool = "select" | "text" | "line" | "rectangle" | "diamond" | "triangle" | "circle";
type DrawableShippingMarkTool = Exclude<ShippingMarkTool, "select">;

type ShippingMarkShape = {
  id: string;
  kind: DrawableShippingMarkTool;
  x: number;
  y: number;
  width: number;
  height: number;
  text?: string;
};

type CanvasPoint = {
  x: number;
  y: number;
};

type ActivePointer =
  | {
      kind: "draw";
      pointerId: number;
      shapeId: string;
      origin: CanvasPoint;
    }
  | {
      kind: "drag";
      pointerId: number;
      shapeId: string;
      offset: CanvasPoint;
    };

const canvasWidth = 720;
const canvasHeight = 420;
const textFont = buildPortableCanvasFont(600, 24);
const defaultTextDraft = "N/M";

const toolButtons: Array<{
  tool: ShippingMarkTool;
  title: string;
  icon: typeof MousePointer2;
}> = [
  { tool: "select", title: "选择", icon: MousePointer2 },
  { tool: "text", title: "文字", icon: Type },
  { tool: "line", title: "线条", icon: Minus },
  { tool: "rectangle", title: "矩形", icon: Square },
  { tool: "diamond", title: "菱形", icon: Diamond },
  { tool: "triangle", title: "三角形", icon: Triangle },
  { tool: "circle", title: "圆形", icon: Circle },
];

export function ShippingMarkEditorDialog({
  initialImageDataUrl,
  isSaving,
  message,
  onClose,
  onSave,
}: {
  initialImageDataUrl?: string | null;
  isSaving: boolean;
  message?: string | null;
  onClose: () => void;
  onSave: (imageDataUrl: string) => Promise<void>;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const activePointerRef = useRef<ActivePointer | null>(null);
  const [tool, setTool] = useState<ShippingMarkTool>("select");
  const [textDraft, setTextDraft] = useState(defaultTextDraft);
  const [shapes, setShapes] = useState<ShippingMarkShape[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [backgroundImage, setBackgroundImage] = useState<HTMLImageElement | null>(null);

  const selectedShape = useMemo(
    () => shapes.find((shape) => shape.id === selectedId) ?? null,
    [selectedId, shapes],
  );

  useEffect(() => {
    setTool("select");
    setTextDraft(defaultTextDraft);
    setShapes([]);
    setSelectedId(null);
    activePointerRef.current = null;

    if (!initialImageDataUrl) {
      setBackgroundImage(null);
      return;
    }

    let isCancelled = false;
    const image = new Image();
    image.onload = () => {
      if (!isCancelled) {
        setBackgroundImage(image);
      }
    };
    image.onerror = () => {
      if (!isCancelled) {
        setBackgroundImage(null);
      }
    };
    image.src = initialImageDataUrl;

    return () => {
      isCancelled = true;
    };
  }, [initialImageDataUrl]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) {
      return;
    }

    const context = canvas.getContext("2d");
    if (!context) {
      return;
    }

    renderShippingMarkCanvas(context, backgroundImage, shapes, selectedId);
  }, [backgroundImage, selectedId, shapes]);

  function readCanvasPoint(event: PointerEvent<HTMLCanvasElement>): CanvasPoint {
    const rect = event.currentTarget.getBoundingClientRect();
    return {
      x: clamp(((event.clientX - rect.left) / rect.width) * canvasWidth, 0, canvasWidth),
      y: clamp(((event.clientY - rect.top) / rect.height) * canvasHeight, 0, canvasHeight),
    };
  }

  function handlePointerDown(event: PointerEvent<HTMLCanvasElement>) {
    if (isSaving) {
      return;
    }

    const point = readCanvasPoint(event);
    event.currentTarget.setPointerCapture(event.pointerId);

    if (tool === "text") {
      const text = textDraft.trim();
      if (!text) {
        return;
      }

      const shape = createTextShape(point, text);
      setShapes((current) => [...current, shape]);
      setSelectedId(shape.id);
      setTool("select");
      return;
    }

    if (tool === "select") {
      const context = canvasRef.current?.getContext("2d") ?? null;
      const hitShape = hitTestShippingMarkShape(shapes, point, context);
      if (!hitShape) {
        setSelectedId(null);
        activePointerRef.current = null;
        return;
      }

      setSelectedId(hitShape.id);
      if (hitShape.kind === "text") {
        setTextDraft(hitShape.text ?? "");
      }
      activePointerRef.current = {
        kind: "drag",
        pointerId: event.pointerId,
        shapeId: hitShape.id,
        offset: {
          x: point.x - hitShape.x,
          y: point.y - hitShape.y,
        },
      };
      return;
    }

    const shape: ShippingMarkShape = {
      id: createShapeId(),
      kind: tool,
      x: point.x,
      y: point.y,
      width: 0,
      height: 0,
    };
    activePointerRef.current = {
      kind: "draw",
      pointerId: event.pointerId,
      shapeId: shape.id,
      origin: point,
    };
    setShapes((current) => [...current, shape]);
    setSelectedId(shape.id);
  }

  function handleTextDraftChange(value: string) {
    setTextDraft(value);
    if (!selectedId) {
      return;
    }

    setShapes((current) =>
      current.map((shape) =>
        shape.id === selectedId && shape.kind === "text"
          ? {
              ...shape,
              text: value,
              width: Math.max(36, value.trim().length * 16),
            }
          : shape,
      ),
    );
  }

  function handlePointerMove(event: PointerEvent<HTMLCanvasElement>) {
    const activePointer = activePointerRef.current;
    if (!activePointer || activePointer.pointerId !== event.pointerId || isSaving) {
      return;
    }

    const point = readCanvasPoint(event);
    if (activePointer.kind === "draw") {
      setShapes((current) =>
        current.map((shape) =>
          shape.id === activePointer.shapeId
            ? {
                ...shape,
                width: point.x - activePointer.origin.x,
                height: point.y - activePointer.origin.y,
              }
            : shape,
        ),
      );
      return;
    }

    setShapes((current) =>
      current.map((shape) =>
        shape.id === activePointer.shapeId
          ? {
              ...shape,
              x: clamp(point.x - activePointer.offset.x, -Math.abs(shape.width), canvasWidth),
              y: clamp(point.y - activePointer.offset.y, -Math.abs(shape.height), canvasHeight),
            }
          : shape,
      ),
    );
  }

  function handlePointerUp(event: PointerEvent<HTMLCanvasElement>) {
    const activePointer = activePointerRef.current;
    if (!activePointer || activePointer.pointerId !== event.pointerId) {
      return;
    }

    if (activePointer.kind === "draw") {
      setShapes((current) => current.filter((shape) => shape.id !== activePointer.shapeId || isMeaningfulShape(shape)));
    }
    activePointerRef.current = null;
  }

  function deleteSelectedShape() {
    if (!selectedId || isSaving) {
      return;
    }

    setShapes((current) => current.filter((shape) => shape.id !== selectedId));
    setSelectedId(null);
  }

  function clearShapes() {
    if (isSaving) {
      return;
    }

    setShapes([]);
    setSelectedId(null);
  }

  async function saveCanvas() {
    const canvas = canvasRef.current;
    const context = canvas?.getContext("2d");
    if (!canvas || !context) {
      return;
    }

    renderShippingMarkCanvas(context, backgroundImage, shapes, null);
    try {
      await onSave(canvas.toDataURL("image/png"));
    } catch {
      renderShippingMarkCanvas(context, backgroundImage, shapes, selectedId);
      return;
    }

    renderShippingMarkCanvas(context, backgroundImage, shapes, selectedId);
  }

  return (
    <div className="single-window-lock-backdrop shipping-mark-backdrop">
      <div className="single-window-lock-dialog shipping-mark-dialog" role="dialog" aria-modal="true" aria-labelledby="shipping-mark-editor-title">
        <div className="single-window-lock-header">
          <div className="single-window-lock-title">
            <Square size={18} aria-hidden="true" />
            <h2 id="shipping-mark-editor-title">唛头图片</h2>
          </div>
          <button className="icon-button" type="button" title="关闭" aria-label="关闭" onClick={onClose} disabled={isSaving}>
            <X size={17} aria-hidden="true" />
          </button>
        </div>

        <div className="shipping-mark-toolbar">
          <div className="shipping-mark-tool-grid" role="toolbar" aria-label="唛头绘图工具">
            {toolButtons.map(({ tool: itemTool, title, icon: Icon }) => (
              <button
                key={itemTool}
                className={`icon-button compact-icon-button shipping-mark-tool-button${tool === itemTool ? " tool-active" : ""}`}
                type="button"
                title={title}
                aria-label={title}
                aria-pressed={tool === itemTool}
                disabled={isSaving}
                onClick={() => setTool(itemTool)}
              >
                <Icon size={16} aria-hidden="true" />
              </button>
            ))}
          </div>
          <label className="shipping-mark-text-tool">
            <Type size={16} aria-hidden="true" />
            <input
              type="text"
              value={textDraft}
              aria-label="唛头文字内容"
              disabled={isSaving}
              maxLength={80}
              placeholder="唛头文字"
              onFocus={() => setTool("text")}
              onChange={(event) => handleTextDraftChange(event.target.value)}
            />
          </label>
          <div className="shipping-mark-tool-actions">
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="删除"
              aria-label="删除"
              disabled={isSaving || !selectedShape}
              onClick={deleteSelectedShape}
            >
              <Trash2 size={16} aria-hidden="true" />
            </button>
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="清空"
              aria-label="清空"
              disabled={isSaving || shapes.length === 0}
              onClick={clearShapes}
            >
              <Eraser size={16} aria-hidden="true" />
            </button>
          </div>
        </div>

        {message ? <div className="item-editor-message shipping-mark-message">{message}</div> : null}

        <div className="shipping-mark-canvas-shell">
          <canvas
            ref={canvasRef}
            className="shipping-mark-canvas"
            width={canvasWidth}
            height={canvasHeight}
            onPointerDown={handlePointerDown}
            onPointerMove={handlePointerMove}
            onPointerUp={handlePointerUp}
            onPointerCancel={handlePointerUp}
          />
        </div>

        <div className="single-window-lock-footer shipping-mark-footer">
          <button className="command-button secondary" type="button" onClick={onClose} disabled={isSaving}>
            取消
          </button>
          <button className="command-button" type="button" onClick={() => void saveCanvas()} disabled={isSaving}>
            <Save size={17} aria-hidden="true" />
            <span>{isSaving ? "保存中" : "保存"}</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function renderShippingMarkCanvas(
  context: CanvasRenderingContext2D,
  backgroundImage: HTMLImageElement | null,
  shapes: ShippingMarkShape[],
  selectedId: string | null,
) {
  context.clearRect(0, 0, canvasWidth, canvasHeight);
  context.fillStyle = "#ffffff";
  context.fillRect(0, 0, canvasWidth, canvasHeight);
  context.lineWidth = 2;
  context.lineCap = "round";
  context.lineJoin = "round";

  if (backgroundImage) {
    drawContainedImage(context, backgroundImage);
  }

  shapes.forEach((shape) => drawShippingMarkShape(context, shape, shape.id === selectedId));
}

function drawContainedImage(context: CanvasRenderingContext2D, image: HTMLImageElement) {
  const scale = Math.min(canvasWidth / image.width, canvasHeight / image.height);
  const width = image.width * scale;
  const height = image.height * scale;
  const x = (canvasWidth - width) / 2;
  const y = (canvasHeight - height) / 2;
  context.drawImage(image, x, y, width, height);
}

function drawShippingMarkShape(context: CanvasRenderingContext2D, shape: ShippingMarkShape, isSelected: boolean) {
  context.strokeStyle = "#111827";
  context.fillStyle = "#111827";
  context.setLineDash([]);

  switch (shape.kind) {
    case "line":
      context.beginPath();
      context.moveTo(shape.x, shape.y);
      context.lineTo(shape.x + shape.width, shape.y + shape.height);
      context.stroke();
      break;
    case "rectangle": {
      const rect = normalizeShapeRect(shape);
      context.strokeRect(rect.x, rect.y, rect.width, rect.height);
      break;
    }
    case "diamond": {
      const rect = normalizeShapeRect(shape);
      context.beginPath();
      context.moveTo(rect.x + rect.width / 2, rect.y);
      context.lineTo(rect.x + rect.width, rect.y + rect.height / 2);
      context.lineTo(rect.x + rect.width / 2, rect.y + rect.height);
      context.lineTo(rect.x, rect.y + rect.height / 2);
      context.closePath();
      context.stroke();
      break;
    }
    case "triangle": {
      const rect = normalizeShapeRect(shape);
      context.beginPath();
      context.moveTo(rect.x + rect.width / 2, rect.y);
      context.lineTo(rect.x + rect.width, rect.y + rect.height);
      context.lineTo(rect.x, rect.y + rect.height);
      context.closePath();
      context.stroke();
      break;
    }
    case "circle": {
      const rect = normalizeShapeRect(shape);
      context.beginPath();
      context.ellipse(rect.x + rect.width / 2, rect.y + rect.height / 2, Math.max(1, rect.width / 2), Math.max(1, rect.height / 2), 0, 0, Math.PI * 2);
      context.stroke();
      break;
    }
    case "text":
      context.font = textFont;
      context.textBaseline = "top";
      context.fillText(shape.text ?? "", shape.x, shape.y);
      break;
  }

  if (!isSelected) {
    return;
  }

  const rect = readShapeBounds(context, shape);
  context.save();
  context.strokeStyle = "#2563eb";
  context.lineWidth = 1.5;
  context.setLineDash([6, 4]);
  context.strokeRect(rect.x - 5, rect.y - 5, rect.width + 10, rect.height + 10);
  context.restore();
}

function hitTestShippingMarkShape(shapes: ShippingMarkShape[], point: CanvasPoint, context: CanvasRenderingContext2D | null) {
  for (let index = shapes.length - 1; index >= 0; index -= 1) {
    const shape = shapes[index];
    if (shape.kind === "line") {
      if (distanceToLine(point, { x: shape.x, y: shape.y }, { x: shape.x + shape.width, y: shape.y + shape.height }) <= 8) {
        return shape;
      }
      continue;
    }

    const rect = readShapeBounds(context, shape);
    if (point.x >= rect.x - 6 && point.x <= rect.x + rect.width + 6 && point.y >= rect.y - 6 && point.y <= rect.y + rect.height + 6) {
      return shape;
    }
  }

  return null;
}

function normalizeShapeRect(shape: ShippingMarkShape) {
  return {
    x: Math.min(shape.x, shape.x + shape.width),
    y: Math.min(shape.y, shape.y + shape.height),
    width: Math.abs(shape.width),
    height: Math.abs(shape.height),
  };
}

function readShapeBounds(context: CanvasRenderingContext2D | null, shape: ShippingMarkShape) {
  if (shape.kind === "text") {
    if (context) {
      context.font = textFont;
    }
    const textWidth = Math.max(36, context?.measureText(shape.text ?? "").width ?? shape.width);
    return {
      x: shape.x,
      y: shape.y,
      width: textWidth,
      height: 30,
    };
  }

  if (shape.kind === "line") {
    const rect = normalizeShapeRect(shape);
    return {
      x: rect.x,
      y: rect.y,
      width: Math.max(1, rect.width),
      height: Math.max(1, rect.height),
    };
  }

  return normalizeShapeRect(shape);
}

function createTextShape(point: CanvasPoint, text: string): ShippingMarkShape {
  return {
    id: createShapeId(),
    kind: "text",
    x: point.x,
    y: point.y,
    width: Math.max(36, text.length * 16),
    height: 30,
    text: text.trim(),
  };
}

function isMeaningfulShape(shape: ShippingMarkShape) {
  if (shape.kind === "line") {
    return Math.hypot(shape.width, shape.height) >= 6;
  }

  if (shape.kind === "text") {
    return Boolean(shape.text?.trim());
  }

  return Math.abs(shape.width) >= 6 && Math.abs(shape.height) >= 6;
}

function distanceToLine(point: CanvasPoint, start: CanvasPoint, end: CanvasPoint) {
  const lineLengthSquared = (end.x - start.x) ** 2 + (end.y - start.y) ** 2;
  if (lineLengthSquared === 0) {
    return Math.hypot(point.x - start.x, point.y - start.y);
  }

  const ratio = Math.max(0, Math.min(1, ((point.x - start.x) * (end.x - start.x) + (point.y - start.y) * (end.y - start.y)) / lineLengthSquared));
  const projected = {
    x: start.x + ratio * (end.x - start.x),
    y: start.y + ratio * (end.y - start.y),
  };
  return Math.hypot(point.x - projected.x, point.y - projected.y);
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function createShapeId() {
  return `shipping-mark-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
