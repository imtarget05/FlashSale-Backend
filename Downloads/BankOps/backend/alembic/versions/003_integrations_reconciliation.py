"""Create integration hub + reconciliation tables (docs/04)."""
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

from alembic import op

revision = '003_integrations_reconciliation'
down_revision = '002_idempotency_and_balances'
branch_labels = None
depends_on = None


def upgrade():
    bind = op.get_bind()
    insp = sa.inspect(bind)
    existing = set(insp.get_table_names())
    if 'integration_connections' in existing:
        # init_db đã create_all từ model — bảng đã có, bỏ qua create.
        return
    op.create_table(
        'integration_connections',
        sa.Column('id', postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column('type', sa.String(64), nullable=False),
        sa.Column('name', sa.String(256), nullable=False),
        sa.Column('status', sa.String(32), nullable=False, server_default='connected'),
        sa.Column('secret_ref', sa.String(256), nullable=True),
        sa.Column('config_json', sa.JSON, nullable=True),
        sa.Column('owner_id', postgresql.UUID(as_uuid=True), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'integration_runs',
        sa.Column('id', postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column('connection_id', postgresql.UUID(as_uuid=True), sa.ForeignKey('integration_connections.id'), nullable=False),
        sa.Column('status', sa.String(32), nullable=False, server_default='staged'),
        sa.Column('source_fingerprint', sa.String(128), nullable=True),
        sa.Column('mapping_version', sa.Integer, nullable=False, server_default='1'),
        sa.Column('valid_count', sa.Integer, nullable=False, server_default='0'),
        sa.Column('invalid_count', sa.Integer, nullable=False, server_default='0'),
        sa.Column('external_event_id', sa.String(256), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'integration_staging_rows',
        sa.Column('id', postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column('run_id', postgresql.UUID(as_uuid=True), sa.ForeignKey('integration_runs.id'), nullable=False),
        sa.Column('source_row_key', sa.String(256), nullable=False),
        sa.Column('data_json', sa.JSON, nullable=False),
        sa.Column('valid', sa.Boolean, nullable=False, server_default='true'),
        sa.Column('errors_json', sa.JSON, nullable=True),
        sa.UniqueConstraint('run_id', 'source_row_key', name='uq_staging_run_row'),
    )
    op.create_table(
        'reconciliation_runs',
        sa.Column('id', postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column('scope', sa.String(32), nullable=False),
        sa.Column('status', sa.String(32), nullable=False, server_default='running'),
        sa.Column('auto_fixed', sa.Boolean, nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'reconciliation_exceptions',
        sa.Column('id', postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column('run_id', postgresql.UUID(as_uuid=True), sa.ForeignKey('reconciliation_runs.id'), nullable=False),
        sa.Column('subject', sa.String(256), nullable=False),
        sa.Column('expected_json', sa.JSON, nullable=False),
        sa.Column('actual_json', sa.JSON, nullable=False),
        sa.Column('expected_amount', sa.Numeric(20, 3), nullable=True),
        sa.Column('actual_amount', sa.Numeric(20, 3), nullable=True),
    )


def downgrade():
    op.drop_table('reconciliation_exceptions')
    op.drop_table('reconciliation_runs')
    op.drop_table('integration_staging_rows')
    op.drop_table('integration_runs')
    op.drop_table('integration_connections')
