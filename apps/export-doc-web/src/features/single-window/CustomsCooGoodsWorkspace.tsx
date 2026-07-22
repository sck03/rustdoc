import { Paperclip, Plus } from "lucide-react";
import type {
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooEditorOptionsResponse,
  ApiCustomsCooItemDto,
  ApiCustomsCooNonpartyCorpDto,
} from "../../api/index.ts";
import { CooItemsEditor } from "./CustomsCooItemsEditor.tsx";
import { CooAttachmentTable, CooNonpartyEditor } from "./CustomsCooTables.tsx";
import { shouldShowCooNonpartyCorps } from "./customsCooModel.ts";

export function CustomsCooGoodsWorkspace({
  document,
  editorOptions,
  disabled,
  savingProducerRowIndex,
  onAddItem,
  onChangeItem,
  onRemoveItem,
  onGenerateGoodsDescription,
  onCopyOriginAndEnterpriseToFollowingRows,
  onOpenProducerProfile,
  onSaveProducerProfile,
  onAddNonpartyCorp,
  onChangeNonpartyCorp,
  onRemoveNonpartyCorp,
  onSelectAttachments,
  onChangeAttachment,
  onRemoveAttachment,
  onAttachmentPathError,
}: {
  document: ApiCustomsCooDocumentDto;
  editorOptions: ApiCustomsCooEditorOptionsResponse;
  disabled: boolean;
  savingProducerRowIndex: number | null;
  onAddItem: () => void;
  onChangeItem: (index: number, next: Partial<ApiCustomsCooItemDto>) => void;
  onRemoveItem: (index: number) => void;
  onGenerateGoodsDescription: (index: number) => void;
  onCopyOriginAndEnterpriseToFollowingRows: (index: number) => void;
  onOpenProducerProfile: (index: number) => void;
  onSaveProducerProfile: (index: number) => void;
  onAddNonpartyCorp: () => void;
  onChangeNonpartyCorp: (index: number, next: Partial<ApiCustomsCooNonpartyCorpDto>) => void;
  onRemoveNonpartyCorp: (index: number) => void;
  onSelectAttachments: () => void;
  onChangeAttachment: (index: number, next: Partial<ApiCustomsCooAttachmentDto>) => void;
  onRemoveAttachment: (index: number) => void;
  onAttachmentPathError: (message: string) => void;
}) {
  return (
    <>
      <section id="coo-section-items" className="form-section single-window-editor-section" aria-label="商品明细">
        <div className="section-header">
          <h2>商品明细</h2>
          <span className="section-count">{document.items.length} 行</span>
          <button className="icon-button" type="button" title="新增商品" aria-label="新增商品" onClick={onAddItem}>
            <Plus size={17} aria-hidden="true" />
          </button>
        </div>
        <CooItemsEditor
          items={document.items}
          certType={document.certType}
          editorOptions={editorOptions}
          disabled={disabled}
          savingProducerRowIndex={savingProducerRowIndex}
          onChangeItem={onChangeItem}
          onRemoveItem={onRemoveItem}
          onGenerateGoodsDescription={onGenerateGoodsDescription}
          onCopyOriginAndEnterpriseToFollowingRows={onCopyOriginAndEnterpriseToFollowingRows}
          onOpenProducerProfile={onOpenProducerProfile}
          onSaveProducerProfile={onSaveProducerProfile}
        />
      </section>

      {shouldShowCooNonpartyCorps(document) ? (
        <section id="coo-section-nonparty" className="form-section single-window-editor-section" aria-label="第三方企业">
          <div className="section-header">
            <h2>第三方企业</h2>
            <button className="icon-button" type="button" title="新增第三方企业" aria-label="新增第三方企业" onClick={onAddNonpartyCorp}>
              <Plus size={17} aria-hidden="true" />
            </button>
          </div>
          <CooNonpartyEditor data={document.nonpartyCorps} onChangeCorp={onChangeNonpartyCorp} onRemoveCorp={onRemoveNonpartyCorp} />
        </section>
      ) : null}

      <section id="coo-section-attachments" className="form-section single-window-editor-section" aria-label="附件">
        <div className="section-header">
          <h2>附件</h2>
          <button className="icon-button" type="button" title="选择附件" aria-label="选择附件" disabled={disabled} onClick={onSelectAttachments}>
            <Paperclip size={17} aria-hidden="true" />
          </button>
          <span className="section-count">{document.attachments.length} 条</span>
        </div>
        <CooAttachmentTable
          data={document.attachments}
          certTypeOptions={editorOptions.certTypeOptions}
          disabled={disabled}
          onChangeAttachment={onChangeAttachment}
          onRemoveAttachment={onRemoveAttachment}
          onPathError={onAttachmentPathError}
        />
      </section>
    </>
  );
}
