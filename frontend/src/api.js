const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:8081'

async function request(path, options = {}) {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {})
    },
    ...options
  })

  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(text || `HTTP ${res.status}`)
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

export async function createPayment(payload) {
  return await request('/api/payments', {
    method: 'POST',
    body: JSON.stringify(payload)
  })
}
