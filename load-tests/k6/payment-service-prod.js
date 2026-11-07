import http from 'k6/http'
import { check, sleep } from 'k6'
import { Rate, Trend } from 'k6/metrics'

const errorRate = new Rate('errors')
const paymentLatency = new Trend('payment_latency')

export const options = {
  stages: [
    { duration: '1m', target: 50 },    // warm up
    { duration: '3m', target: 200 },   // sustained production load
    { duration: '1m', target: 500 },   // peak spike
    { duration: '2m', target: 200 },   // sustain peak
    { duration: '1m', target: 0 },     // ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<300', 'p(99)<800'],
    errors: ['rate<0.05'],
    payment_latency: ['p(95)<250'],
  },
}

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8081'
const TOKEN = __ENV.TOKEN || ''

export default function () {
  const headers = {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${TOKEN}`,
  }

  // Health check (liveness)
  const healthRes = http.get(`${BASE_URL}/healthz`)
  check(healthRes, {
    'health status 200': (r) => r.status === 200,
  }) || errorRate.add(1)

  // Readiness check (dependency health)
  const readyRes = http.get(`${BASE_URL}/actuator/health/readiness`)
  check(readyRes, {
    'readiness status 200': (r) => r.status === 200,
  }) || errorRate.add(1)

  // Create payment (idempotent)
  const idempotencyKey = `prod-load-${__VU}-${__ITER}-${Date.now()}`
  const payload = JSON.stringify({
    paymentId: crypto.randomUUID(),
    orderId: Math.floor(Math.random() * 1000) + 1,
    idempotencyKey: idempotencyKey,
    amount: (Math.random() * 500 + 10).toFixed(2),
    currency: Math.random() > 0.5 ? 'VND' : 'USD',
  })

  const start = Date.now()
  const paymentRes = http.post(`${BASE_URL}/api/payments`, payload, { headers })
  paymentLatency.add(Date.now() - start)

  check(paymentRes, {
    'payment accepted (200/202)': (r) => r.status === 200 || r.status === 202,
    'payment not 5xx': (r) => r.status < 500,
  }) || errorRate.add(1)

  // Idempotency replay (same key should return same result)
  const replayRes = http.post(`${BASE_URL}/api/payments`, payload, { headers })
  check(replayRes, {
    'idempotency replay 200': (r) => r.status === 200,
  }) || errorRate.add(1)

  sleep(Math.random() * 0.5 + 0.1)
}
EOF