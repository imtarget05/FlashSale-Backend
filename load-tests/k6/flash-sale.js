import http from 'k6/http';
import { check, fail } from 'k6';

// Run only on an isolated local/CI stack; each iteration creates one payment.
// Usage: k6 run -e BASE_URL=http://localhost:8081 load-tests/k6/flash-sale.js

export const options = {
  scenarios: {
    payment_smoke_load: {
      executor: 'ramping-arrival-rate',
      startRate: 2,
      timeUnit: '1s',
      preAllocatedVUs: 10,
      maxVUs: 30,
      stages: [
        { target: 5, duration: '10s' },
        { target: 5, duration: '10s' },
        { target: 0, duration: '5s' },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<1000'],
    http_req_failed: ['rate<0.02'],
    checks: ['rate>0.98'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8081';

export function setup() {
  const response = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({
      email: __ENV.STAFF_EMAIL || 'staff@flashsale.local',
      password: __ENV.STAFF_PASSWORD || 'password',
    }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  if (response.status !== 200) {
    fail(`Staff login failed with HTTP ${response.status}`);
  }

  const token = response.json('token');
  if (!token) {
    fail('Staff login response did not contain a token');
  }
  return { token };
}

export default function ({ token }) {
  const uuidSuffix = `${__VU}`.padStart(4, '0') + `${__ITER}`.padStart(8, '0');
  const response = http.post(
    `${BASE_URL}/api/payments`,
    JSON.stringify({
      paymentId: `00000000-0000-4000-8000-${uuidSuffix}`,
      orderId: __VU * 1000000 + __ITER,
      idempotencyKey: `k6-${__VU}-${__ITER}`,
      amount: 10.01,
      currency: 'USD',
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
    },
  );

  const ok = check(response, {
    'payment processed': (result) => result.status === 200,
    'success status returned': (result) => result.json('success') === true,
  });
  if (!ok) console.error(`unexpected status ${response.status}`);
}
