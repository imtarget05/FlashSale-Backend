const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:8081'

let token = null

export function setAuthToken(newToken) {
  token = newToken
}

export function clearAuthToken() {
  token = null
}

async function request(path, options = {}) {
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(options.headers || {})
  }

  const res = await fetch(`${API_BASE}${path}`, {
    headers,
    ...options
  })

  if (!res.ok) {
    const text = await res.text().catch(() => '')
    const err = new Error(text || `HTTP ${res.status}`)
    err.status = res.status
    throw err
  }

  const contentType = res.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    return res.json()
  }
  return res.text()
}

export function getApiBase() {
  return API_BASE
}

export async function getHealth() {
  return await request('/healthz')
}

export async function login(email, password) {
  const data = await request('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password })
  })
  if (data.token) {
    setAuthToken(data.token)
  }
  return data
}

export async function createPayment(payload) {
  return await request('/api/payments', {
    method: 'POST',
    body: JSON.stringify(payload)
  })
}

export async function getPayment(idempotencyKey) {
  return await request(`/api/payments/${encodeURIComponent(idempotencyKey)}`)
}

export async function listPayments(page = 0, size = 20) {
  return await request(`/api/payments?page=${page}&size=${size}`)
}

export async function getHealthDetailed() {
  return await request('/actuator/health/readiness')
}
