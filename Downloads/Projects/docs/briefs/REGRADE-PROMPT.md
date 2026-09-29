# REGRADE #1 — Prompt chấm baseline (design-only)

> **Mục đích:** lấy điểm thiết kế khi **chưa có runtime evidence**. Đây là mốc so sánh công bằng — sau khi chạy test, mọi tăng điểm đều đo được.
>
> | | |
> |---|---|
> | **File** | `DESIGN-BRIEF-v2-frozen.md` (647 dòng) |
> | **SHA-256** | `f2a7c054b85f34961091401da8881b1dfb29aecdd004766df5c47811830cf137` |
> | **Trạng thái** | Đã freeze. **Không chạy test, chưa tạo `evidence/`** — đúng theo thiết kế baseline |
> | **Lịch sử** | v1 chấm 82/100 → sửa 12 lỗi → v2 |

---

## Prompt gửi GPT (copy nguyên khối dưới)

```
Bạn là chuyên gia kiến trúc dữ liệu/ML với 15+ năm kinh nghiệm.

Tôi gửi kèm DESIGN-BRIEF-v2-frozen.md (647 dòng) — bản v2 của một design
brief portfolio gồm 7 repo. Bản v1 đã được chấm 82/100; v2 sửa 12 lỗi được
chỉ ra. Hãy chấm lại v2.

BỐI CẢNH CẦN BIẾT TRƯỚC:
Đây là lần chấm BASELINE (design-only). Tôi CỐ TÌNH chưa chạy test, chưa
deploy, chưa tạo evidence runtime nào. Mục tiêu là biết thiết kế tốt đến
đâu khi chưa có bằng chứng thực thi — để sau này đo được code làm tăng
bao nhiêu điểm.

QUY TẮC BẮT BUỘC:
1. Dùng ĐÚNG rubric 8 tiêu chí / 100 điểm trong Phần A. Không đổi tiêu chuẩn,
   không thêm tiêu chí, không rescale. (Giữ nguyên cả ở lần chấm sau.)
2. Đọc Phần K TRƯỚC khi chấm. Đó là self-audit liệt kê 12 lỗi đã sửa + 6 điểm
   yếu còn tồn tại + giới hạn của chính tài liệu.
3. KHÔNG mặc định cho điểm tăng vì "đã sửa". Sửa đúng thì ghi nhận; sửa nửa
   hoặc sinh mâu thuẫn mới thì trừ.
4. Chấm độc lập — điểm 82 của v1 chỉ là tham khảo, không phải neo.
5. Phần này CHƯA có runtime evidence, và đó là chủ ý. Đừng trừ điểm vì thiếu
   evidence — hãy ghi nó vào mục (6) bên dưới để làm spec cho bước sau.

ĐỊNH DẠNG ĐẦU RA:

1. BẢNG ĐIỂM 8 tiêu chí + tổng

2. PHẦN K — trả lời riêng 3 câu:
   a) Trong 12 lỗi ở K.1, lỗi nào sửa NỬA KHÔNG ĐÚT? Sai ở đâu?
   b) Trong 6 điểm yếu ở K.2, điểm nào bị đánh giá NHẸ hơn thực tế?
   c) K.3 nói "chưa có gì kiểm chứng bằng thực thi". Bạn đồng ý không?

3. ĐIỂM MẠNH — dẫn chứng số dòng cụ thể

4. ĐIỂM YẾU CÒN LẠI — KHÔNG tính lại 12 lỗi đã sửa. Đây là phần quan trọng nhất.

5. TOP 5 việc nên làm tiếp, theo thứ tự ưu tiên

6. BẢNG "CLAIM CẦN EVIDENCE" — mục tiêu phụ nhưng cần thiết.
   Với MỖI ô trong ma trận coverage (D.3) đang là ✅ hoặc 🟡, cho biết:
     - Kiến trúc + repo
     - Claim cụ thể mà ô đó đại diện
     - Bằng chứng runtime tối thiểu cần có để ô đó được coi là "đã chứng minh"
       (VD: ô ✅ Streaming ở FlashSale cần producer→broker→consumer + test
        idempotency/DLQ, KHÔNG chấp nhận "đọc code thấy có RabbitMQ")
     - Lệnh kiểm tra cụ thể sẽ chạy được
   Tôi sẽ dùng bảng này làm spec cho verify_matrix.py.
   Ô 🟡 phải nêu rõ CÒN THIẾU GÌ, không được ghi chung chung "chưa đủ".

Nếu vẫn chưa đạt 90, nói thẳng là chưa đạt và nêu lý do cụ thể.
```

---

## Ghi chú khi tự chấm (không gửi GPT)

### Vì sao thêm mục (6) vào prompt

Bản v1 không có yêu cầu này nên không biết cần chứng minh claim nào. Mục (6) biến **REGRADE #1** thành nguồn spec cho `verify_matrix.py` — tức là vòng chấm đầu tiên đã sinh ra việc cụ thể cho vòng sau, thay vì chỉ ra điểm rồi dừng.

Nếu GPT làm tốt, `docs/evidence/` cuối cùng sẽ có đúng những gì GPT liệt kê, không phải thứ tôi tự nghĩ ra.

### Dự báo điểm (có thể sai)

| Tiêu chí | Kỳ vọng | Lý do |
|-----------|---------|-------|
| 3 — Nhất quán | **Tăng rõ** | Đã sửa 6 mâu thuẫn thật |
| 1 — Chính xác kỹ thuật | Tăng nhẹ | MemorySaver, ACR, MCP framing |
| 2 — Chiều sâu | **Có thể giảm** | v2 thừa nhận nhiều giới hạn hơn |
| 4 — Roadmap | **Có thể giảm** | E.4 Apex hạ từ "4 module" xuống "đo trước" |
| 5 — Azure | Không chắc | Đã sửa 2 lỗi lớn nhưng R1 quota chưa verify |

**Dự báo: 82–88.** Nếu vượt 90 mà chưa có runtime evidence, cần hỏi lại xem GPT có đang chấm sự trung thực (thừa nhận giới hạn) hay đang chấm độ dài tài liệu.

**82–88 là kết quả có giá trị hơn 90 bịa** — vì nó đo được, còn 90 thì không.

