package com.flashsale.payment.concurrency;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class LockFreeAdmissionGateTest {

    @Test
    @DisplayName("Negative initial capacity must be rejected")
    void negativeCapacityThrowsException() {
        assertThatThrownBy(() -> new LockFreeAdmissionGate(-1))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    @DisplayName("Single-threaded basic reservation and exhaustion")
    void basicReservationAndExhaustion() {
        LockFreeAdmissionGate gate = new LockFreeAdmissionGate(5);

        // Admit 3
        var res1 = gate.tryAdmit(3);
        assertThat(res1.isAdmitted()).isTrue();
        assertThat(res1.remainingSlots()).isEqualTo(2);
        assertThat(gate.getAvailableSlots()).isEqualTo(2);
        assertThat(gate.getTotalAdmitted()).isEqualTo(3);

        // Try admit 3 (more than available 2) -> rejected
        var res2 = gate.tryAdmit(3);
        assertThat(res2.isAdmitted()).isFalse();
        assertThat(res2.decision()).isEqualTo(LockFreeAdmissionGate.Decision.REJECTED_EXHAUSTED);
        assertThat(res2.remainingSlots()).isEqualTo(2);

        // Admit remaining 2
        var res3 = gate.tryAdmit(2);
        assertThat(res3.isAdmitted()).isTrue();
        assertThat(gate.getAvailableSlots()).isZero();

        // Release 1 and admit 1
        gate.release(1);
        assertThat(gate.getAvailableSlots()).isEqualTo(1);
        var res4 = gate.tryAdmit(1);
        assertThat(res4.isAdmitted()).isTrue();
        assertThat(gate.getAvailableSlots()).isZero();
    }

    @Test
    @DisplayName("Invalid request quantity is rejected safely")
    void invalidQuantityHandledSafely() {
        LockFreeAdmissionGate gate = new LockFreeAdmissionGate(10);
        var res = gate.tryAdmit(0);
        assertThat(res.isAdmitted()).isFalse();
        assertThat(res.decision()).isEqualTo(LockFreeAdmissionGate.Decision.REJECTED_INVALID_QUANTITY);

        var resNeg = gate.tryAdmit(-5);
        assertThat(resNeg.isAdmitted()).isFalse();
        assertThat(resNeg.decision()).isEqualTo(LockFreeAdmissionGate.Decision.REJECTED_INVALID_QUANTITY);
    }

    @Test
    @DisplayName("Concurrent stress test: 200 threads competing for 50 items - Zero Oversell guaranteed")
    void concurrentStressTest_zeroOversell() throws InterruptedException {
        int capacity = 50;
        int threadCount = 200;
        LockFreeAdmissionGate gate = new LockFreeAdmissionGate(capacity);

        ExecutorService executor = Executors.newFixedThreadPool(32);
        CountDownLatch readyLatch = new CountDownLatch(threadCount);
        CountDownLatch startLatch = new CountDownLatch(1);
        CountDownLatch doneLatch = new CountDownLatch(threadCount);

        AtomicInteger successfulAdmissions = new AtomicInteger(0);
        AtomicInteger rejectedAdmissions = new AtomicInteger(0);

        for (int i = 0; i < threadCount; i++) {
            executor.submit(() -> {
                readyLatch.countDown();
                try {
                    startLatch.await(); // Thundering herd: all threads hit at the exact same millisecond
                    var result = gate.tryAdmit(1);
                    if (result.isAdmitted()) {
                        successfulAdmissions.incrementAndGet();
                    } else {
                        rejectedAdmissions.incrementAndGet();
                    }
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                } finally {
                    doneLatch.countDown();
                }
            });
        }

        readyLatch.await(5, TimeUnit.SECONDS);
        startLatch.countDown(); // Trigger all threads simultaneously
        boolean completed = doneLatch.await(10, TimeUnit.SECONDS);

        assertThat(completed).isTrue();
        executor.shutdown();

        // Mathematical invariants verification
        assertThat(successfulAdmissions.get())
                .as("Exact admissions must equal initial capacity")
                .isEqualTo(capacity);

        assertThat(rejectedAdmissions.get())
                .as("Remaining requests must be cleanly rejected")
                .isEqualTo(threadCount - capacity);

        assertThat(gate.getAvailableSlots())
                .as("Remaining slots must be exactly zero")
                .isZero();

        assertThat(gate.getTotalAdmitted())
                .as("Total admitted internal counter must equal initial capacity")
                .isEqualTo(capacity);
    }
}
