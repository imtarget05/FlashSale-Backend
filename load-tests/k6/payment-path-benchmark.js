import http from 'k6/http'
import { check } from 'k6'
import { Counter, Rate } from 'k6/metrics'

const paymentErrors = new Rate('payment_errors')
const paymentRateLimited = new Counter('payment_rate_limited')
const baseUrl = __ENV.BASE_URL
const token = __ENV.TOKEN

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '1m',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  thresholds: {
    'http_req_duration{name:payment_create}': ['p(95)<500'],
    payment_errors: ['rate<0.01'],
  },
}

export function setup() {
  if (!baseUrl || !token) {
    throw new Error('BASE_URL and TOKEN are required')
  }
}

export default function () {
  const id = crypto.randomUUID()
  const response = http.post(`${baseUrl}/api/payments`, JSON.stringify({
    paymentId: id,
    orderId: 1,
    idempotencyKey: `bench-${id}`,
    amount: '10.01',
    currency: 'USD',
  }), {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    tags: { name: 'payment_create' },
  })

  const accepted = check(response, { 'payment created': (result) => result.status === 200 })
  paymentErrors.add(!accepted)
  paymentRateLimited.add(response.status === 429 ? 1 : 0)
}
