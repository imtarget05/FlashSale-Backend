package com.flashsale.payment.repository;

import com.flashsale.payment.entity.Payment;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import java.util.Optional;

@Repository
public interface PaymentRepository extends JpaRepository<Payment, String> {

	Optional<Payment> findByIdempotencyKey(String idempotencyKey);

	boolean existsByIdempotencyKey(String idempotencyKey);

	Optional<Payment> findByTransactionId(String transactionId);

	@Modifying
	@Query(value = """
		INSERT INTO payment_records (
			idempotency_key, payment_id, order_id, customer_email, amount, currency, success, status,
			transaction_id, failure_reason, is_transient_error, processed_at
		) VALUES (
			:#{#record.idempotencyKey}, :#{#record.paymentId}, :#{#record.orderId},
			:#{#record.customerEmail},
			:#{#record.amount}, :#{#record.currency}, :#{#record.success}, :#{#record.status},
			:#{#record.transactionId}, :#{#record.failureReason}, :#{#record.isTransientError},
			:#{#record.processedAt}
		)
		ON CONFLICT (idempotency_key) DO NOTHING
		""", nativeQuery = true)
	int insertIfAbsent(@Param("record") Payment record);
}
