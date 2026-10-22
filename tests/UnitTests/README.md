# OpsBridge Unit Tests Suite

## Overview
This directory contains hermetic unit tests verifying domain invariants, use-case logic, saga state machines, and Clean Architecture constraints.

### Characteristics:
- **Zero External Infrastructure**: Tests run entirely in-memory using domain doubles, mocks, or Entity Framework Core InMemory provider.
- **Fast Execution**: Entire suite runs in < 2 seconds.
- **Hermetic & Deterministic**: No dependency on Docker, PostgreSQL, Redis, or RabbitMQ.

---

## Test Execution Commands

### 1. Run Complete Unit Test Suite (186 tests)
```bash
dotnet test tests/UnitTests/FlashSale.UnitTests.csproj
```

### 2. Run G5 Optimistic Concurrency Tests (Version Token)
```bash
dotnet test tests/UnitTests/FlashSale.UnitTests.csproj --filter "FullyQualifiedName~CheckoutSagaConcurrencyTests"
```

**Verified Test Cases:**
1. `SaveAsync_WhenConcurrentUpdateHasStaleVersion_ThrowsDbUpdateConcurrencyException_AndPreventsLostUpdates`:
   Simulates two concurrent transactional sessions loading the same saga at Version 1; the first commit succeeds (advancing to Version 2), and the second commit is rejected with `DbUpdateConcurrencyException`, actively preventing lost updates.
2. `SaveAsync_SequentialUpdates_IncrementVersionMonotonically`:
   Verifies that state transitions monotonically increment the version token (`1 -> 2 -> 3`).
3. `FakeCheckoutSagaRepository_WhenStaleVersionPresented_ThrowsDbUpdateConcurrencyException`:
   Verifies that the test fake (`FakeCheckoutSagaRepository`) accurately mirrors the optimistic locking semantics of the production EF Core repository.

### 3. Run Clean Architecture Boundary Guard Tests
```bash
dotnet test tests/UnitTests/FlashSale.UnitTests.csproj --filter "FullyQualifiedName~ArchitectureTests"
```
Ensures that `Domain` and `Application` layers never reference external infrastructure or presentation packages.

---

## Note on Solution-Level Execution (.NET 10.0)
Because this repository contains two solution configurations (`AzureFlashSale.slnx` and `OpsBridge.slnx`), always target the explicit project file or solution:
```bash
# Target solution directly
dotnet test AzureFlashSale.slnx --filter "Category!=Integration"

# Or target specific test project directly (Recommended)
dotnet test tests/UnitTests/FlashSale.UnitTests.csproj
dotnet test tests/OpsBridge.UnitTests/OpsBridge.UnitTests.csproj
```
*(Integration tests under `tests/IntegrationTests/` require a running Docker daemon for Testcontainers).*
