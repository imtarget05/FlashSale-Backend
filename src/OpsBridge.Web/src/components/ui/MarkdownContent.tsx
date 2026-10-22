import React from 'react';

interface MarkdownContentProps {
  content: string;
  className?: string;
}

/**
 * Lightweight, safe Markdown renderer for OpsBridge AI Copilot and system descriptions.
 * Handles:
 * - Bold (**text**)
 * - Italic (*text*)
 * - Inline code (`code`)
 * - Bullet lists (- item or * item)
 * - Numbered lists (1. item)
 * - Headers (### Header)
 * - Paragraph breaks
 */
export function MarkdownContent({ content, className = '' }: MarkdownContentProps) {
  if (!content) return null;

  // Split lines
  const lines = content.split('\n');
  const elements: React.ReactNode[] = [];

  let inList: 'ul' | 'ol' | null = null;
  let listItems: React.ReactNode[] = [];

  const flushList = (key: string) => {
    if (inList === 'ul' && listItems.length > 0) {
      elements.push(
        <ul key={`ul-${key}`} className="my-1.5 ml-4 list-disc space-y-1 text-slate-800">
          {listItems}
        </ul>
      );
    } else if (inList === 'ol' && listItems.length > 0) {
      elements.push(
        <ol key={`ol-${key}`} className="my-1.5 ml-4 list-decimal space-y-1 text-slate-800">
          {listItems}
        </ol>
      );
    }
    inList = null;
    listItems = [];
  };

  const renderInline = (text: string): React.ReactNode[] => {
    // Regex for bold (**text**), italic (*text*), inline code (`code`)
    const parts: React.ReactNode[] = [];
    let remaining = text;
    let keyIdx = 0;

    // Pattern matches: **bold**, *italic*, `code`
    const regex = /(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`)/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = regex.exec(text)) !== null) {
      // Add text before match
      if (match.index > lastIndex) {
        parts.push(text.substring(lastIndex, match.index));
      }

      const token = match[0];
      if (token.startsWith('**') && token.endsWith('**')) {
        parts.push(
          <strong key={`b-${keyIdx++}`} className="font-semibold text-slate-900">
            {token.slice(2, -2)}
          </strong>
        );
      } else if (token.startsWith('*') && token.endsWith('*')) {
        parts.push(
          <em key={`i-${keyIdx++}`} className="italic text-slate-700">
            {token.slice(1, -1)}
          </em>
        );
      } else if (token.startsWith('`') && token.endsWith('`')) {
        parts.push(
          <code
            key={`c-${keyIdx++}`}
            className="px-1 py-0.5 bg-slate-100 text-indigo-700 font-mono text-[11px] rounded border border-slate-200"
          >
            {token.slice(1, -1)}
          </code>
        );
      }

      lastIndex = regex.lastIndex;
    }

    if (lastIndex < text.length) {
      parts.push(text.substring(lastIndex));
    }

    return parts.length > 0 ? parts : [remaining];
  };

  lines.forEach((line, idx) => {
    const trimmed = line.trim();

    if (!trimmed) {
      flushList(`flush-${idx}`);
      return;
    }

    // Header 3 (###) or Header 2 (##) or Header 1 (#)
    if (trimmed.startsWith('### ')) {
      flushList(`h3-${idx}`);
      elements.push(
        <h4 key={`h-${idx}`} className="text-xs font-bold text-slate-900 mt-2 mb-1">
          {renderInline(trimmed.substring(4))}
        </h4>
      );
      return;
    }
    if (trimmed.startsWith('## ')) {
      flushList(`h2-${idx}`);
      elements.push(
        <h3 key={`h-${idx}`} className="text-sm font-bold text-slate-900 mt-2.5 mb-1.5">
          {renderInline(trimmed.substring(3))}
        </h3>
      );
      return;
    }

    // Bullet list: starts with "- " or "* "
    if (/^[-*]\s+/.test(trimmed)) {
      if (inList !== 'ul') {
        flushList(`switch-ul-${idx}`);
        inList = 'ul';
      }
      const itemText = trimmed.replace(/^[-*]\s+/, '');
      listItems.push(
        <li key={`li-${idx}`} className="leading-relaxed">
          {renderInline(itemText)}
        </li>
      );
      return;
    }

    // Numbered list: starts with "1. ", "2. ", etc.
    const numMatch = trimmed.match(/^(\d+)\.\s+(.*)/);
    if (numMatch) {
      if (inList !== 'ol') {
        flushList(`switch-ol-${idx}`);
        inList = 'ol';
      }
      listItems.push(
        <li key={`li-${idx}`} className="leading-relaxed">
          {renderInline(numMatch[2])}
        </li>
      );
      return;
    }

    // Regular line
    flushList(`line-${idx}`);
    elements.push(
      <p key={`p-${idx}`} className="leading-relaxed my-1">
        {renderInline(trimmed)}
      </p>
    );
  });

  flushList('end');

  return <div className={`space-y-1 ${className}`}>{elements}</div>;
}
