package com.flashsale.payment.outbox;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;

@Repository
public interface PaymentOutboxRepository extends JpaRepository<PaymentOutbox, UUID> {

	@Query(value = """
		SELECT * FROM payment_outbox
		WHERE published_at IS NULL AND next_attempt_at <= :now
		ORDER BY created_at ASC
		LIMIT :batchSize
		FOR UPDATE SKIP LOCKED
		""", nativeQuery = true)
	List<PaymentOutbox> lockDueEvents(@Param("now") OffsetDateTime now, @Param("batchSize") int batchSize);
}
