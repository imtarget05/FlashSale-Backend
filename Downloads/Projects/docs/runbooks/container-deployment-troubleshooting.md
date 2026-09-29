# Ghi lại ba lỗi deploy đã tốn thời gian, vì cả ba đều trông giống hỏng
# quyền và dẫn tới chẩn đoán sai.

## 1. Tên ảnh không có namespace owner → Docker Hub trả 401

Triệu chứng:

```
unauthorized: authentication required
```

Đọc như thế thì rất dễ kết luận token của GitHub Actions không đủ quyền
`packages: write`, và đi sửa quyền — trong khi quyền vốn đã đúng.

Nguyên nhân: `IMAGE_NAME=maia-api` mà không có phần owner. Docker mặc định
resolve tên không có slash thành `docker.io/library/...`, nên ảnh bị push thẳng
về Docker Hub thay vì GHCR. Hub yêu cầu đăng nhập, Hub không biết `GITHUB_TOKEN`.

Cách phát hiện: 401 đến từ `docker.io` trong log, không phải từ `ghcr.io`.

Cách tránh: luôn dựng tên đầy đủ
`ghcr.io/${GITHUB_REPOSITORY_OWNER}/${repo_lc}-${image_name}`.

## 2. Tên repository viết hoa → OCI registry từ chối

Triệu chứng:

```
invalid reference format: repository name must be lowercase
```

Đây thì thông báo đã chỉ đúng chỗ, nhưng chỉ xuất hiện lúc push, sau khi
build đã tốn thời gian. Rẻ hơn nhiều nếu chuẩn hóa tên ngay trong bước resolve
tag:

```bash
REPO_LC="$(printf '%s' "${GITHUB_REPOSITORY#*/}" | tr '[:upper:]' '[:lower:]')"
```

## 3. Push theo digest rồi gắn tag → tag không pull được

Triệu chứng:

```
manifest unknown
```

khi kéo bằng tag, dù `docker push` báo thành công.

Nguyên nhân: build theo digest (`push-by-digest`) rồi gắn tag ở bước riêng là
mẫu thứ hai, và nếu bước gắn tag chạy trên một runner khác thì manifest đó
không tồn tại trong registry local của runner đó. Tag được tạo ra nhưng trỏ
tới một manifest chưa từng được đẩy lên.

Cách tránh ở đây: dùng một lần `docker/build-push-action` với
`tags: $IMAGE:$SHA`. Bằng chứng provenance lấy từ `steps.build.outputs.digest`,
tức digest mà chính step đó xác nhận đã push. Nếu sau này thực sự cần
push-by-digest thì bước gắn tag phải chạy trên cùng runner.

---

## Phụ đề: `COPY ... 2>/dev/null || true` không hợp lệ

Không thuộc ba lỗi trên nhưng gặp ngay sau đó, và thông báo lỗi cũng gây hiểu
sai:

```
failed to calculate checksum of ref ...: "/||": not found
```

`COPY` không chạy shell, nên `||` bị Docker đọc thành tên đường dẫn. Không có
`data/` thì build vẫn lỗi, nhưng vì một lý do khác hẳn. Nếu đường dẫn là tùy
chọn, hãy kiểm tra trước ở `RUN`, hoặc chấp nhận rằng nó bắt buộc phải tồn
tại.

## Phụ đề: suy ra requirements từ một file là chưa đủ

Không thuộc ba lỗi registry nhưng là lỗi tốn thời gian nhất khi deploy MAIA.
Image build xanh rồi mới crash lúc uvicorn import với
`ModuleNotFoundError: passlib`. Quét AST của `api.py` không thấy `passlib`
vì nó nằm trong `maia/auth.py`.

Dùng `tools/import_closure.py` để lấy closure thật. Cần phân biệt import ở
module scope (chặn khởi động) với import trong thân hàm (chỉ chạy khi hàm được
gọi), và phải nạp `__init__.py` của mọi package cha. Chi tiết và các lỗi
của chính tool nằm trong docstring của nó.
