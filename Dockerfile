FROM maven:3.9-eclipse-temurin-17 AS build
WORKDIR /workspace
COPY src/payment-service-java/pom.xml ./payment-service-java/pom.xml
COPY src/payment-service-java/src ./payment-service-java/src
RUN --mount=type=cache,target=/root/.m2 \
    mvn -q -f payment-service-java/pom.xml -DskipTests package

FROM eclipse-temurin:17-jre
WORKDIR /app
COPY --from=build /workspace/payment-service-java/target/*.jar /app/app.jar
EXPOSE 8080
USER nobody
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["java", "-jar", "/app/app.jar"]
