"""Convert remaining VARCHAR datetime columns to timestamptz (docs/04).

001 khai báo submitted_at/posted_at là String(64) trong khi model là
DateTime(timezone=True): DB trả str, route gọi .isoformat() -> 500.
002 đã sửa cash_journals.posted_at; 004 sửa 3 cột còn lại.
"""
import sqlalchemy as sa

from alembic import op

revision = '004_timestamptz_cleanup'
down_revision = '003_integrations_reconciliation'
branch_labels = None
depends_on = None


def upgrade():
    bind = op.get_bind()
    insp = sa.inspect(bind)
    targets = [
        ("inv_requests", "submitted_at"),
        ("inv_movements", "posted_at"),
        ("cash_requests", "submitted_at"),
    ]
    for table, column in targets:
        col_types = {c["name"]: str(c["type"]) for c in insp.get_columns(table)}
        if "TIMESTAMP WITH TIME ZONE" in col_types.get(column, "").upper():
            continue  # DB đã đồng bộ (model create_all) — bỏ qua cột này.
        op.alter_column(
            table, column,
            type_=sa.DateTime(timezone=True),
            postgresql_using=f'{column}::timestamptz',
            nullable=True,
            existing_nullable=True,
        )


def downgrade():
    op.alter_column('inv_requests', 'submitted_at', type_=sa.String(64), nullable=True)
    op.alter_column('inv_movements', 'posted_at', type_=sa.String(64), nullable=True)
    op.alter_column('cash_requests', 'submitted_at', type_=sa.String(64), nullable=True)
