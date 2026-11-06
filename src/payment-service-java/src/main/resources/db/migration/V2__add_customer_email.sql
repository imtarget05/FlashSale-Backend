ALTER TABLE payment_records
    ADD COLUMN IF NOT EXISTS customer_email VARCHAR(256);
