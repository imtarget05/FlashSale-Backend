"""Initial migration - all tables."""
import sqlalchemy as sa
import sqlalchemy.dialects.postgresql as pg

from alembic import op

revision = '001_initial'
down_revision = None
branch_labels = None
depends_on = None


def upgrade():
    # IAM tables
    op.create_table(
        'iam_users',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('username', sa.String(64), unique=True, nullable=False, index=True),
        sa.Column('password_hash', sa.String(256), nullable=False),
        sa.Column('status', sa.Enum('active', 'inactive', 'locked', name='userstatus'), nullable=False, default='active'),
        sa.Column('display_name_demo', sa.String(256), nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'iam_roles',
        sa.Column('code', sa.String(64), primary_key=True),
        sa.Column('name', sa.String(128), nullable=False),
        sa.Column('description', sa.String(512), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'iam_capabilities',
        sa.Column('code', sa.String(64), primary_key=True),
        sa.Column('name', sa.String(128), nullable=False),
        sa.Column('description', sa.String(512), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'iam_user_roles',
        sa.Column('user_id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('role_code', sa.String(64), primary_key=True),
    )

    op.create_table(
        'iam_role_capabilities',
        sa.Column('role_code', sa.String(64), primary_key=True),
        sa.Column('capability_code', sa.String(64), primary_key=True),
    )

    # Inventory tables
    op.create_table(
        'inv_locations',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('code', sa.String(64), unique=True, nullable=False, index=True),
        sa.Column('name', sa.String(256), nullable=False),
        sa.Column('active', sa.Boolean, default=True, nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'inv_items',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('sku', sa.String(128), unique=True, nullable=False, index=True),
        sa.Column('name', sa.String(256), nullable=False),
        sa.Column('unit', sa.String(32), nullable=False),
        sa.Column('tracked_serial', sa.Boolean, default=False, nullable=False),
        sa.Column('active', sa.Boolean, default=True, nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'inv_assets',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('item_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_items.id', ondelete='RESTRICT'), nullable=False),
        sa.Column('serial', sa.String(128), unique=True, nullable=False, index=True),
        sa.Column('location_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_locations.id', ondelete='RESTRICT'), nullable=False),
        sa.Column('assignee_demo', sa.String(128), nullable=True),
        sa.Column('status', sa.String(32), default='available', nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'inv_requests',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('kind', sa.String(32), nullable=False),
        sa.Column('status', sa.String(32), default='draft', nullable=False),
        sa.Column('maker_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=False),
        sa.Column('approver_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=True),
        sa.Column('source_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_locations.id'), nullable=True),
        sa.Column('dest_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_locations.id'), nullable=True),
        sa.Column('reason', sa.String(512), nullable=True),
        sa.Column('version', sa.Integer, default=1, nullable=False),
        sa.Column('submitted_at', sa.String(64), nullable=True),
        sa.Column('created_by', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'inv_request_lines',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('request_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_requests.id', ondelete='CASCADE'), nullable=False),
        sa.Column('item_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_items.id'), nullable=False),
        sa.Column('quantity', sa.Numeric(20, 3), nullable=False),
        sa.Column('serial', sa.String(128), nullable=True),
    )

    op.create_table(
        'inv_movements',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('request_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_requests.id'), nullable=False),
        sa.Column('line_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_request_lines.id'), nullable=False),
        sa.Column('item_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_items.id'), nullable=False),
        sa.Column('location_id', sa.UUID(as_uuid=True), sa.ForeignKey('inv_locations.id'), nullable=False),
        sa.Column('quantity_signed', sa.Numeric(20, 3), nullable=False),
        sa.Column('posted_at', sa.String(64), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'inv_balances',
        sa.Column('location_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_locations.id'), primary_key=True),
        sa.Column('item_id', pg.UUID(as_uuid=True), sa.ForeignKey('inv_items.id'), primary_key=True),
        sa.Column('quantity', sa.Numeric(20, 3), nullable=False),
        sa.Column('version', sa.Integer, default=1, nullable=False),
        )

    # Cash sim tables
    op.create_table(
        'cash_boxes',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('code', sa.String(64), unique=True, nullable=False, index=True),
        sa.Column('name', sa.String(256), nullable=False),
        sa.Column('currency', sa.String(8), default='VND', nullable=False),
        sa.Column('active', sa.Boolean, default=True, nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )
    op.create_table(
        'cash_requests',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('cashbox_id', pg.UUID(as_uuid=True), sa.ForeignKey('cash_boxes.id'), nullable=False),
        sa.Column('kind', sa.String(32), nullable=False),
        sa.Column('amount', sa.Numeric(20, 2), nullable=False),
        sa.Column('status', sa.String(32), default='draft', nullable=False),
        sa.Column('maker_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=False),
        sa.Column('approver_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=True),
        sa.Column('reason', sa.String(512), nullable=True),
        sa.Column('version', sa.Integer, default=1, nullable=False),
        sa.Column('submitted_at', sa.String(64), nullable=True),
        sa.Column('created_by', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'cash_journals',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('request_id', pg.UUID(as_uuid=True), sa.ForeignKey('cash_requests.id'), unique=True, nullable=False),
        sa.Column('posted_at', sa.String(64), nullable=True),
        sa.Column('reversal_of', pg.UUID(as_uuid=True), sa.ForeignKey('cash_journals.id'), nullable=True),
        sa.Column('correlation_id', sa.String(64), nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'cash_entries',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('journal_id', pg.UUID(as_uuid=True), sa.ForeignKey('cash_journals.id'), nullable=False),
        sa.Column('account_code', sa.String(128), nullable=False),
        sa.Column('debit', sa.Numeric(20, 2), default=0, nullable=False),
        sa.Column('credit', sa.Numeric(20, 2), default=0, nullable=False),
    )

    op.create_table(
        'cash_balances',
        sa.Column('cashbox_id', pg.UUID(as_uuid=True), sa.ForeignKey('cash_boxes.id'), primary_key=True),
        sa.Column('amount', sa.Numeric(20, 2), default=0, nullable=False),
        sa.Column('version', sa.Integer, default=1, nullable=False),
    )

    # Approval tables
    op.create_table(
        'approval_decisions',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('subject_type', sa.String(64), nullable=False),
        sa.Column('subject_id', pg.UUID(as_uuid=True), nullable=False),
        sa.Column('actor_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=False),
        sa.Column('decision', sa.String(32), nullable=False),
        sa.Column('reason', sa.Text, nullable=True),
        sa.Column('request_payload_hash', sa.String(128), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    # Activity/Audit tables
    op.create_table(
        'audit_events',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('actor_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=True),
        sa.Column('action', sa.String(128), nullable=False),
        sa.Column('subject', sa.String(128), nullable=False),
        sa.Column('before_hash', sa.String(128), nullable=True),
        sa.Column('after_hash', sa.String(128), nullable=True),
        sa.Column('request_id', sa.String(64), nullable=True),
        sa.Column('occurred_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('metadata_json', sa.JSON, nullable=True),
    )

    op.create_table(
        'outbox_events',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('event_type', sa.String(128), nullable=False),
        sa.Column('aggregate_id', sa.String(128), nullable=False),
        sa.Column('payload_json', sa.JSON, nullable=False),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('published_at', sa.DateTime(timezone=True), nullable=True),
        sa.Column('attempts', sa.Integer, default=0, nullable=False),
    )

    op.create_table(
        'jobs',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('type', sa.String(128), nullable=False),
        sa.Column('status', sa.String(32), default='queued', nullable=False),
        sa.Column('processed', sa.Integer, nullable=True),
        sa.Column('total', sa.Integer, nullable=True),
        sa.Column('error_code', sa.String(64), nullable=True),
        sa.Column('owner_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=True),
        sa.Column('finished_at', sa.DateTime(timezone=True), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    )

    op.create_table(
        'activity_events',
        sa.Column('id', pg.UUID(as_uuid=True), primary_key=True),
        sa.Column('owner_id', pg.UUID(as_uuid=True), sa.ForeignKey('iam_users.id'), nullable=True),
        sa.Column('type', sa.String(128), nullable=False),
        sa.Column('subject', sa.String(128), nullable=False),
        sa.Column('message_key', sa.String(256), nullable=False),
        sa.Column('payload', sa.JSON, nullable=False),
        sa.Column('occurred_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.Column('outbox_event_id', pg.UUID(as_uuid=True), sa.ForeignKey('outbox_events.id'), nullable=True),
    )


def downgrade():
    # Reverse order of creation
    op.drop_table('activity_events')
    op.drop_table('jobs')
    op.drop_table('outbox_events')
    op.drop_table('audit_events')
    op.drop_table('approval_decisions')
    op.drop_table('cash_balances')
    op.drop_table('cash_entries')
    op.drop_table('cash_journals')
    op.drop_table('cash_requests')
    op.drop_table('cash_boxes')
    op.drop_table('inv_balances')
    op.drop_table('inv_movements')
    op.drop_table('inv_request_lines')
    op.drop_table('inv_requests')
    op.drop_table('inv_assets')
    op.drop_table('inv_items')
    op.drop_table('inv_locations')
    op.drop_table('iam_role_capabilities')
    op.drop_table('iam_user_roles')
    op.drop_table('iam_capabilities')
    op.drop_table('iam_roles')
    op.drop_table('iam_users')
    op.execute("DROP TYPE userstatus")
