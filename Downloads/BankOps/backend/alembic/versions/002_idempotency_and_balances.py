"""Add idempotency_key to cash_requests and cash_balances timestamps."""
import sqlalchemy as sa

from alembic import op

revision = '002_idempotency_and_balances'
down_revision = '001_initial'
branch_labels = None
depends_on = None


def upgrade():
    # Thêm idempotency_key vào cash_requests
    op.add_column(
        'cash_requests',
        sa.Column('idempotency_key', sa.String(128), nullable=True, unique=True)
    )

    # Thêm timestamps vào cash_balances (để ON CONFLICT DO UPDATE có updated_at)
    op.add_column(
        'cash_balances',
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False)
    )
    op.add_column(
        'cash_balances',
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False)
    )

    # Thêm timestamps vào cash_journals (posted_at là timestamptz thay vì string)
    # Đổi posted_at thành TIMESTAMPTZ thực
    op.alter_column(
        'cash_journals',
        'posted_at',
        type_=sa.DateTime(timezone=True),
        postgresql_using='posted_at::timestamptz',
        nullable=True,
        existing_nullable=True,
    )


def downgrade():
    op.alter_column(
        'cash_journals', 'posted_at',
        type_=sa.String(64), nullable=True,
    )
    op.drop_column('cash_balances', 'updated_at')
    op.drop_column('cash_balances', 'created_at')
    op.drop_column('cash_requests', 'idempotency_key')
