import { useCallback, type Dispatch, type SetStateAction } from "react";
import type {
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooItemDto,
  ApiCustomsCooNonpartyCorpDto,
} from "../../api/index.ts";
import { selectCustomsCooAttachmentFiles } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import {
  buildCooGoodsDescription,
  copyCooOriginAndEnterpriseFields,
  createAttachmentFromPath,
  createEmptyCooItem,
  createEmptyNonpartyCorp,
  getCooGoodsDescriptionFailureMessage,
  normalizeText,
  numberOrZero,
} from "./customsCooModel.ts";
import { cloneEditorDocument } from "./singleWindowEditorTools.ts";

type MessageSetter = Dispatch<SetStateAction<string | null>>;

type CustomsCooDocumentEditorOptions = {
  document: ApiCustomsCooDocumentDto | null;
  undoDocument: ApiCustomsCooDocumentDto | null;
  setDocument: Dispatch<SetStateAction<ApiCustomsCooDocumentDto | null>>;
  setUndoDocument: Dispatch<SetStateAction<ApiCustomsCooDocumentDto | null>>;
  setMessage: MessageSetter;
  setSuccessMessage: MessageSetter;
};

export function useCustomsCooDocumentEditor({
  document,
  undoDocument,
  setDocument,
  setUndoDocument,
  setMessage,
  setSuccessMessage,
}: CustomsCooDocumentEditorOptions) {
  const clearToolFeedback = useCallback(() => {
    setUndoDocument(null);
    setSuccessMessage(null);
  }, [setSuccessMessage, setUndoDocument]);

  const patchDocument = useCallback((next: Partial<ApiCustomsCooDocumentDto>) => {
    setDocument((current) => (current ? { ...current, ...next } : current));
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const patchItem = useCallback((index: number, next: Partial<ApiCustomsCooItemDto>) => {
    setDocument((current) => current
      ? {
          ...current,
          items: current.items.map((item, itemIndex) => (itemIndex === index ? { ...item, ...next } : item)),
        }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const addItem = useCallback(() => {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      const nextGNo = Math.max(0, ...current.items.map((item) => numberOrZero(item.gNo))) + 1;
      return {
        ...current,
        items: [...current.items, createEmptyCooItem(current.id, nextGNo, current.invNo)],
      };
    });
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const removeItem = useCallback((index: number) => {
    setDocument((current) => current
      ? { ...current, items: current.items.filter((_, itemIndex) => itemIndex !== index) }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const patchNonpartyCorp = useCallback((index: number, next: Partial<ApiCustomsCooNonpartyCorpDto>) => {
    setDocument((current) => current
      ? {
          ...current,
          nonpartyCorps: current.nonpartyCorps.map((corp, corpIndex) =>
            corpIndex === index ? { ...corp, ...next } : corp,
          ),
        }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const addNonpartyCorp = useCallback(() => {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      const nextSortNo = Math.max(0, ...current.nonpartyCorps.map((corp) => numberOrZero(corp.sortNo))) + 1;
      return {
        ...current,
        nonpartyCorps: [...current.nonpartyCorps, createEmptyNonpartyCorp(current.id, nextSortNo)],
      };
    });
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const removeNonpartyCorp = useCallback((index: number) => {
    setDocument((current) => current
      ? { ...current, nonpartyCorps: current.nonpartyCorps.filter((_, corpIndex) => corpIndex !== index) }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const addAttachmentsFromDialog = useCallback(async () => {
    if (!document) {
      return;
    }

    try {
      const attachmentPaths = (await selectCustomsCooAttachmentFiles())
        .map((filePath) => filePath.trim())
        .filter(Boolean);
      if (attachmentPaths.length === 0) {
        return;
      }

      setDocument((current) => {
        if (!current) {
          return current;
        }

        let nextSortOrder = Math.max(
          0,
          ...current.attachments.map((attachment) => numberOrZero(attachment.sortOrder)),
        ) + 1;
        const newAttachments = attachmentPaths.map((filePath) =>
          createAttachmentFromPath(current, filePath, nextSortOrder++),
        );
        return { ...current, attachments: [...current.attachments, ...newAttachments] };
      });
      setUndoDocument(null);
      setMessage(null);
      setSuccessMessage(`已添加 ${attachmentPaths.length} 个附件，保存后写入草稿。`);
    } catch (error) {
      setMessage(readDesktopError(error));
      setSuccessMessage(null);
    }
  }, [document, setDocument, setMessage, setSuccessMessage, setUndoDocument]);

  const patchAttachment = useCallback((index: number, next: Partial<ApiCustomsCooAttachmentDto>) => {
    setDocument((current) => current
      ? {
          ...current,
          attachments: current.attachments.map((attachment, attachmentIndex) =>
            attachmentIndex === index ? { ...attachment, ...next } : attachment,
          ),
        }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const removeAttachment = useCallback((index: number) => {
    setDocument((current) => current
      ? { ...current, attachments: current.attachments.filter((_, attachmentIndex) => attachmentIndex !== index) }
      : current);
    clearToolFeedback();
  }, [clearToolFeedback, setDocument]);

  const generateGoodsDescription = useCallback((index: number) => {
    if (!document || !document.items[index]) {
      return;
    }

    const item = document.items[index];
    const generated = buildCooGoodsDescription(item);
    if (!generated) {
      setMessage(getCooGoodsDescriptionFailureMessage(item));
      setSuccessMessage(null);
      return;
    }

    if (normalizeText(item.goodsDesc) === generated) {
      setMessage(null);
      setSuccessMessage(`第 ${index + 1} 行货物描述已是当前生成内容。`);
      return;
    }

    setDocument({
      ...document,
      items: document.items.map((currentItem, itemIndex) =>
        itemIndex === index ? { ...currentItem, goodsDesc: generated } : currentItem,
      ),
    });
    setUndoDocument(cloneEditorDocument(document));
    setMessage(null);
    setSuccessMessage(`已生成第 ${index + 1} 行货物描述，保存后写入草稿。`);
  }, [document, setDocument, setMessage, setSuccessMessage, setUndoDocument]);

  const copyOriginAndEnterpriseToFollowingRows = useCallback((index: number) => {
    if (!document || !document.items[index] || index >= document.items.length - 1) {
      return;
    }

    const source = document.items[index];
    let changedRows = 0;
    const nextItems = document.items.map((item, itemIndex) => {
      if (itemIndex <= index) {
        return item;
      }

      const { item: nextItem, changed } = copyCooOriginAndEnterpriseFields(source, item);
      if (changed) {
        changedRows++;
      }
      return nextItem;
    });

    if (changedRows === 0) {
      setMessage(null);
      setSuccessMessage("后续货项没有需要复制的原产标准或生产企业字段。");
      return;
    }

    setUndoDocument(cloneEditorDocument(document));
    setDocument({ ...document, items: nextItems });
    setMessage(null);
    setSuccessMessage(`已复制当前行原产标准和生产企业字段到后续 ${changedRows} 行，保存后写入草稿。`);
  }, [document, setDocument, setMessage, setSuccessMessage, setUndoDocument]);

  const undoToolAction = useCallback(() => {
    if (!undoDocument) {
      return;
    }

    setDocument(cloneEditorDocument(undoDocument));
    setUndoDocument(null);
    setMessage(null);
    setSuccessMessage("已撤销上一次工具动作，保存后写入草稿。");
  }, [setDocument, setMessage, setSuccessMessage, setUndoDocument, undoDocument]);

  return {
    addAttachmentsFromDialog,
    addItem,
    addNonpartyCorp,
    copyOriginAndEnterpriseToFollowingRows,
    generateGoodsDescription,
    patchAttachment,
    patchDocument,
    patchItem,
    patchNonpartyCorp,
    removeAttachment,
    removeItem,
    removeNonpartyCorp,
    undoToolAction,
  };
}
