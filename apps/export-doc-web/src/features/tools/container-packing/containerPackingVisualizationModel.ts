export type ContainerPackingVisualizationDimensions = {
  length: number;
  width: number;
  height: number;
};

export type ContainerPackingRenderBlockSource = {
  key: string;
  name: string;
  colorArgb: number;
  isRotated: boolean;
  isPalletized: boolean;
  x: number;
  y: number;
  length: number;
  width: number;
  baseHeight: number;
  occupiedHeight: number;
  unitsRepresented: number;
  loadCount: number;
  totalWeight: number;
  priorityGroup: string;
  preferredZone: string;
  lengthSegments: number;
  widthSegments: number;
  heightSegments: number;
};
