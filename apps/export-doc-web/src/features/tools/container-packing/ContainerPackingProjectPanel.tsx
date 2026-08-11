import { FolderOpen, RefreshCw, Save, Trash2 } from "lucide-react";
import type { ApiContainerPackingProjectSummaryDto } from "../../../api/index.ts";

type Props = {
  canLoadProject: boolean;
  canManage: boolean;
  canOperate: boolean;
  canSaveProject: boolean;
  currentProjectId: number;
  isDeletingProject: boolean;
  isLoadingProject: boolean;
  isRefreshingProjects: boolean;
  isSavingProject: boolean;
  projectName: string;
  savedProjects: ApiContainerPackingProjectSummaryDto[];
  selectedProjectId: string;
  onDeleteProject(): void;
  onLoadProject(): void;
  onProjectNameChange(value: string): void;
  onRefreshProjects(): void;
  onSaveProject(): void;
  onSelectedProjectChange(value: string): void;
};

export function ContainerPackingProjectPanel({
  canLoadProject,
  canManage,
  canOperate,
  canSaveProject,
  currentProjectId,
  isDeletingProject,
  isLoadingProject,
  isRefreshingProjects,
  isSavingProject,
  projectName,
  savedProjects,
  selectedProjectId,
  onDeleteProject,
  onLoadProject,
  onProjectNameChange,
  onRefreshProjects,
  onSaveProject,
  onSelectedProjectChange,
}: Props) {
  return (
    <div className="container-packing-project-panel" aria-label="装柜方案管理">
      <label className="container-packing-project-name">
        <span>方案名称</span>
        <input
          value={projectName}
          disabled={!canOperate}
          onChange={(event) => onProjectNameChange(event.target.value)}
        />
      </label>
      <label className="container-packing-project-select">
        <span>已存方案</span>
        <select value={selectedProjectId} onChange={(event) => onSelectedProjectChange(event.target.value)}>
          <option value="">选择方案</option>
          {savedProjects.map((project) => (
            <option key={project.id} value={project.id}>
              {project.name} {project.containerType ? `(${project.containerType})` : ""}
            </option>
          ))}
        </select>
      </label>
      <div className="container-packing-project-actions">
        <button
          className="command-button secondary"
          type="button"
          disabled={isRefreshingProjects}
          onClick={onRefreshProjects}
        >
          <RefreshCw size={16} aria-hidden="true" />
          <span>刷新方案</span>
        </button>
        <button
          className="command-button secondary"
          type="button"
          disabled={!canLoadProject || isLoadingProject}
          onClick={onLoadProject}
        >
          <FolderOpen size={16} aria-hidden="true" />
          <span>加载方案</span>
        </button>
        <button
          className="command-button secondary"
          type="button"
          disabled={!canSaveProject || isSavingProject}
          onClick={onSaveProject}
        >
          <Save size={16} aria-hidden="true" />
          <span>保存方案</span>
        </button>
        <button
          className="command-button secondary danger"
          type="button"
          disabled={!canManage || (!canLoadProject && currentProjectId <= 0) || isDeletingProject}
          onClick={onDeleteProject}
        >
          <Trash2 size={16} aria-hidden="true" />
          <span>删除方案</span>
        </button>
      </div>
    </div>
  );
}
