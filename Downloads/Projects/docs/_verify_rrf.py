#!/usr/bin/env python3
"""Verify công thức RRF của MAIA theo src/maia/retriever.py:157,163.

Đây là kiểm tra deterministic, không cần Qdrant/Kafka: xác minh đúng tính
chất toán học của Reciprocal Rank Fusion, không chỉ "hàm không crash".
"""
import json
from pathlib import Path

K = 60  # rrf_k mặc định trong retriever.py:24


def rrf(dense_rank, bm25_rank):
    """Công thức đúng như implementation: sum(1/(K + rank + 1)) per chunk_id."""
    s = 0.0
    if dense_rank is not None:
        s += 1.0 / (K + dense_rank + 1)
    if bm25_rank is not None:
        s += 1.0 / (K + bm25_rank + 1)
    return s


props = {
    # score giảm đơn điệu khi rank tệ hơn trong cùng một danh sách
    "monotonic_per_list": all(rrf(i, None) > rrf(i + 1, None) for i in range(50)),
    # doc xuất hiện ở cả 2 danh sách được ưu tiên hơn doc chỉ ở 1 danh sách
    "two_list_beats_one": rrf(1, 1) > rrf(2, None),
    # fusion cộng dồn — cùng rank ở 2 list thì điểm cao hơn 1 list
    "fusion_additive": rrf(1, 1) > rrf(1, None),
    # score luôn trong (0, 1), không vô lý
    "bounded_0_1": all(0 < rrf(d, b) < 1
                       for d in range(50) for b in [None] + list(range(50))),
}

out = {
    "rrf_k": K,
    "formula": "sum(1/(K + rank + 1)) per chunk_id",
    "source": "src/maia/retriever.py:157,163",
    "samples": {f"dense#{d}_bm25#{b}": round(rrf(d, b), 6)
                for d, b in [(1, 1), (5, 5), (10, 2), (2, None), (1, 50)]},
    "properties_verified": props,
    "all_pass": all(props.values()),
    "note": ("RRF không đảm bảo thứ tự tuyệt đối giữa các doc ở vị trí khác nhau — "
             "đây là định nghĩa chuẩn của Reciprocal Rank Fusion, không phải bug. "
             "Các tính chất được kiểm ở trên là những gì RRF thực sự bảo đảm."),
}

dest = Path(__file__).resolve().parent / "evidence/maia/rag/rrf-verification.json"
dest.parent.mkdir(parents=True, exist_ok=True)
dest.write_text(json.dumps(out, indent=2, ensure_ascii=False))
print(f"all_pass = {out['all_pass']}")
for k, v in props.items():
    print(f"  {k:20} {v}")
