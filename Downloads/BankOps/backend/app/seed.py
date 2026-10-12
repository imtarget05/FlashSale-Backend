"""Seed demo data into PostgreSQL database.

Idempotent seed that reads sample data from JSON files under sample-data/.
In production, this is blocked via the APP_ENV check.
"""
import asyncio
import json
import os
import uuid
from pathlib import Path

from sqlalchemy import select

from app.api.routes.auth_utils import get_password_hash
from app.config import settings
from app.database import AsyncSessionLocal
from app.models.cash_sim import CashBalance, CashBox
from app.models.iam import Capability, Role, RoleCapability, User, UserRole
from app.models.inventory import Asset, Balance, Item, Location

# Demo password - used for all seeded demo users.
DEMO_PASSWORD = "DemoPass123!"

REPO_DIR = Path(__file__).resolve().parents[2]  # backend/app -> backend -> repo
SAMPLE_DATA_DIR = Path(os.environ.get("SAMPLE_DATA_DIR", str(REPO_DIR / "sample-data")))


def _load_sample_data(filename: str) -> dict | list:
    """Load sample data from JSON file under sample-data/ directory."""
    path = SAMPLE_DATA_DIR / filename
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def uuid_gen() -> str:
    """Generate UUID as string."""
    return str(uuid.uuid4())


async def seed_demo():
    """Seed demo data from sample-data/*.json files.

    Idempotent: skips if data already exists.
    """
    if settings.app_env == "production":
        raise RuntimeError("seed_demo is blocked in production.")

    async with AsyncSessionLocal() as db:
        # --- Roles ---
        existing_roles = await db.execute(select(Role.code))
        if not existing_roles.first():
            roles = [
                Role(code="admin", name="Admin", description="Full access"),
                Role(code="staff", name="Staff (Maker)", description="Creates requests"),
                Role(code="approver", name="Approver (Checker)", description="Approves requests"),
                Role(code="inventory_manager", name="Inventory Manager", description="Manages catalog and stocktakes"),
                Role(code="cash_sim_operator", name="Cash Simulation Operator", description="Reconciles cash ledger"),
                Role(code="integration_admin", name="Integration Admin", description="Manages connectors"),
                Role(code="auditor", name="Auditor", description="Read-only access"),
                Role(code="system_admin", name="System Admin", description="User and config management"),
            ]
            db.add_all(roles)
            await db.commit()
            print("Seeded roles")

        # --- Capabilities ---
        existing_caps = await db.execute(select(Capability.code))
        if not existing_caps.first():
            caps = [
                Capability(code="inventory:create_request", name="Create inventory request"),
                Capability(code="inventory:approve_request", name="Approve inventory request"),
                Capability(code="inventory:manage_catalog", name="Manage inventory catalog"),
                Capability(code="inventory:view_balances", name="View inventory balances"),
                Capability(code="cash:create_request", name="Create cash request"),
                Capability(code="cash:approve_request", name="Approve cash request"),
                Capability(code="cash:reconcile", name="Reconcile cash ledger"),
                Capability(code="cash:view_balances", name="View cash balances"),
                Capability(code="approval:view_inbox", name="View approval inbox"),
                Capability(code="activity:view", name="View activity feed"),
                Capability(code="integration:manage", name="Manage integrations"),
                Capability(code="audit:view", name="View audit logs"),
                Capability(code="admin:user_management", name="Manage users"),
            ]
            db.add_all(caps)
            await db.commit()
            print("Seeded capabilities")

        # --- Role-Capability mappings ---
        result = await db.execute(select(RoleCapability).limit(1))
        if not result.first():
            mappings = [
                RoleCapability(role_code="staff", capability_code="inventory:create_request"),
                RoleCapability(role_code="approver", capability_code="inventory:approve_request"),
                RoleCapability(role_code="inventory_manager", capability_code="inventory:manage_catalog"),
                RoleCapability(role_code="inventory_manager", capability_code="inventory:view_balances"),
                RoleCapability(role_code="staff", capability_code="cash:create_request"),
                RoleCapability(role_code="approver", capability_code="cash:approve_request"),
                RoleCapability(role_code="cash_sim_operator", capability_code="cash:reconcile"),
                RoleCapability(role_code="cash_sim_operator", capability_code="cash:view_balances"),
                RoleCapability(role_code="approver", capability_code="approval:view_inbox"),
                RoleCapability(role_code="staff", capability_code="activity:view"),
                RoleCapability(role_code="approver", capability_code="activity:view"),
                RoleCapability(role_code="inventory_manager", capability_code="activity:view"),
                RoleCapability(role_code="cash_sim_operator", capability_code="activity:view"),
                RoleCapability(role_code="integration_admin", capability_code="integration:manage"),
                RoleCapability(role_code="integration_admin", capability_code="activity:view"),
                RoleCapability(role_code="auditor", capability_code="audit:view"),
                RoleCapability(role_code="auditor", capability_code="inventory:view_balances"),
                RoleCapability(role_code="auditor", capability_code="cash:view_balances"),
                RoleCapability(role_code="auditor", capability_code="activity:view"),
                RoleCapability(role_code="staff", capability_code="integration:manage"),
                RoleCapability(role_code="staff", capability_code="inventory:view_balances"),
                RoleCapability(role_code="staff", capability_code="cash:view_balances"),
                RoleCapability(role_code="approver", capability_code="inventory:view_balances"),
                RoleCapability(role_code="approver", capability_code="cash:view_balances"),
                RoleCapability(role_code="approver", capability_code="cash:reconcile"),
                RoleCapability(role_code="admin", capability_code="admin:user_management"),
                RoleCapability(role_code="admin", capability_code="integration:manage"),
                RoleCapability(role_code="system_admin", capability_code="admin:user_management"),
            ]
            db.add_all(mappings)
            await db.commit()
            print("Seeded role-capability mappings")

        # --- Users ---
        users_data = list(_load_sample_data("users.json"))
        qa_extra_users = [
            {
                "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa4",
                "username": "demo_admin",
                "display_name_demo": "Phạm Quản Trị (giả)",
                "roles": ["admin", "staff", "approver"],
            },
            {
                "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5",
                "username": "demo_approver2",
                "display_name_demo": "Hoàng Duyệt 2 (giả)",
                "roles": ["approver"],
            },
        ]
        all_users_spec = users_data + qa_extra_users
        existing_usernames = set(
            (await db.execute(select(User.username))).scalars().all()
        )
        missing_specs = [
            u for u in all_users_spec if u["username"] not in existing_usernames
        ]
        if missing_specs:
            password_hash = get_password_hash(DEMO_PASSWORD)
            users = []
            user_roles = []
            role_map = {
                "staff": "staff",
                "approver": "approver",
                "auditor": "auditor",
                "admin": "admin",
            }
            for u in missing_specs:
                user = User(
                    id=uuid.UUID(u["id"]),
                    username=u["username"],
                    password_hash=password_hash,
                    status="active",
                    display_name_demo=u["display_name_demo"],
                )
                users.append(user)
                for role_key in u.get("roles", []):
                    user_roles.append(
                        UserRole(
                            user_id=uuid.UUID(u["id"]),
                            role_code=role_map.get(role_key, role_key),
                        )
                    )
            db.add_all(users)
            db.add_all(user_roles)
            await db.commit()
            print(f"Seeded {len(users)} users")

        # --- Inventory: Locations ---
        inv_data = _load_sample_data("inventory.json")
        existing_locs = await db.execute(select(Location.id))
        if not existing_locs.first():
            locations = [
                Location(
                    id=uuid.UUID(loc["id"]),
                    code=loc["code"],
                    name=loc["name"],
                    active=True,
                )
                for loc in inv_data["locations"]
            ]
            db.add_all(locations)
            await db.commit()
            print(f"Seeded {len(locations)} locations")

        # --- Inventory: Items ---
        existing_items = await db.execute(select(Item.id))
        if not existing_items.first():
            items = [
                Item(
                    id=uuid.UUID(item["id"]),
                    sku=item["sku"],
                    name=item["name"],
                    unit=item["unit"],
                    tracked_serial=item.get("tracked_serial", False),
                    active=True,
                )
                for item in inv_data["items"]
            ]
            db.add_all(items)
            await db.commit()
            print(f"Seeded {len(items)} items")

        # --- Inventory: Assets ---
        existing_assets = await db.execute(select(Asset.id))
        if not existing_assets.first():
            loc_result = await db.execute(select(Location.code, Location.id))
            loc_map = {row.code: row.id for row in loc_result.all()}
            item_result = await db.execute(select(Item.sku, Item.id))
            item_map = {row.sku: row.id for row in item_result.all()}

            assets = []
            for asset_data in inv_data.get("assets", []):
                asset = Asset(
                    id=uuid.uuid4(),
                    item_id=item_map[asset_data["sku"]],
                    serial=asset_data["serial"],
                    location_id=loc_map[asset_data["location_code"]],
                    assignee_demo=None,
                    status=asset_data.get("status", "available"),
                )
                assets.append(asset)
            db.add_all(assets)
            await db.commit()
            print(f"Seeded {len(assets)} assets")

        # --- Inventory: Initial Balances ---
        existing_balances = await db.execute(select(Balance.location_id).limit(1))
        if not existing_balances.first():
            loc_result = await db.execute(select(Location.code, Location.id))
            loc_map = {row.code: row.id for row in loc_result.all()}
            item_result = await db.execute(select(Item.sku, Item.id))
            item_map = {row.sku: row.id for row in item_result.all()}

            balances = []
            for bal_data in inv_data.get("initial_balances", []):
                balances.append(Balance(
                    location_id=loc_map[bal_data["location_code"]],
                    item_id=item_map[bal_data["sku"]],
                    quantity=bal_data["quantity"],
                    version=1,
                ))
            db.add_all(balances)
            await db.commit()
            print(f"Seeded {len(balances)} initial balances")

        # --- Cash Simulation ---
        cash_data = _load_sample_data("cash-sim.json")
        existing_cashboxes = await db.execute(select(CashBox.id))
        if not existing_cashboxes.first():
            cashboxes = [
                CashBox(
                    id=uuid.UUID(cb["id"]),
                    code=cb["code"],
                    name=cb["name"],
                    currency=cash_data.get("currency", "VND"),
                    active=True,
                )
                for cb in cash_data["cashboxes"]
            ]
            db.add_all(cashboxes)
            await db.commit()

            cb_result = await db.execute(select(CashBox.id, CashBox.code))
            cb_map = {row.code: row.id for row in cb_result.all()}

            cash_balances = []
            for cb in cash_data["cashboxes"]:
                cash_balances.append(CashBalance(
                    cashbox_id=cb_map[cb["code"]],
                    amount=float(cb.get("opening_balance", "0.00")),
                    version=1,
                ))
            db.add_all(cash_balances)
            await db.commit()
            print(f"Seeded {len(cashboxes)} cashboxes")

        await db.close()
        print("Demo seeding complete!")


if __name__ == "__main__":
    asyncio.run(seed_demo())