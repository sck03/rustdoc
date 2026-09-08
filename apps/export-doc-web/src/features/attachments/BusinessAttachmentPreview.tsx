import { useEffect, useRef, useState } from "react";

export function BusinessAttachmentPreview({ blob, name, onClose }: { blob: Blob; name: string; onClose: () => void }) {
  const [url, setUrl] = useState("");
  const region = useRef<HTMLElement>(null);
  useEffect(() => {
    const value = URL.createObjectURL(blob);
    setUrl(value);
    region.current?.focus();
    return () => URL.revokeObjectURL(value);
  }, [blob]);
  return <section className="attachment-preview" aria-label={`预览 ${name}`} ref={region} tabIndex={-1}>
    <div className="business-records-heading"><h3>{name}</h3><button className="command-button secondary" type="button" onClick={onClose}>关闭预览</button></div>
    {url && (blob.type === "application/pdf" ? <iframe src={url} title={`PDF 预览：${name}`} referrerPolicy="no-referrer" /> : <img src={url} alt={name} />)}
  </section>;
}
