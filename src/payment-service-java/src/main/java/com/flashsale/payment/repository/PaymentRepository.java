package com.flashsale.payment.repository;

import com.flashsale.payment.entity.Payment;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import java.util.Optional;

@Repository
public interface PaymentRepository extends JpaRepository<Payment, String> {

	Optional<Payment> findByIdempotencyKey(String idempotencyKey);

	boolean existsByIdempotencyKey(String idempotencyKey);
}
