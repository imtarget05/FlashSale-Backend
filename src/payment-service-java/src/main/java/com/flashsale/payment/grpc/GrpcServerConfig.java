package com.flashsale.payment.grpc;

import io.grpc.Server;
import io.grpc.ServerBuilder;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.io.IOException;

/**
 * Khởi động gRPC server trên cordon 9090 (không xung đột với REST cổng 8080)
 * và đăng ký PaymentGrpcServiceImpl vào server.
 */
@Configuration
public class GrpcServerConfig {

    @Bean
    public Server grpcServer(@Value("${grpc.server.port:9090}") int port,
                             PaymentGrpcServiceImpl paymentGrpcService) throws IOException {
        Server server = ServerBuilder.forPort(port)
            .addService(paymentGrpcService)
            .build();
        server.start();
        return server;
    }
}
