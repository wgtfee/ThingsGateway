#!/usr/bin/env python3
"""Static contract checks for the ThingsGateway Industrial IAM rollout."""

from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / "src" / "ThingsGateway.Server"


def load_json(name: str) -> dict:
    with (SERVER / name).open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"IAM SSO contract failed: {message}")


def security(profile: dict) -> dict:
    return profile["Security"]


def validate_profiles() -> None:
    prepare = load_json("appsettings.IamPrepare.json")
    shadow = load_json("appsettings.Shadow.json")
    centralized = load_json("appsettings.Centralized.json")

    require(security(prepare)["Authentication"]["Mode"] == "Local", "IamPrepare authentication must remain Local")
    require(security(prepare)["Authorization"]["Mode"] == "Local", "IamPrepare authorization must remain Local")
    require(prepare["IndustrialIamWeb"]["Enabled"] is False, "IamPrepare web SSO must stay disabled")

    require(security(shadow)["Authentication"]["Mode"] == "Centralized", "Shadow authentication must be Centralized")
    require(security(shadow)["Authorization"]["Mode"] == "Local", "Shadow local authorization must remain authoritative")
    require(security(shadow)["Central"]["ShadowCentralAuthorization"] is True, "Shadow central comparison must be enabled")
    require(shadow["IndustrialIamWeb"]["Enabled"] is True, "Shadow IAM web login must be available")

    require(security(centralized)["Authentication"]["Mode"] == "Centralized", "Centralized authentication must be Centralized")
    require(security(centralized)["Authorization"]["Mode"] == "Centralized", "Centralized authorization must be Centralized")
    require(centralized["IndustrialIamWeb"]["Enabled"] is True, "Centralized IAM web login must be enabled")

    for name, profile in (("Shadow", shadow), ("Centralized", centralized)):
        central = security(profile)["Central"]
        require(central["SystemCode"] == "THINGSGATEWAY", f"{name} SystemCode must be THINGSGATEWAY")
        require(central["RequireSystemAccess"] is True, f"{name} must require SystemAccess")
        require(central["ShadowUserProvisioning"] == "RequirePreProvision", f"{name} must require explicit local-user binding")
        require(profile["IndustrialIamWeb"]["ClientId"] == "industrial-thingsgateway-web", f"{name} must use the dedicated public client")
        require(profile["IndustrialIamWeb"]["RedirectUri"].endswith("/thinggateway/auth/iam/callback"), f"{name} callback is inconsistent")


def validate_manifest_and_controllers() -> None:
    manifest = load_json("permission-manifest.json")
    require(manifest["system"]["code"] == "THINGSGATEWAY", "manifest SystemCode must be THINGSGATEWAY")

    codes = {resource["code"] for resource in manifest["resources"]}
    required = {
        "thingsgateway.gateway.view",
        "thingsgateway.gateway.control",
        "thingsgateway.gateway.manage",
        "thingsgateway.gateway.export",
    }
    require(required.issubset(codes), "manifest is missing canonical gateway permissions")
    require(not any(code.startswith("thinggateway.") for code in codes), "central manifest must not contain the old missing-S aliases")
    require(not any(code.startswith("THINGGATEWAY.") for code in codes), "central manifest must not contain legacy uppercase aliases")

    controller_root = ROOT / "src" / "Gateway" / "ThingsGateway.Gateway.Application" / "Controller"
    expected = {
        "RuntimeInfoController.cs": 'Permission("thingsgateway.gateway.view")',
        "ControlController.cs": 'Permission("thingsgateway.gateway.control")',
        "ManagementController.cs": 'Permission("thingsgateway.gateway.manage")',
        "GatewayExportController.cs": 'Permission("thingsgateway.gateway.export")',
    }
    for file_name, marker in expected.items():
        text = (controller_root / file_name).read_text(encoding="utf-8-sig")
        require(marker in text, f"{file_name} does not use canonical permission metadata")
        require("THINGGATEWAY.Gateway." not in text, f"{file_name} still contains legacy central permission metadata")


def validate_publish_contract() -> None:
    project = (SERVER / "ThingsGateway.Server.csproj").read_text(encoding="utf-8-sig")
    require('appsettings.IamPrepare.json" CopyToOutputDirectory="PreserveNewest"' in project,
            "IamPrepare profile must be copied to build output")


def main() -> None:
    validate_profiles()
    validate_manifest_and_controllers()
    validate_publish_contract()
    print("ThingsGateway IAM SSO static contract: OK")


if __name__ == "__main__":
    main()
