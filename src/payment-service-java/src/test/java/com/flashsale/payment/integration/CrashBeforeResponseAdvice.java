package com.flashsale.payment.integration;

import com.flashsale.payment.dto.PaymentRecordDto;
import org.springframework.context.annotation.Profile;
import org.springframework.core.MethodParameter;
import org.springframework.http.MediaType;
import org.springframework.http.converter.HttpMessageConverter;
import org.springframework.http.server.ServerHttpRequest;
import org.springframework.http.server.ServerHttpResponse;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.servlet.mvc.method.annotation.ResponseBodyAdvice;

/** Test-classpath-only fault hook. Never packaged into the production JAR. */
@RestControllerAdvice
@Profile("crash-drill")
public class CrashBeforeResponseAdvice implements ResponseBodyAdvice<Object> {

	private final String targetKey = System.getenv("CRASH_DRILL_KEY");

	@Override
	public boolean supports(MethodParameter returnType,
		Class<? extends HttpMessageConverter<?>> converterType) {
		return true;
	}

	@Override
	public Object beforeBodyWrite(Object body, MethodParameter returnType, MediaType selectedContentType,
		Class<? extends HttpMessageConverter<?>> selectedConverterType,
		ServerHttpRequest request, ServerHttpResponse response) {
		if (body instanceof PaymentRecordDto payment
			&& targetKey != null && !targetKey.isBlank()
			&& targetKey.equals(payment.idempotencyKey())
			&& "/api/payments".equals(request.getURI().getPath())) {
			System.err.println("CRASH_AFTER_COMMIT_BEFORE_RESPONSE key=" + targetKey);
			System.err.flush();
			Runtime.getRuntime().halt(87);
		}
		return body;
	}
}
