import React from 'react'
import ReactDOM from 'react-dom/client'
import { getHealth, createPayment } from './api'
import './styles.css'

function App() {
  const [health, setHealth] = React.useState('Checking...')
  const [payment, setPayment] = React.useState(null)
  const [error, setError] = React.useState('')

  React.useEffect(() => {
    getHealth()
      .then((data) => {
        const text = typeof data === 'string' ? data : JSON.stringify(data)
        setHealth(text)
      })
      .catch((err) => {
        setHealth('unreachable')
        setError(err.message)
      })
  }, [])

  const handleCreatePayment = async () => {
    try {
      const payload = {
        orderId: 1,
        amount: 199000,
        currency: 'VND',
        idempotencyKey: `demo-${Date.now()}`
      }
      const data = await createPayment(payload)
      setPayment(data)
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <span className="eyebrow">FlashSale</span>
          <h1>Java Payment Preview</h1>
        </div>
        <button onClick={handleCreatePayment}>Create demo payment</button>
      </header>

      <section className="grid">
        <div className="card">
          <h2>Backend Health</h2>
          <p>{health}</p>
          {error && <small className="error">{error}</small>}
        </div>

        <div className="card">
          <h2>Payment Demo</h2>
          {payment ? (
            <pre>{JSON.stringify(payment, null, 2)}</pre>
          ) : (
            <p>Click the button to test the API.</p>
          )}
        </div>
      </section>
    </div>
  )
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)
