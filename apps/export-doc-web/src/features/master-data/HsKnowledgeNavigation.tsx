import { BookOpen, CloudDownload, Database, Download, GraduationCap, Search } from "lucide-react";
import { Link } from "react-router-dom";

export const hsKnowledgeSections = [
  { key: "search", label: "智能查询", to: "/master-data/hs-knowledge/search", icon: Search },
  { key: "catalog", label: "税则目录", to: "/master-data/hs-codes", icon: Database },
  { key: "examples", label: "申报实例库", to: "/master-data/hs-knowledge/examples", icon: BookOpen },
  { key: "history", label: "历史资料学习", to: "/master-data/hs-knowledge/history", icon: GraduationCap },
  { key: "online", label: "联网补充", to: "/master-data/hs-knowledge/online", icon: CloudDownload },
  { key: "annual", label: "年度税则导入", to: "/master-data/hs-knowledge/annual", icon: Database, manage: true },
  { key: "transfer", label: "知识库导入导出", to: "/master-data/hs-knowledge/transfer", icon: Download, manage: true },
] as const;

export function HsKnowledgeNavigation({ activeSection, canManage }: { activeSection: string; canManage: boolean }) {
  return <nav className="hs-knowledge-nav" aria-label="HS知识中心功能">
    {hsKnowledgeSections.filter((item) => !("manage" in item) || canManage).map((item) => {
      const Icon = item.icon;
      return <Link key={item.key} to={item.to} className={activeSection === item.key ? "active" : ""} aria-current={activeSection === item.key ? "page" : undefined}>
        <Icon size={18} aria-hidden="true" /><span>{item.label}</span>
      </Link>;
    })}
  </nav>;
}
