# T10 — Dependency Scan Report

## Scope
- Project: FlashSale payment-service (`src/payment-service-java`)
- Tool: `mvn dependency:tree -Dincludes=org.springframework,io.jsonwebtoken,com.rabbitmq,org.postgresql`
- FlashSale SHA: `96dc3d9`
- Build tool: Apache Maven 3.9.16, Java 17.0.20

## Dependency tree (filtered includes)

```
com.flashsale:payment-service:jar:1.0.0
+- org.springframework.boot:spring-boot-starter-web:jar:4.0.9:compile
|  \- org.springframework.boot:spring-boot-webmvc:jar:4.0.9:compile
|     \- org.springframework:spring-webmvc:jar:7.0.10:compile
+- org.springframework.boot:spring-boot-jackson2:jar:4.0.9:compile
|  +- org.springframework.boot:spring-boot:jar:4.0.9:compile
|  |  \- org.springframework:spring-context:jar:7.0.10:compile
|  \- org.springframework:spring-web:jar:7.0.10:compile
|     \- org.springframework:spring-beans:jar:7.0.10:compile
+- org.springframework.boot:spring-boot-starter-data-jpa:jar:4.0.9:compile
|  +- org.springframework.boot:spring-boot-data-jpa:jar:4.0.9:compile
|  |  +- org.springframework.boot:spring-boot-hibernate:jar:4.0.9:compile
|  |  |  \- org.springframework:spring-orm:jar:7.0.10:compile
|  |  +- org.springframework.data:spring-data-jpa:jar:4.0.8:compile
|  |  |  \- org.springframework:spring-tx:jar:7.0.10:compile
|  |  \- org.springframework:spring-aspects:jar:7.0.10:compile
|  \- org.springframework.boot:spring-boot-jdbc:jar:4.0.9:compile
|     \- org.springframework:spring-jdbc:jar:7.0.10:compile
+- org.springframework.boot:spring-boot-starter-security:jar:4.0.9:compile
|  \- org.springframework:spring-aop:jar:7.0.10:compile
+- org.springframework.boot:spring-boot-starter-amqp:jar:4.0.9:compile
|  \- org.springframework.boot:spring-boot-amqp:jar:4.0.9:compile
|     +- org.springframework:spring-messaging:jar:7.0.10:compile
|     \- org.springframework.amqp:spring-rabbit:jar:4.0.6:compile
|        \- com.rabbitmq:amqp-client:jar:5.34.0:compile
+- org.springframework.boot:spring-boot-starter-data-redis:jar:4.0.9:compile
|  \- org.springframework.boot:spring-boot-data-redis:jar:4.0.9:compile
|     \- org.springframework.data:spring-data-redis:jar:4.0.8:compile
|        \- org.springframework:spring-oxm:jar:7.0.10:compile
+- org.postgresql:postgresql:jar:42.7.14:runtime
+- io.jsonwebtoken:jjwt-api:jar:0.12.3:compile
+- io.jsonwebtoken:jjwt-impl:jar:0.12.3:runtime
+- io.jsonwebtoken:jjwt-jackson:jar:0.12.3:runtime
```

## Known-vulnerable version check

| Library | Version | Known-vulnerable? | Source |
|---------|---------|-------------------|--------|
| spring-boot / spring-framework | 4.0.9 / 7.0.10 | Not flagged by trivy (see container scan) | trivy image |
| com.rabbitmq:amqp-client | 5.34.0 | MEDIUM CVE-2026-106121 (fixed in 5.36.1) | trivy image (app.jar) |
| org.postgresql:postgresql | 42.7.14 | Not flagged | trivy image |
| io.jsonwebtoken:jjwt | 0.12.3 | Not flagged | trivy image |

## OWASP dependency-check
- Not available in this environment (no `dependency-check` CLI / maven plugin configured).
- Cross-checked with trivy image scan (see t10-container-scan.md).

## Disposition
- `com.rabbitmq:amqp-client 5.34.0` → MEDIUM CVE-2026-106121 DoS (fixed in 5.36.1). Accepted risk for now; upgrade to 5.36.1+ in next dependency bump. Owner: platform team; no expiry set (track as backlog).
- All other scanned dependencies: no known vulnerabilities flagged.
