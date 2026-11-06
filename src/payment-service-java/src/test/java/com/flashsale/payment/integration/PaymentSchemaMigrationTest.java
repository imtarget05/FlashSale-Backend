package com.flashsale.payment.integration;

import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.Test;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@Testcontainers
class PaymentSchemaMigrationTest {

	@Container
	static PostgreSQLContainer<?> postgres = new PostgreSQLContainer<>("postgres:15-alpine");

	@Test
	void migratesExistingSchemaThatPredatesCustomerEmail() throws SQLException {
		String schema = testSchemaName();
		try (Connection connection = openConnection(); Statement statement = connection.createStatement()) {
			statement.execute("CREATE SCHEMA " + schema);
			statement.execute("CREATE TABLE " + schema + ".payment_records (idempotency_key VARCHAR(128) PRIMARY KEY)");
		}

		migrate(schema);

		assertThat(hasCustomerEmailColumn(schema)).isTrue();
	}

	@Test
	void createsLatestSchemaFromAnEmptyDatabaseSchema() throws SQLException {
		String schema = testSchemaName();
		try (Connection connection = openConnection(); Statement statement = connection.createStatement()) {
			statement.execute("CREATE SCHEMA " + schema);
		}

		migrate(schema);

		assertThat(hasCustomerEmailColumn(schema)).isTrue();
	}

	private void migrate(String schema) {
		Flyway.configure()
			.dataSource(postgres.getJdbcUrl(), postgres.getUsername(), postgres.getPassword())
			.defaultSchema(schema)
			.schemas(schema)
			.baselineOnMigrate(true)
			.baselineVersion("1")
			.locations("classpath:db/migration")
			.load()
			.migrate();
	}

	private boolean hasCustomerEmailColumn(String schema) throws SQLException {
		try (Connection connection = openConnection();
			 Statement statement = connection.createStatement();
			 ResultSet resultSet = statement.executeQuery(
				 "SELECT 1 FROM information_schema.columns"
					 + " WHERE table_schema = '" + schema + "'"
					 + " AND table_name = 'payment_records'"
					 + " AND column_name = 'customer_email'")) {
			return resultSet.next();
		}
	}

	private Connection openConnection() throws SQLException {
		return DriverManager.getConnection(postgres.getJdbcUrl(), postgres.getUsername(), postgres.getPassword());
	}

	private String testSchemaName() {
		return "migration_" + UUID.randomUUID().toString().replace("-", "");
	}
}
