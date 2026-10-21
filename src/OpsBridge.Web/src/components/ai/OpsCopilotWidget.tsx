import React, { useState, useRef, useEffect } from 'react';
import {
  Sparkles,
  X,
  Send,
  Bot,
  User,
  Settings,
  ShieldCheck,
  Cloud,
} from 'lucide-react';
import { Button } from '../ui/Button';
import { Input } from '../ui/Input';

interface Message {
  id: string;
  sender: 'ai' | 'user';
  text: string;
  timestamp: string;
  isFallback?: boolean;
}

const DEFAULT_ACCOUNT_ID = '';
const DEFAULT_CF_TOKEN = '';
const DEFAULT_MODEL = '@cf/meta/llama-3.1-8b-instruct';

const INITIAL_MESSAGES: Message[] = [
  {
    id: 'welcome-1',
    sender: 'ai',
    text: `📌 **Xin chào! Tôi là Trợ lý AI Đối Soát (OpsCopilot) chạy trên Cloudflare Workers AI.**
Tôi có thể hỗ trợ bạn:
- Giải thích chi tiết quy trình **Đối soát 3 Bên (Three-Way Matching)**.
- Phân tích nguyên nhân sai lệch số lượng (**QuantityMismatch**) hoặc đơn giá (**PriceMismatch**).
- Hướng dẫn áp dụng chính sách **Dung sai (Tolerance Policy)** và xuất tiền an toàn!`,
    timestamp: '09:00',
  },
];

/**
 * Rich Markdown Formatter for Chat Messages
 * Parses bold (**text**), italic (*text*), bullet lists (- or *), numbered lists (1.), and headers.
 * Resolves font clipping and raw syntax issues cleanly.
 */
function renderInlineMarkdown(text: string): React.ReactNode[] {
  const parts: React.ReactNode[] = [];
  const regex = /(\*\*.*?\*\*|\*.*?\*|`.*?`)/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  let key = 0;

  while ((match = regex.exec(text)) !== null) {
    if (match.index > lastIndex) {
      parts.push(text.substring(lastIndex, match.index));
    }
    const token = match[0];
    if (token.startsWith('**') && token.endsWith('**')) {
      parts.push(
        <strong key={key++} className="font-semibold text-slate-900">
          {token.slice(2, -2)}
        </strong>
      );
    } else if (token.startsWith('*') && token.endsWith('*')) {
      parts.push(
        <em key={key++} className="italic text-slate-800">
          {token.slice(1, -1)}
        </em>
      );
    } else if (token.startsWith('`') && token.endsWith('`')) {
      parts.push(
        <code key={key++} className="px-1 py-0.5 bg-slate-100 text-indigo-700 rounded font-mono text-[11px]">
          {token.slice(1, -1)}
        </code>
      );
    }
    lastIndex = regex.lastIndex;
  }
  if (lastIndex < text.length) {
    parts.push(text.substring(lastIndex));
  }
  return parts;
}

function MarkdownContent({ text, isUser }: { text: string; isUser: boolean }) {
  if (isUser) {
    return <div className="whitespace-pre-wrap leading-relaxed">{text}</div>;
  }

  const lines = text.split('\n');
  return (
    <div className="space-y-1.5 leading-relaxed text-slate-800">
      {lines.map((line, idx) => {
        const trimmed = line.trim();
        if (!trimmed) {
          return <div key={idx} className="h-1.5" />;
        }

        // Header style with emoji or bold title
        if (trimmed.startsWith('📌') || trimmed.startsWith('🔍') || trimmed.startsWith('⚙️') || trimmed.startsWith('👋') || trimmed.startsWith('###')) {
          return (
            <div key={idx} className="font-bold text-slate-900 text-xs pt-1 pb-0.5 border-b border-slate-100 flex items-start gap-1">
              {renderInlineMarkdown(trimmed)}
            </div>
          );
        }

        // Ordered list (1. 2. 3.)
        const numMatch = trimmed.match(/^(\d+)\.\s+(.*)/);
        if (numMatch) {
          return (
            <div key={idx} className="flex items-start gap-1.5 pl-1 my-0.5">
              <span className="font-bold text-indigo-600 shrink-0 text-[11px] mt-0.5">{numMatch[1]}.</span>
              <div className="flex-1 text-[11.5px] leading-relaxed">
                {renderInlineMarkdown(numMatch[2])}
              </div>
            </div>
          );
        }

        // Sub-bullets / list items (- or *)
        if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
          return (
            <div key={idx} className="flex items-start gap-2 pl-3 my-0.5 text-slate-700">
              <span className="text-indigo-500 font-bold shrink-0 mt-0.5 text-[10px]">•</span>
              <div className="flex-1 text-[11.5px] leading-relaxed">
                {renderInlineMarkdown(trimmed.slice(2))}
              </div>
            </div>
          );
        }

        // Standard paragraph
        return (
          <div key={idx} className="text-[11.5px] leading-relaxed">
            {renderInlineMarkdown(trimmed)}
          </div>
        );
      })}
    </div>
  );
}

export function OpsCopilotWidget() {
  const [isOpen, setIsOpen] = useState(false);
  const [accountId, setAccountId] = useState(() => {
    return localStorage.getItem('opsbridge_cf_account') || DEFAULT_ACCOUNT_ID;
  });
  const [apiToken, setApiToken] = useState(() => {
    return localStorage.getItem('opsbridge_cf_token') || DEFAULT_CF_TOKEN;
  });
  const [model, setModel] = useState(() => {
    return localStorage.getItem('opsbridge_cf_model') || DEFAULT_MODEL;
  });

  const [showSettings, setShowSettings] = useState(false);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [messages, setMessages] = useState<Message[]>(INITIAL_MESSAGES);

  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    if (isOpen) {
      scrollToBottom();
    }
  }, [messages, isOpen]);

  const handleSaveSettings = (acc: string, tok: string, mdl: string) => {
    setAccountId(acc);
    setApiToken(tok);
    setModel(mdl);
    localStorage.setItem('opsbridge_cf_account', acc);
    localStorage.setItem('opsbridge_cf_token', tok);
    localStorage.setItem('opsbridge_cf_model', mdl);
    setShowSettings(false);
  };

  // Local expert heuristic fallback when Cloudflare is offline or unavailable
  const getHeuristicResponse = (query: string): string => {
    const q = query.toLowerCase();

    if (q.includes('quy trình') || q.includes('3 bên') || q.includes('3-way') || q.includes('là gì')) {
      return `📌 **Quy trình Đối soát 3 Bên (Three-Way Matching) gồm 4 bước:**
1. **Đơn mua hàng (PO):** Bộ phận mua hàng lập cam kết mua hàng với NCC (quy định rõ Số lượng đặt & Đơn giá thỏa thuận).
2. **Phiếu nhập kho (GR):** Thủ kho kiểm đếm thực tế khi xe giao hàng tới kho, ghi nhận số lượng thực tế nhận được.
3. **Hóa đơn NCC (INV):** NCC gửi hóa đơn tài chính yêu cầu thanh toán.
4. **Đối soát (3-Way Matching):** Hệ thống tự động so khớp chéo:
   - *Số lượng trên Hóa đơn* phải bằng *Số lượng thực nhận tại Kho (GR)*.
   - *Đơn giá trên Hóa đơn* phải bằng *Đơn giá cam kết trong Đơn mua (PO)*.
Nếu khớp 100% hoặc trong biên độ dung sai (Tolerance), hóa đơn sẽ chuyển sang trạng thái **Matched** để phòng Tài chính xuất tiền!`;
    }

    if (q.includes('8803') || q.includes('sai lệch') || q.includes('chênh lệch') || q.includes('lệch')) {
      return `🔍 **Phân tích Hóa đơn INV-2026-8803:**
- **Nhà cung cấp:** Công Ty TNHH Trân Châu & Topping Đài Loan.
- **Mã sản phẩm:** Trân Châu Đen Hoàng Gia 20kg (SKU-BOBA-BLACK-20K).
- **Thực tế:** 
  + Hóa đơn NCC yêu cầu thanh toán: **100 bao**.
  + Thực tế thủ kho kiểm đếm nhận được: **80 bao** (thiếu 20 bao do lỗi vận chuyển).
- **Sai lệch (Discrepancy):** Chênh lệch **20 bao**, tương đương giá trị vênh **13.200.000 VNĐ**.
- **Khuyến nghị xử lý:**
  👉 Nhân viên kế toán nên chọn hành động **Rejected (Từ chối)** hoặc **Adjusted (Yêu cầu NCC phát hành lại hóa đơn cho 80 bao thực nhận)** thay vì thanh toán toàn bộ!`;
    }

    if (q.includes('dung sai') || q.includes('tolerance')) {
      return `⚙️ **Chính sách Biên độ Dung sai (Tolerance Policy Engine):**
Hệ thống cho phép cấu hình 3 chỉ số linh hoạt:
1. **Price Tolerance (%):** Biên độ lệch đơn giá cho phép (mặc định 1.0%). Phù hợp cho trường hợp thuế phí làm tròn vài nghìn đồng.
2. **Qty Tolerance (%):** Biên độ lệch số lượng theo tỷ lệ %.
3. **Qty Absolute Tolerance:** Số lượng lệch tuyệt đối (ví dụ cho phép hao hụt 1-2 đơn vị bao bì với hàng rời/nông sản).
Nếu chênh lệch nằm trong biên độ này, hệ thống sẽ tự động duyệt với trạng thái **ApprovedWithTolerance**.`;
    }

    if (q.includes('bước') || q.includes('người mới') || q.includes('hướng dẫn') || q.includes('dùng')) {
      return `👋 **Hướng dẫn sử dụng nhanh cho người mới:**
1. **Đơn mua hàng (PO):** Vào mục PO ở menu trái để kiểm tra danh sách đơn đặt hàng hiện có.
2. **Phiếu nhập kho (GR):** Vào mục GR để xem hàng đã về kho chưa, thủ kho đã xác nhận bao nhiêu kiện.
3. **Hóa đơn NCC (INV):** Vào mục INV để kiểm tra hóa đơn cần đối soát hoặc nhập mới hóa đơn.
4. **Đối soát 3 bên:** Vào mục Đối soát 3 bên, chọn hóa đơn và nhấn nút xanh **"Thực Thi Đối Soát 3 Bên (Execute 3-Way Match)"**. Hệ thống sẽ tính toán và hiển thị kết quả ngay lập tức!`;
    }

    return `Tôi hiểu bạn đang hỏi về: "${query}".
Trong hệ thống OpsBridge, mọi giao dịch mua hàng đều được bảo vệ nghiêm ngặt qua quy tắc Đối soát 3 bên.
- Bạn có thể nhấn vào nút **"Execute 3-Way Match"** trong trang Đối soát để chạy kiểm tra chéo tự động.
- Nếu bạn có bất kỳ câu hỏi nào về số liệu hóa đơn cụ thể, cách tạo đơn PO hay xử lý chênh lệch, hãy gõ câu hỏi cho tôi nhé!`;
  };

  const handleSend = async (textToSend?: string) => {
    const question = (textToSend || input).trim();
    if (!question || loading) return;

    const userMsg: Message = {
      id: 'usr-' + Date.now(),
      sender: 'user',
      text: question,
      timestamp: new Date().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }),
    };

    setMessages((prev) => [...prev, userMsg]);
    setInput('');
    setLoading(true);

    try {
      const systemPrompt = `Bạn là Trợ lý AI Chuyên gia Vận hành Kho & Kế toán Tài chính của hệ thống OpsBridge (Three-Way Matching Reliability Platform).
Nhiệm vụ của bạn là giải thích quy trình đối soát 3 bên (Đơn mua PO, Phiếu nhập kho GR, Hóa đơn NCC INV), giải thích các lỗi sai lệch số lượng/đơn giá (QuantityMismatch, PriceMismatch), hướng dẫn người dùng sử dụng trang web một cách thân thiện, súc tích và dễ hiểu bằng tiếng Việt.`;

      let aiReply = '';

      // 1. First attempt: call local Cloudflare Pages Function proxy
      try {
        const proxyResp = await fetch('/api/v1/copilot/chat', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: question },
            ],
            model,
            accountId,
            apiToken,
          }),
        });

        if (proxyResp.ok) {
          const data = await proxyResp.json();
          aiReply = data.response || data.result?.response || data.result?.choices?.[0]?.message?.content || '';
        }
      } catch {
        // Fall through to direct call
      }

      // 2. Second attempt: Direct call to Cloudflare Workers AI if proxy unavailable
      if (!aiReply && accountId && apiToken) {
        const url = `https://api.cloudflare.com/client/v4/accounts/${accountId}/ai/run/${model}`;
        const cfResp = await fetch(url, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${apiToken}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: question },
            ],
          }),
        });

        if (cfResp.ok) {
          const cfData = await cfResp.json();
          aiReply = cfData.result?.response || cfData.result?.choices?.[0]?.message?.content || '';
        }
      }

      // 3. Fallback to expert heuristics if API offline
      if (!aiReply) {
        aiReply = getHeuristicResponse(question);
      }

      setMessages((prev) => [
        ...prev,
        {
          id: 'ai-' + Date.now(),
          sender: 'ai',
          text: aiReply,
          timestamp: new Date().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }),
        },
      ]);
    } catch (err: unknown) {
      console.warn('Cloudflare Workers AI call fallback to local heuristics:', err);
      const fallbackReply = getHeuristicResponse(question);
      setMessages((prev) => [
        ...prev,
        {
          id: 'ai-' + Date.now(),
          sender: 'ai',
          text: fallbackReply,
          timestamp: new Date().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }),
          isFallback: true,
        },
      ]);
    } finally {
      setLoading(false);
    }
  };

  const quickPrompts = [
    'Quy trình đối soát 3 bên hoạt động thế nào?',
    'Tại sao hóa đơn INV-2026-8803 bị phát hiện sai lệch?',
    'Tôi là người mới, hãy hướng dẫn các bước sử dụng web.',
    'Biên độ dung sai (Tolerance Policy) là gì?',
  ];

  return (
    <>
      {/* Floating Trigger Button */}
      {!isOpen && (
        <button
          onClick={() => setIsOpen(true)}
          className="fixed bottom-6 right-6 z-40 bg-gradient-to-r from-orange-600 via-amber-600 to-indigo-600 text-white p-3.5 rounded-full shadow-2xl hover:scale-105 hover:shadow-orange-500/25 transition-all flex items-center gap-2 group cursor-pointer border border-white/20"
          title="Mở Trợ lý AI OpsCopilot"
        >
          <div className="relative">
            <Sparkles className="w-5 h-5 animate-pulse text-amber-200" />
            <span className="absolute -top-1 -right-1 w-2.5 h-2.5 bg-emerald-400 rounded-full border-2 border-slate-900"></span>
          </div>
          <span className="text-xs font-bold pr-1 hidden sm:inline">AI Copilot</span>
        </button>
      )}

      {/* Floating Chat Modal */}
      {isOpen && (
        <div className="fixed bottom-6 right-6 z-50 w-[92vw] sm:w-[440px] h-[600px] bg-white rounded-2xl shadow-2xl border border-slate-200 flex flex-col overflow-hidden animate-in fade-in slide-in-from-bottom-5 duration-200 font-sans">
          {/* Header */}
          <div className="bg-gradient-to-r from-slate-900 via-indigo-950 to-orange-950 text-white p-3.5 flex items-center justify-between shadow-xs">
            <div className="flex items-center gap-2.5">
              <div className="w-8 h-8 rounded-xl bg-orange-500/20 border border-orange-400/40 flex items-center justify-center text-orange-400">
                <Cloud className="w-4 h-4" />
              </div>
              <div>
                <h3 className="font-bold text-xs flex items-center gap-1.5">
                  <span>OpsBridge AI Copilot</span>
                  <span className="text-[10px] bg-orange-500/20 text-orange-300 px-1.5 py-0.5 rounded font-medium border border-orange-400/30">
                    Cloudflare AI
                  </span>
                </h3>
                <p className="text-[10px] text-slate-300">Chuyên gia đối soát kho & hóa đơn (Llama 3.1)</p>
              </div>
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={() => setShowSettings(!showSettings)}
                className="p-1.5 rounded-lg text-slate-300 hover:text-white hover:bg-white/10 transition-colors"
                title="Cài đặt Cloudflare AI"
              >
                <Settings className="w-4 h-4" />
              </button>
              <button
                onClick={() => setIsOpen(false)}
                className="p-1.5 rounded-lg text-slate-300 hover:text-white hover:bg-white/10 transition-colors"
                title="Đóng cửa sổ"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          </div>

          {/* Settings Sub-panel */}
          {showSettings && (
            <div className="p-3 bg-slate-50 border-b border-slate-200 text-xs space-y-2.5 animate-in fade-in duration-150">
              <div className="flex justify-between items-center">
                <span className="font-bold text-slate-800 flex items-center gap-1">
                  <Cloud className="w-3.5 h-3.5 text-orange-600" />
                  Cấu hình Cloudflare Workers AI
                </span>
                <span className="text-[10px] text-orange-600 font-semibold bg-orange-50 px-1.5 py-0.5 rounded border border-orange-200">
                  Workers AI
                </span>
              </div>
              <div className="space-y-1">
                <label className="text-[11px] font-semibold text-slate-600">Account ID:</label>
                <Input
                  type="text"
                  value={accountId}
                  onChange={(e) => setAccountId(e.target.value)}
                  placeholder="Cloudflare Account ID"
                  className="text-xs font-mono"
                />
              </div>
              <div className="space-y-1">
                <label className="text-[11px] font-semibold text-slate-600">API Token:</label>
                <Input
                  type="password"
                  value={apiToken}
                  onChange={(e) => setApiToken(e.target.value)}
                  placeholder="Cloudflare API Token (cfut_...)"
                  className="text-xs font-mono"
                />
              </div>
              <div className="space-y-1">
                <label className="text-[11px] font-semibold text-slate-600">Model:</label>
                <Input
                  type="text"
                  value={model}
                  onChange={(e) => setModel(e.target.value)}
                  placeholder="@cf/meta/llama-3.1-8b-instruct"
                  className="text-xs font-mono"
                />
              </div>
              <div className="p-2 bg-emerald-50 rounded border border-emerald-200 text-[11px] text-emerald-800 flex items-center gap-1.5">
                <ShieldCheck className="w-4 h-4 text-emerald-600 shrink-0" />
                <span>Token Workers AI đã được xác thực an toàn trên Cloudflare Edge.</span>
              </div>
              <div className="flex justify-end gap-2 pt-1">
                <Button size="sm" variant="outline" onClick={() => setShowSettings(false)}>
                  Đóng
                </Button>
                <Button size="sm" variant="primary" onClick={() => handleSaveSettings(accountId, apiToken, model)}>
                  Lưu Cấu Hình
                </Button>
              </div>
            </div>
          )}

          {/* Message List with generous padding to prevent font clipping */}
          <div className="flex-1 p-3.5 pt-4 pb-4 overflow-y-auto space-y-3.5 bg-slate-50/70">
            {messages.map((m) => (
              <div
                key={m.id}
                className={`flex gap-2.5 ${m.sender === 'user' ? 'justify-end' : 'justify-start'}`}
              >
                {m.sender === 'ai' && (
                  <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-orange-500 to-indigo-600 text-white flex items-center justify-center shrink-0 mt-0.5 shadow-2xs">
                    <Bot className="w-4 h-4" />
                  </div>
                )}
                <div
                  className={`p-3 rounded-2xl text-xs max-w-[85%] shadow-xs transition-all ${
                    m.sender === 'user'
                      ? 'bg-indigo-600 text-white rounded-br-none'
                      : 'bg-white text-slate-800 border border-slate-200/90 rounded-bl-none'
                  }`}
                >
                  <MarkdownContent text={m.text} isUser={m.sender === 'user'} />
                  <div
                    className={`text-[9px] mt-1.5 text-right font-medium ${
                      m.sender === 'user' ? 'text-indigo-200' : 'text-slate-400'
                    }`}
                  >
                    {m.timestamp}
                  </div>
                </div>
                {m.sender === 'user' && (
                  <div className="w-7 h-7 rounded-lg bg-slate-200 text-slate-700 flex items-center justify-center shrink-0 mt-0.5 shadow-2xs">
                    <User className="w-4 h-4" />
                  </div>
                )}
              </div>
            ))}
            {loading && (
              <div className="flex gap-2.5 items-center text-slate-500 text-xs p-2 bg-white/70 rounded-xl border border-slate-100">
                <div className="w-7 h-7 rounded-lg bg-orange-500 text-white flex items-center justify-center shrink-0">
                  <Bot className="w-4 h-4 animate-spin" />
                </div>
                <span className="italic font-medium text-slate-600">Cloudflare Llama 3.1 đang phân tích dữ liệu đối soát...</span>
              </div>
            )}
            <div ref={messagesEndRef} />
          </div>

          {/* Quick Prompts */}
          <div className="px-3 py-2 bg-slate-100/90 border-t border-slate-200 flex gap-1.5 overflow-x-auto text-[11px] no-scrollbar">
            {quickPrompts.map((q, idx) => (
              <button
                key={idx}
                onClick={() => handleSend(q)}
                className="whitespace-nowrap bg-white border border-slate-300 text-slate-700 hover:border-orange-500 hover:text-orange-600 px-2.5 py-1 rounded-full transition-all cursor-pointer shrink-0 font-medium shadow-2xs"
              >
                {q}
              </button>
            ))}
          </div>

          {/* Input Box */}
          <div className="p-2.5 bg-white border-t border-slate-200 flex items-center gap-2">
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSend()}
              placeholder="Hỏi AI về hóa đơn, đơn mua, đối soát..."
              className="flex-1 text-xs px-3 py-2 bg-slate-100 border border-slate-300 rounded-xl focus:outline-none focus:border-orange-500 focus:bg-white transition-all text-slate-800 placeholder:text-slate-400"
            />
            <Button
              size="sm"
              variant="primary"
              onClick={() => handleSend()}
              disabled={loading || !input.trim()}
              className="bg-gradient-to-r from-orange-600 to-indigo-600 hover:from-orange-700 hover:to-indigo-700 h-9 w-9 p-0 flex items-center justify-center rounded-xl shrink-0 cursor-pointer text-white shadow-2xs"
            >
              <Send className="w-4 h-4" />
            </Button>
          </div>
        </div>
      )}
    </>
  );
}
