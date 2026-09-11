export function SettingsSectionNav<T extends string>({ label, sections, activeSection, onSelect }: {
  label: string;
  sections: readonly { key: T; label: string }[];
  activeSection: T;
  onSelect: (section: T) => void;
}) {
  return <nav className="settings-section-nav" aria-label={label}>
    {sections.map((section) => <button key={section.key} type="button"
      className={activeSection === section.key ? "command-button" : "command-button secondary"}
      aria-current={activeSection === section.key ? "page" : undefined}
      onClick={() => onSelect(section.key)}>{section.label}</button>)}
  </nav>;
}
