"""Custom OpenAPI schema generation."""


def custom_openapi(app):
    """Generate custom OpenAPI schema with security definitions."""
    if app.openapi_schema:
        return app.openapi_schema

    from fastapi.openapi.utils import get_openapi

    openapi_schema = get_openapi(
        title="BankOps API",
        version="0.1.0",
        description="BankOps API - Inventory and Cash Simulation System (Demo). All data is simulated.",
        routes=app.routes,
    )

    # Add security schemes
    openapi_schema["components"]["securitySchemes"] = {
        "BearerAuth": {
            "type": "http",
            "scheme": "bearer",
            "bearerFormat": "JWT",
            "description": "JWT Bearer token for authentication",
        }
    }

    # Define security requirements for all operations
    for _path, methods in openapi_schema["paths"].items():
        for method, spec in methods.items():
            if method in ("get", "post", "put", "patch", "delete"):
                if spec.get("parameters"):
                    spec["security"] = [{"BearerAuth": []}]
                else:
                    spec["security"] = [{"BearerAuth": []}]

    app.openapi_schema = openapi_schema
    return app.openapi_schema