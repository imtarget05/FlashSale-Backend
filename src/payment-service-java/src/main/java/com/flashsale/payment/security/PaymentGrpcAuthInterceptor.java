package com.flashsale.payment.security;

import io.grpc.Context;
import io.grpc.Contexts;
import io.grpc.Metadata;
import io.grpc.ServerCall;
import io.grpc.ServerCallHandler;
import io.grpc.ServerInterceptor;
import io.grpc.Status;
import io.jsonwebtoken.JwtException;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.core.userdetails.UsernameNotFoundException;
import org.springframework.stereotype.Component;

@Component
public class PaymentGrpcAuthInterceptor implements ServerInterceptor {

	public static final Context.Key<Authentication> AUTHENTICATION_CONTEXT_KEY =
		Context.key("payment-authentication");

	private static final Metadata.Key<String> AUTHORIZATION_HEADER =
		Metadata.Key.of("authorization", Metadata.ASCII_STRING_MARSHALLER);
	private static final String HEALTH_METHOD = "flashsale.payment.v1.PaymentService/CheckHealth";

	private final JwtUtil jwtUtil;
	private final UserDetailsService userDetailsService;

	public PaymentGrpcAuthInterceptor(JwtUtil jwtUtil, UserDetailsService userDetailsService) {
		this.jwtUtil = jwtUtil;
		this.userDetailsService = userDetailsService;
	}

	@Override
	public <ReqT, RespT> ServerCall.Listener<ReqT> interceptCall(
		ServerCall<ReqT, RespT> call,
		Metadata headers,
		ServerCallHandler<ReqT, RespT> next
	) {
		String methodName = call.getMethodDescriptor().getFullMethodName();
		if (HEALTH_METHOD.equals(methodName)) {
			return next.startCall(call, headers);
		}

		String authorization = headers.get(AUTHORIZATION_HEADER);
		if (authorization == null || !authorization.startsWith("Bearer ")
			|| authorization.length() <= "Bearer ".length()) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Authentication required"));
		}

		String token = authorization.substring("Bearer ".length());
		if (!jwtUtil.validateToken(token)) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Invalid authentication token"));
		}

		String username;
		try {
			username = jwtUtil.extractUsername(token);
		} catch (JwtException | IllegalArgumentException ex) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Invalid authentication token"));
		}
		if (username == null || username.isBlank()) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Invalid authentication token"));
		}

		UserDetails userDetails;
		try {
			userDetails = userDetailsService.loadUserByUsername(username);
		} catch (UsernameNotFoundException ex) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Invalid authentication token"));
		}
		if (!jwtUtil.isTokenValid(token, userDetails)
			|| !userDetails.isEnabled()
			|| !userDetails.isAccountNonExpired()
			|| !userDetails.isAccountNonLocked()
			|| !userDetails.isCredentialsNonExpired()) {
			return reject(call, Status.UNAUTHENTICATED.withDescription("Invalid authentication token"));
		}

		Authentication authentication = new UsernamePasswordAuthenticationToken(
			userDetails, null, userDetails.getAuthorities());
		Context context = Context.current().withValue(AUTHENTICATION_CONTEXT_KEY, authentication);
		return Contexts.interceptCall(context, call, headers, next);
	}

	private static <ReqT, RespT> ServerCall.Listener<ReqT> reject(
		ServerCall<ReqT, RespT> call,
		Status status
	) {
		call.close(status, new Metadata());
		return new ServerCall.Listener<>() {};
	}
}
