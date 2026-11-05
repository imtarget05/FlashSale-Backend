package com.flashsale.payment.integration;

import com.flashsale.payment.audit.PaymentAuditDocument;
import com.flashsale.payment.audit.PaymentAuditRepository;
import com.mongodb.client.MongoClient;
import com.mongodb.client.MongoClients;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.springframework.data.mongodb.core.MongoTemplate;
import org.springframework.data.mongodb.repository.support.MongoRepositoryFactory;
import org.testcontainers.containers.MongoDBContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;

@Testcontainers
class PaymentAuditRepositoryIntegrationTest {

	@Container
	static final MongoDBContainer mongo = new MongoDBContainer(DockerImageName.parse("mongo:7.0.5"));

	private static MongoClient mongoClient;
	private static PaymentAuditRepository repository;

	@BeforeAll
	static void connectRepository() {
		mongoClient = MongoClients.create(mongo.getReplicaSetUrl());
		MongoTemplate mongoTemplate = new MongoTemplate(mongoClient, "flashsale_test");
		repository = new MongoRepositoryFactory(mongoTemplate).getRepository(PaymentAuditRepository.class);
	}

	@AfterAll
	static void closeMongoClient() {
		mongoClient.close();
	}

	@Test
	void savesAuditAndUpdatesByIdempotencyKey() {
		repository.deleteAll();

		PaymentAuditDocument original = new PaymentAuditDocument(
			"idem-123", true, "Succeeded", null, false, "2026-01-02T03:04:05Z");
		PaymentAuditDocument updated = new PaymentAuditDocument(
			"idem-123", false, "Declined", "Insufficient funds", false, "2026-01-02T03:05:05Z");

		repository.save(original);
		repository.save(updated);

		Optional<PaymentAuditDocument> result = repository.findById("idem-123");

		assertThat(result).contains(updated);
		assertThat(repository.count()).isEqualTo(1);
	}

}
