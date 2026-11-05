package com.flashsale.payment.grpc;

import com.flashsale.payment.security.PaymentGrpcAuthInterceptor;
import org.junit.jupiter.api.io.TempDir;
import org.junit.jupiter.api.Test;

import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.Mockito.mock;

class PaymentGrpcServerConfigTest {

	@TempDir
	Path tempDir;

	@Test
	void refusesToStartWithoutReadableTlsFiles() {
		var config = new PaymentGrpcServerConfig();

		assertThrows(IllegalStateException.class, () -> config.paymentGrpcServer(
			9090,
			"",
			"",
			mock(PaymentGrpcServiceImpl.class),
			mock(PaymentGrpcAuthInterceptor.class)));
	}

	@Test
	void validatesReadableTlsFilesBeforeBuildingTheServer() throws Exception {
		Path certificate = Files.writeString(tempDir.resolve("server.crt"), "not a certificate");
		Path privateKey = Files.writeString(tempDir.resolve("server.key"), "not a private key");
		var config = new PaymentGrpcServerConfig();

		assertThrows(Exception.class, () -> config.paymentGrpcServer(
			0,
			certificate.toString(),
			privateKey.toString(),
			mock(PaymentGrpcServiceImpl.class),
			mock(PaymentGrpcAuthInterceptor.class)));
	}
}
