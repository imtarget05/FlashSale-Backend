import React from 'react'
import ReactDOM from 'react-dom/client'
import {
  getHealth,
  login,
  createPayment,
  getPayment,
  listPayments,
  getHealthDetailed,
  setAuthToken,
  clearAuthToken
} from './api'
import './styles.css'

const DEMO_EMAILS = [
  { email: 'customer@flashsale.local', role: 'CUSTOMER' },
  { email: 'staff@flashsale.local', role: 'STAFF' },
  { email: 'admin@flashsale.local', role: 'ADMIN' }
]

function App() {
  const [view, setView] = React.useState('login')
  const [health, setHealth] = React.useState('Checking...')
  const [error, setError] = React.useState('')
  const [token, setToken] = React.useState(null)
  const [user, setUser] = React.useState(null)
  const [payment, setPayment] = React.useState(null)
  const [payments, setPayments] = React.useState([])
  const [paymentDetail, setPaymentDetail] = React.useState(null)
  const [detailError, setDetailError] = React.useState(null)
  const [creating, setCreating] = React.useState(false)

  React.useEffect(() => {
    checkHealth()
  }, [])

  async function checkHealth() {
    try {
      const data = await getHealth()
      setHealth(typeof data === 'string' ? data : JSON.stringify(data))
    } catch (err) {
      setHealth('unreachable')
    }
  }

  async function handleLogin(email, password) {
    setError('')
    try {
      const data = await login(email, password)
      setToken(data.token)
      const matched = DEMO_EMAILS.find((u) => u.email === email)
      setUser({ email, role: matched ? matched.role : 'USER' })
      setView('dashboard')
      await checkHealth()
    } catch (err) {
      setError(err.status === 401 ? 'Sai email hoặc mật khẩu.' : 'Không thể đăng nhập. Vui lòng thử lại sau.')
    }
  }

  function handleLogout() {
    clearAuthToken()
    setToken(null)
    setUser(null)
    setPayment(null)
    setPayments([])
    setPaymentDetail(null)
    setView('login')
  }

  async function handleCreatePayment(e) {
    e.preventDefault()
    setError('')
    setCreating(true)
    const form = new FormData(e.target)
    const payload = {
      paymentId: form.get('paymentId') || crypto.randomUUID(),
      orderId: parseInt(form.get('orderId'), 10) || 1,
      idempotencyKey: form.get('idempotencyKey') || `demo-${Date.now()}`,
      amount: parseFloat(form.get('amount')) || 10.01,
      currency: form.get('currency') || 'VND'
    }
    try {
      const data = await createPayment(payload)
      setPayment(data)
      setView('payment-result')
    } catch (err) {
      if (err.status === 409) {
        setError('Mã idempotency key đã tồn tại với thông tin thanh toán khác (HTTP 409 Conflict).')
      } else if (err.status === 429) {
        setError('Quá nhiều yêu cầu. Vui lòng chờ và thử lại (HTTP 429 Too Many Requests).')
      } else if (err.status === 504) {
        setError('Cổng thanh toán phản hồi chậm (HTTP 504 Gateway Timeout). Yêu cầu đã được ghi nhận.')
      } else {
        setError(err.message || 'Không thể tạo thanh toán.')
      }
    } finally {
      setCreating(false)
    }
  }

  async function handleLoadPayments() {
    setError('')
    try {
      const data = await listPayments()
      setPayments(data.content || data || [])
    } catch (err) {
      setError('Không tải được lịch sử thanh toán.')
    }
  }

  async function handleLookupPayment(e) {
    e.preventDefault()
    setDetailError('')
    setPaymentDetail(null)
    const form = new FormData(e.target)
    const key = form.get('idempotencyKey')
    try {
      const data = await getPayment(key)
      setPaymentDetail(data)
    } catch (err) {
      if (err.status === 404) {
        setDetailError('Không tìm thấy thanh toán với mã idempotency key này.')
      } else if (err.status === 403) {
        setDetailError('Bạn không có quyền xem thanh toán này.')
      } else {
        setDetailError(err.message || 'Không thể tra cứu thanh toán.')
      }
    }
  }

  const isAuthenticated = !!token

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <span className="eyebrow">FlashSale</span>
          <h1>Java Payment Preview</h1>
        </div>
        {isAuthenticated ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <span style={{ fontSize: 14, opacity: 0.8 }}>{user?.email} · {user?.role}</span>
            <button onClick={handleLogout} style={{ background: '#94a3b8' }}>Đăng xuất</button>
          </div>
        ) : null}
      </header>

      {view === 'login' && (
        <section className="card" style={{ maxWidth: 400, margin: '40px auto' }}>
          <h2>Đăng nhập demo</h2>
          <p style={{ fontSize: 14, opacity: 0.7, marginBottom: 16 }}>
            Dùng tài khoản demo: customer / staff / admin (mật khẩu theo DEMO_AUTH_PASSWORD).
          </p>
          <form onSubmit={(e) => {
            e.preventDefault()
            const fd = new FormData(e.target)
            handleLogin(fd.get('email'), fd.get('password'))
          }}>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Email</label>
              <select name="email" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }}>
                {DEMO_EMAILS.map((u) => (
                  <option key={u.email} value={u.email}>{u.email} ({u.role})</option>
                ))}
              </select>
            </div>
            <div style={{ marginBottom: 16 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Mật khẩu</label>
              <input name="password" type="password" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <button type="submit" style={{ width: '100%' }}>Đăng nhập</button>
          </form>
          {error && <p className="error" style={{ marginTop: 12 }}>{error}</p>}
        </section>
      )}

      {view === 'dashboard' && (
        <>
          <section className="grid">
            <div className="card">
              <h2>Backend Health</h2>
              <p>{health}</p>
              <button onClick={checkHealth} style={{ marginTop: 10, padding: 8, fontSize: 13, background: '#3b82f6', color: '#fff' }}>Làm mới</button>
            </div>
            <div className="card">
              <h2>Thanh toán nhanh</h2>
              <p>Tạo thanh toán mới với mã idempotency key.</p>
              <button onClick={() => setView('create-payment')} style={{ width: '100%' }}>Tạo thanh toán</button>
            </div>
            <div className="card">
              <h2>Lịch sử thanh toán</h2>
              <p>Xem danh sách thanh toán đã xử lý.</p>
              <button onClick={() => { setView('payments'); handleLoadPayments() }} style={{ width: '100%' }}>Xem lịch sử</button>
            </div>
            <div className="card">
              <h2>Tra cứu theo key</h2>
              <p>Tìm thanh toán bằng mã idempotency key.</p>
              <button onClick={() => setView('lookup-payment')} style={{ width: '100%' }}>Tra cứu</button>
            </div>
          </section>
          {error && <p className="error">{error}</p>}
        </>
      )}

      {view === 'create-payment' && (
        <section className="card" style={{ maxWidth: 600, margin: '20px auto' }}>
          <h2>Tạo thanh toán mới</h2>
          <p>Mã idempotency key trùng sẽ không tạo đơn mới (idempotent replay).</p>
          <form onSubmit={handleCreatePayment}>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Payment ID</label>
              <input name="paymentId" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Order ID</label>
              <input name="orderId" type="number" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Idempotency Key</label>
              <input name="idempotencyKey" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Amount</label>
              <input name="amount" type="number" step="0.01" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <div style={{ marginBottom: 16 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Currency</label>
              <select name="currency" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }}>
                <option value="VND">VND</option>
                <option value="USD">USD</option>
              </select>
            </div>
            <button type="submit" disabled={creating} style={{ width: '100%' }}>
              {creating ? 'Đang xử lý...' : 'Tạo thanh toán'}
            </button>
          </form>
          {error && <p className="error" style={{ marginTop: 12 }}>{error}</p>}
          <button onClick={() => setView('dashboard')} style={{ marginTop: 12, padding: 8, fontSize: 13, background: '#94a3b8' }}>Quay lại</button>
        </section>
      )}

      {view === 'payment-result' && payment && (
        <section className="card" style={{ maxWidth: 600, margin: '20px auto' }}>
          <h2>Kết quả thanh toán</h2>
          <p style={{ fontSize: 18, fontWeight: 700, marginBottom: 16, color: payment.success ? '#4ade80' : '#fca5a5' }}>
            {payment.status}
          </p>
          <pre style={{ background: '#0f172a', padding: 16, borderRadius: 12, overflow: 'auto' }}>{JSON.stringify(payment, null, 2)}</pre>
          <div style={{ marginTop: 16, display: 'flex', gap: 10 }}>
            <button onClick={() => { setPayment(null); setView('dashboard') }} style={{ flex: 1, background: '#94a3b8' }}>Tạo thanh toán khác</button>
            <button onClick={() => { setPayment(null); setView('dashboard') }} style={{ flex: 1, background: '#3b82f6', color: '#fff' }}>Quay về trang chủ</button>
          </div>
        </section>
      )}

      {view === 'payments' && (
        <section className="card" style={{ maxWidth: 900, margin: '20px auto' }}>
          <h2>Lịch sử thanh toán</h2>
          <button onClick={handleLoadPayments} style={{ marginBottom: 12, padding: 8, fontSize: 13, background: '#3b82f6', color: '#fff' }}>Làm mới</button>
          {error && <p className="error">{error}</p>}
          {payments.length === 0 && !error && <p>Chưa có thanh toán.</p>}
          {payments.length > 0 && (
            <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: 12 }}>
              <thead>
                <tr style={{ borderBottom: '1px solid rgba(148,163,184,0.3)' }}>
                  <th style={{ textAlign: 'left', padding: 8, color: '#93c5fd', fontSize: 13 }}>Idempotency Key</th>
                  <th style={{ textAlign: 'left', padding: 8, color: '#93c5fd', fontSize: 13 }}>Status</th>
                  <th style={{ textAlign: 'left', padding: 8, color: '#93c5fd', fontSize: 13 }}>Amount</th>
                  <th style={{ textAlign: 'left', padding: 8, color: '#93c5fd', fontSize: 13 }}>Processed At</th>
                </tr>
              </thead>
              <tbody>
                {payments.map((p) => (
                  <tr key={p.idempotencyKey} style={{ borderBottom: '1px solid rgba(148,163,184,0.15)' }}>
                    <td style={{ padding: 8, fontSize: 13, fontFamily: 'monospace' }}>{p.idempotencyKey}</td>
                    <td style={{ padding: 8, fontSize: 13, color: p.success ? '#4ade80' : '#fca5a5' }}>{p.status}</td>
                    <td style={{ padding: 8, fontSize: 13 }}>{p.amount}</td>
                    <td style={{ padding: 8, fontSize: 13 }}>{p.processedAt}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <button onClick={() => setView('dashboard')} style={{ marginTop: 16, padding: 8, fontSize: 13, background: '#94a3b8' }}>Quay về trang chủ</button>
        </section>
      )}

      {view === 'lookup-payment' && (
        <section className="card" style={{ maxWidth: 600, margin: '20px auto' }}>
          <h2>Tra cứu thanh toán</h2>
          <form onSubmit={handleLookupPayment}>
            <div style={{ marginBottom: 12 }}>
              <label style={{ display: 'block', fontSize: 13, marginBottom: 6, color: '#93c5fd' }}>Idempotency Key</label>
              <input name="idempotencyKey" required style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid rgba(148,163,184,0.3)', background: '#0f172a', color: '#edf2ff' }} />
            </div>
            <button type="submit" style={{ width: '100%' }}>Tra cứu</button>
          </form>
          {detailError && <p className="error" style={{ marginTop: 12 }}>{detailError}</p>}
          {paymentDetail && (
            <div style={{ marginTop: 16, background: '#0f172a', padding: 16, borderRadius: 12 }}>
              <h3 style={{ marginTop: 0, color: '#93c5fd' }}>Chi tiết</h3>
              <pre style={{ margin: 0, overflow: 'auto' }}>{JSON.stringify(paymentDetail, null, 2)}</pre>
            </div>
          )}
          <button onClick={() => setView('dashboard')} style={{ marginTop: 16, padding: 8, fontSize: 13, background: '#94a3b8' }}>Quay về trang chủ</button>
        </section>
      )}
    </div>
  )
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)
