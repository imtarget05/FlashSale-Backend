package com.flashsale.payment.grpc;

import com.flashsale.payment.security.PaymentGrpcAuthInterceptor;
import io.grpc.ServerInterceptors;
import io.grpc.Server;
import io.grpc.netty.shaded.io.grpc.netty.NettyServerBuilder;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.io.File;
import java.io.IOException;

@Configuration
@ConditionalOnProperty(name = "grpc.server.enabled", havingValue = "true")
public class PaymentGrpcServerConfig {

	@Bean(destroyMethod = "shutdown")
	public Server paymentGrpcServer(
		@Value("${grpc.server.port:9090}") int port,
		@Value("${grpc.server.tls.certificate-chain:}") String certificateChain,
		@Value("${grpc.server.tls.private-key:}") String privateKey,
		PaymentGrpcServiceImpl paymentService,
		PaymentGrpcAuthInterceptor authInterceptor
	) throws IOException {
		File certificate = new File(certificateChain);
		File key = new File(privateKey);
		if (!certificate.isFile() || !certificate.canRead() || !key.isFile() || !key.canRead()) {
			throw new IllegalStateException(
				"gRPC requires readable TLS certificate and private key files when enabled");
		}
		return NettyServerBuilder.forPort(port)
			.useTransportSecurity(certificate, key)
			.addService(ServerInterceptors.intercept(paymentService, authInterceptor))
			.build()
			.start();
	}
}
