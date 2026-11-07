-- FlashSale seed data for production-scale testing
-- Run: psql -U flashsale -d flashsale -f seed-data.sql

-- Seed payment records for production-scale testing (10000 records)
DO $$
DECLARE
  i INT;
  statuses TEXT[] := ARRAY['Succeeded', 'Declined', 'TimedOut'];
  currencies TEXT[] := ARRAY['VND', 'USD'];
BEGIN
  FOR i IN 1..10000 LOOP
    INSERT INTO payment_records (
      idempotency_key, payment_id, order_id, customer_email, amount, currency,
      success, status, transaction_id, failure_reason, is_transient_error, processed_at
    ) VALUES (
      'seed-key-' || i,
      gen_random_uuid(),
      (i % 100) + 1,
      'user' || (i % 50) || '@flashsale.local',
      (10 + (i % 990))::numeric + 0.01,
      currencies[1 + (i % 2)],
      CASE WHEN i % 10 = 2 THEN false ELSE true END,
      CASE WHEN i % 10 = 2 THEN 'Declined' WHEN i % 10 = 3 THEN 'TimedOut' ELSE 'Succeeded' END,
      CASE WHEN i % 10 IN (2,3) THEN NULL ELSE 'tx_' || gen_random_uuid()::text END,
      CASE WHEN i % 10 = 2 THEN 'Insufficient funds' WHEN i % 10 = 3 THEN 'Gateway timeout' ELSE NULL END,
      CASE WHEN i % 10 = 3 THEN true ELSE false END,
      NOW() - (i || ' minutes')::interval
    ) ON CONFLICT (idempotency_key) DO NOTHING;
  END LOOP;
END $$;

-- Seed payment outbox events (10000 records)
DO $$
DECLARE
  i INT;
BEGIN
  FOR i IN 1..10000 LOOP
    INSERT INTO payment_outbox (idempotency_key, routing_key, payload, created_at, next_attempt_at, published_at, attempts)
    VALUES (
      'seed-key-' || i,
      CASE WHEN i % 10 IN (2,3) THEN 'payment.failed' ELSE 'payment.completed' END,
      '{"idempotencyKey":"seed-key-' || i || '","status":"Succeeded"}',
      NOW() - (i || ' minutes')::interval,
      NOW() - (i || ' minutes')::interval,
      NOW() - (i || ' minutes')::interval,
      0
    ) ON CONFLICT (idempotency_key) DO NOTHING;
  END LOOP;
END $$;
