package com.flashsale.payment.audit;

import org.springframework.data.mongodb.repository.MongoRepository;

public interface PaymentAuditRepository extends MongoRepository<PaymentAuditDocument, String> {
}
