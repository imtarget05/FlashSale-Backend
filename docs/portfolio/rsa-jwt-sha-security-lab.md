# Lab bảo mật: RSA, JWT, SHA — FlashSale

## 1. Tổng quan

Demo của FlashSale sử dụng **JWT với HMAC-SHA256 (HS256)** cho xác thực stateless; profile production đã chuyển request authentication sang OIDC resource server. Lab này đối chiếu yêu cầu JD Sacombank ("JWT, RSA, SHA") và giải thích tại sao demo chọn HS256 thay vì RSA, cùng các bản mã hóa (SHA, BCrypt) hiện đang triển khai.

## 2. JWT implementasi hiện tại

| Thành phần | Triển khai | Ghi chú |
|-----------|-----------|---------|
| JWT library | `io.jsonwebtoken:jjwt-api/impl/jackson` 0.12.3 | Có trong `pom.xml` |
| Ký token | HMAC-SHA256 (HS256) qua `Keys.hmacShaKeyFor()` | SecretKey từ `JWT_SIGNING_KEY` |
| Thuật toán băm | SHA-256 (bên trong HS256) | Không gọi trực tiếp SHA, HS256 đã dùng SHA-256 internally |
| Password hash | BCrypt qua `BCryptPasswordEncoder` | Spring Security, salt tự sinh |
| Token expiration | 86400000 ms (24 giờ) | Cấu hình qua `app.jwt.expiration-ms` cho JWT demo HS256 |
| Key rotation | HS256 demo: chưa có; OIDC: issuer publish signing keys qua JWKS | Local simulated-JWKS overlap test verifies one decoder accepts a new key; no live provider rotation has run |
| Key size tối thiểu | 32 bytes for HS256 | `JwtStartupValidator` checks non-production profiles only |

## 3. Tại sao chọn HS256 thay vì RSA

| Tiêu chí | HS256 (hiện tại) | RSA (RS256) |
|----------|-----------------|-------------|
| Khóa | Một khóa đối xứng (shared secret) | Cặp khóa công/riêng |
| Phát hành + verify | Cùng một service | Verify có thể dùng public key riêng biệt |
| Key rotation | Cần đồng bộ secret trên tất cả instances | Public key có thể publish rộng rãi, private key bảo mật ở authorization server |
| Phù hợp microservice đơn | ✅ Đơn giản, không cần key management phức tạp | ⚠️ Cần auth server riêng để ký, resource server để verify |
| Kém nhất | Nếu secret rò rỉ, toàn bộ hệ thống bị tổn hại | Private key rò rỉ cũng nguy hiểm, nhưng public key có thể công khai |

**Kết luận**: Với một dịch vụ thanh toán đơn (payment-service), HS256 là lựa chọn đơn giản và đủ an toàn nếu `JWT_SIGNING_KEY` được quản lý qua secret store (Azure Key Vault / env var) và rotate định kỳ. RSA được khuyến nghị khi hệ thống tách auth server và resource server, hoặc cần verify token mà không cần biết secret ký.

**Đánh giá JWT**: `PARTIAL` — JWT HS256 đã triển khai và kiểm thử; RSA chưa có.

Production request authentication uses the OIDC resource-server decoder and does
not require `JWT_SIGNING_KEY`; the legacy HS256 key is still used outside the
`production`/`prod` profiles. OIDC rotation is the issuer's JWKS responsibility.
The local test at
[`OidcJwksRotationTest`](../../src/payment-service-java/src/test/java/com/flashsale/payment/security/OidcJwksRotationTest.java)
simulates publishing old and new public keys together and verifies that the
decoder accepts tokens signed by both. It does not verify a live issuer,
provider-specific cache behavior, or an operational rotation runbook.

## 4. Mã hóa (SHA / BCrypt)

| Mục | Thuật toán | Trạng thái |
|-----|-----------|-----------|
| JWT signing | HMAC-SHA256 | ✅ Đã triển khai |
| Password hash | BCrypt | ✅ Đã triển khai (`PasswordEncoder`) |
| Mã hóa dữ liệu nhạy cảm | Chưa có | Cần thêm nếu lưu thẻ/tài khoản nhạy cảm |
| TLS/SSL | Chưa cấu hình trong code | Cần cấu hình ở reverse proxy/ingress (production) |

**Đánh giá mã hóa**: `PARTIAL` — SHA (bên trong HS256) và BCrypt đã có; mã hóa dữ liệu ở rest chưa có.

## 5. OWASP controls

| Control | Triển khai | Ghi chú |
|---------|-----------|---------|
| Input validation | `@Valid`, `@NotBlank`, `@Size`, `@DecimalMin` | ✅ Trong `ProcessPaymentRequest` |
| Authentication failure | 401/403 fail closed | ✅ Production OIDC requires issuer/audience; local HS256 startup rejects missing or weak key |
| Authorization | `@PreAuthorize`, role-based | ✅ `SecurityConfig` |
| Security headers | `X-Content-Type-Options`, `X-Frame-Options`, `CSP`, `HSTS` | ✅ `ProductionSecurityConfig` (chỉ profile production) |
| CORS | Giới hạn origins, không wildcard | ✅ `ProductionSecurityConfig` |
| Rate limiting | Chưa có gateway limiter đã triển khai/kiểm chứng | `NOT_YET` — dự kiến đặt ở gateway; repo hiện không có cấu hình payment-service gateway hoặc bài test HTTP 429 |
| Secret management | Env var, không hard-code | ✅ |

**Đánh giá OWASP**: `PARTIAL` — phần lớn control đã có, nhưng rate limiting runtime và security scan report chưa rõ ràng.

## 6. Kết luận lab bảo mật

| Yêu cầu JD | Trạng thái | Ghi chú |
|-----------|-----------|---------|
| JWT | `DEMONSTRATED` | HS256, validate, expiration, role claims |
| RSA | `NOT_YET` | Chưa triển khai cặp khóa công/riêng |
| SHA | `PARTIAL` | SHA-256 bên trong HS256, BCrypt cho password |
| OWASP | `PARTIAL` | Validation, headers, RBAC đã có; scan report chưa rõ |

**Khuyến nghị**: Nếu cần RSA, bổ sung Authorization Server riêng (ví dụ: Keycloak hoặc Spring Authorization Server), payment-service làm resource server verify bằng public key.
