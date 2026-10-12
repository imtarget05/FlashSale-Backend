"""IAM models - Identity and Access Management."""
import enum

from sqlalchemy import Column, Enum, String
from sqlalchemy.dialects.postgresql import UUID

from app.db.base import Base, TimestampMixin


class UserStatus(enum.StrEnum):
    active = "active"
    inactive = "inactive"
    locked = "locked"


class User(Base, TimestampMixin):
    __tablename__ = "iam_users"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    username: str = Column(String(64), unique=True, nullable=False, index=True)
    password_hash: str = Column(String(256), nullable=False)
    status: UserStatus = Column(Enum(UserStatus), nullable=False, default=UserStatus.active)
    display_name_demo: str = Column(String(256), nullable=False)

    def __repr__(self):
        return f"<User {self.username}>"


class Role(Base, TimestampMixin):
    __tablename__ = "iam_roles"

    code: str = Column(String(64), primary_key=True)
    name: str = Column(String(128), nullable=False)
    description: str = Column(String(512), nullable=True)

    def __repr__(self):
        return f"<Role {self.code}>"


class Capability(Base, TimestampMixin):
    __tablename__ = "iam_capabilities"

    code: str = Column(String(64), primary_key=True)
    name: str = Column(String(128), nullable=False)
    description: str = Column(String(512), nullable=True)

    def __repr__(self):
        return f"<Capability {self.code}>"


class UserRole(Base):
    __tablename__ = "iam_user_roles"

    user_id: str = Column(UUID(as_uuid=True), primary_key=True)
    role_code: str = Column(String(64), primary_key=True)

    def __repr__(self):
        return f"<UserRole {self.user_id} -> {self.role_code}>"


class RoleCapability(Base):
    __tablename__ = "iam_role_capabilities"

    role_code: str = Column(String(64), primary_key=True)
    capability_code: str = Column(String(64), primary_key=True)