package com.flashsale.payment.security;

import com.flashsale.payment.grpc.v1.PaymentServiceGrpc;
import com.flashsale.payment.grpc.v1.HealthCheckRequest;
import com.flashsale.payment.grpc.v1.HealthCheckResponse;
import com.flashsale.payment.grpc.v1.ProcessPaymentRequest;
import com.flashsale.payment.grpc.v1.ProcessPaymentResponse;
import io.grpc.Metadata;
import io.grpc.ServerCall;
import io.grpc.ServerCallHandler;
import io.grpc.Status;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.ArgumentCaptor;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;

import java.util.concurrent.atomic.AtomicReference;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class PaymentGrpcAuthInterceptorTest {

	private static final Metadata.Key<String> AUTHORIZATION_HEADER =
		Metadata.Key.of("authorization", Metadata.ASCII_STRING_MARSHALLER);

	@Mock
	private JwtUtil jwtUtil;
	@Mock
	private UserDetailsService userDetailsService;
	@Mock
	private ServerCall<ProcessPaymentRequest, ProcessPaymentResponse> call;
	@Mock
	private ServerCallHandler<ProcessPaymentRequest, ProcessPaymentResponse> next;

	private PaymentGrpcAuthInterceptor interceptor;

	@BeforeEach
	void setUp() {
		interceptor = new PaymentGrpcAuthInterceptor(jwtUtil, userDetailsService);
	}

	@Test
	void rejectsMissingBearerToken() {
		when(call.getMethodDescriptor()).thenReturn(PaymentServiceGrpc.getProcessPaymentMethod());
		var headers = new Metadata();
		interceptor.interceptCall(call, headers, next);

		ArgumentCaptor<Status> status = ArgumentCaptor.forClass(Status.class);
		verify(call).close(status.capture(), any());
		assertEquals(Status.Code.UNAUTHENTICATED, status.getValue().getCode());
		verify(next, never()).startCall(any(), any());
	}

	@Test
	void rejectsInvalidJwtWithoutLoadingUser() {
		when(call.getMethodDescriptor()).thenReturn(PaymentServiceGrpc.getProcessPaymentMethod());
		var headers = bearer("invalid");
		when(jwtUtil.validateToken("invalid")).thenReturn(false);

		interceptor.interceptCall(call, headers, next);

		ArgumentCaptor<Status> status = ArgumentCaptor.forClass(Status.class);
		verify(call).close(status.capture(), any());
		assertEquals(Status.Code.UNAUTHENTICATED, status.getValue().getCode());
		verify(userDetailsService, never()).loadUserByUsername(any());
	}

	@Test
	void attachesAuthenticatedUserToGrpcContext() {
		when(call.getMethodDescriptor()).thenReturn(PaymentServiceGrpc.getProcessPaymentMethod());
		String token = "signed-token";
		UserDetails user = User.withUsername("staff-user")
			.password("not-used")
			.authorities("ROLE_STAFF")
			.build();
		when(jwtUtil.validateToken(token)).thenReturn(true);
		when(jwtUtil.extractUsername(token)).thenReturn("staff-user");
		when(userDetailsService.loadUserByUsername("staff-user")).thenReturn(user);
		when(jwtUtil.isTokenValid(token, user)).thenReturn(true);
		var listener = new ServerCall.Listener<ProcessPaymentRequest>() {};
		AtomicReference<Authentication> observedAuthentication = new AtomicReference<>();
		when(next.startCall(eq(call), any())).thenAnswer(invocation -> {
			observedAuthentication.set(PaymentGrpcAuthInterceptor.AUTHENTICATION_CONTEXT_KEY.get());
			return listener;
		});

		var returned = interceptor.interceptCall(call, bearer(token), next);

		assertNotNull(returned);
		assertEquals("staff-user", observedAuthentication.get().getName());
		assertEquals("ROLE_STAFF", observedAuthentication.get().getAuthorities().iterator().next().getAuthority());
	}

	@Test
	void rejectsDisabledUser() {
		when(call.getMethodDescriptor()).thenReturn(PaymentServiceGrpc.getProcessPaymentMethod());
		String token = "signed-token";
		UserDetails disabledUser = User.withUsername("staff-user")
			.password("not-used")
			.authorities("ROLE_STAFF")
			.disabled(true)
			.build();
		when(jwtUtil.validateToken(token)).thenReturn(true);
		when(jwtUtil.extractUsername(token)).thenReturn("staff-user");
		when(userDetailsService.loadUserByUsername("staff-user")).thenReturn(disabledUser);
		when(jwtUtil.isTokenValid(token, disabledUser)).thenReturn(true);

		interceptor.interceptCall(call, bearer(token), next);

		ArgumentCaptor<Status> status = ArgumentCaptor.forClass(Status.class);
		verify(call).close(status.capture(), any());
		assertEquals(Status.Code.UNAUTHENTICATED, status.getValue().getCode());
		verify(next, never()).startCall(any(), any());
	}

	@Test
	void allowsHealthMethodWithoutAuthentication() {
		ServerCall<HealthCheckRequest, HealthCheckResponse> healthCall = mock(ServerCall.class);
		ServerCallHandler<HealthCheckRequest, HealthCheckResponse> healthNext = mock(ServerCallHandler.class);
		when(healthCall.getMethodDescriptor()).thenReturn(PaymentServiceGrpc.getCheckHealthMethod());

		interceptor.interceptCall(healthCall, new Metadata(), healthNext);

		verify(healthNext).startCall(eq(healthCall), any());
		verify(healthCall, never()).close(any(), any());
	}

	private static Metadata bearer(String token) {
		Metadata headers = new Metadata();
		headers.put(AUTHORIZATION_HEADER, "Bearer " + token);
		return headers;
	}
}
