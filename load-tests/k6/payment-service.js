import http from 'k6/http'
import { check, sleep } from 'k6'
import { Rate } from 'k6/metrics'

const errorRate = new Rate('errors')

export const options = {
  stages: [
    { duration: '30s', target: 10 },   // ramp up
    { duration: '1m', target: 50 },     // sustained load
    { duration: '30s', target: 100 },  // spike
    { duration: '30s', target: 0 },    // ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],
    errors: ['rate<0.1'],
  },
}

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8081'
const TOKEN = __ENV.TOKEN || ''

export default function () {
  const headers = {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${TOKEN}`,
  }

  // Health check
  const healthRes = http.get(`${BASE_URL}/healthz`)
  check(healthRes, {
    'health status 200': (r) => r.status === 200,
  }) || errorRate.add(1)

  // Create payment
  const payload = JSON.stringify({
    paymentId: crypto.randomUUID(),
    orderId: Math.floor(Math.random() * 100) + 1,
    idempotencyKey: `load-${__VU}-${__ITER}-${Date.now()}`,
    amount: (Math.random() * 100 + 10).toFixed(2),
    currency: Math.random() > 0.5 ? 'VND' : 'USD',
  })

  const paymentRes = http.post(`${BASE_URL}/api/payments`, payload, { headers })
  check(paymentRes, {
    'payment status 200 or 504': (r) => r.status === 200 || r.status === 504,
  }) || errorRate.add(1)

  sleep(0.1)
}
