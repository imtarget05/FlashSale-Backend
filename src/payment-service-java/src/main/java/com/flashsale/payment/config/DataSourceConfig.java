package com.flashsale.payment.config;

import org.springframework.boot.jdbc.DataSourceBuilder;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.jpa.repository.config.EnableJpaRepositories;

import javax.sql.DataSource;

@Configuration
@EnableJpaRepositories("com.flashsale.payment.repository")
public class DataSourceConfig {

	@Bean
	public DataSource dataSource() {
		String host = System.getenv("POSTGRES_HOST") != null ? System.getenv("POSTGRES_HOST") : "postgres";
		String port = System.getenv("POSTGRES_PORT") != null ? System.getenv("POSTGRES_PORT") : "5432";
		String db = System.getenv("POSTGRES_DB") != null ? System.getenv("POSTGRES_DB") : "FlashSaleDb";
		String user = System.getenv("POSTGRES_USER") != null ? System.getenv("POSTGRES_USER") : "postgres";
		String password = System.getenv("POSTGRES_PASSWORD") != null ? System.getenv("POSTGRES_PASSWORD") : "postgres";

		String url = String.format("jdbc:postgresql://%s:%s/%s", host, port, db);

		return DataSourceBuilder.create()
			.url(url)
			.username(user)
			.password(password)
			.driverClassName("org.postgresql.Driver")
			.build();
	}
}
