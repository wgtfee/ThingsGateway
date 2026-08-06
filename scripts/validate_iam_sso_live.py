#!/usr/bin/env python3
"""Live Phase 8.2 acceptance for ThingsGateway IAM Web SSO.

This script uses only Python's standard library and talks to the public YARP origin,
so IAM browser-session cookies and ThingsGateway PKCE/cookie state are exercised the
same way as a browser. Credentials are read from environment variables and are never
printed.

Environment variables for Shadow/Centralized:
  THINGSGATEWAY_IAM_USER
  THINGSGATEWAY_IAM_PASSWORD
  THINGSGATEWAY_IAM_TENANT   (optional)

Examples:
  python scripts/validate_iam_sso_live.py --profile iamprepare
  python scripts/validate_iam_sso_live.py --profile shadow
  python scripts/validate_iam_sso_live.py --profile centralized
"""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any


@dataclass
class HttpResult:
    status: int
    url: str
    body: bytes
    headers: Any

    def text(self) -> str:
        return self.body.decode("utf-8", errors="replace")

    def json(self) -> Any:
        return json.loads(self.text())


class RecordingRedirectHandler(urllib.request.HTTPRedirectHandler):
    def __init__(self) -> None:
        super().__init__()
        self.history: list[dict[str, Any]] = []

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        target = urllib.parse.urljoin(req.full_url, newurl)
        self.history.append({"status": code, "from": req.full_url, "to": target})
        return super().redirect_request(req, fp, code, msg, headers, target)


class AcceptanceError(RuntimeError):
    pass


def build_client() -> tuple[urllib.request.OpenerDirector, RecordingRedirectHandler, http.cookiejar.CookieJar]:
    jar = http.cookiejar.CookieJar()
    redirects = RecordingRedirectHandler()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), redirects)
    opener.addheaders = [("User-Agent", "ThingsGateway-IAM-Acceptance/1.0")]
    return opener, redirects, jar


def request(
    opener: urllib.request.OpenerDirector,
    url: str,
    method: str = "GET",
    json_body: Any | None = None,
    timeout: float = 15.0,
) -> HttpResult:
    data = None
    headers: dict[str, str] = {}
    if json_body is not None:
        data = json.dumps(json_body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
        headers["Accept"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with opener.open(req, timeout=timeout) as response:
            return HttpResult(response.status, response.geturl(), response.read(), response.headers)
    except urllib.error.HTTPError as exc:
        return HttpResult(exc.code, exc.geturl(), exc.read(), exc.headers)
    except urllib.error.URLError as exc:
        raise AcceptanceError(f"无法连接 {url}: {exc.reason}") from exc


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AcceptanceError(message)


def pretty_error(result: HttpResult) -> str:
    text = result.text().strip()
    if not text:
        return f"HTTP {result.status}"
    try:
        payload = json.loads(text)
        if isinstance(payload, dict) and payload.get("error"):
            return f"HTTP {result.status}: {payload['error']}"
    except json.JSONDecodeError:
        pass
    return f"HTTP {result.status}: {text[:500]}"


def check_profile(status: dict[str, Any], profile: str) -> None:
    expected = {
        "iamprepare": {
            "enabled": False,
            "authenticationMode": "Local",
            "authorizationMode": "Local",
            "shadowCentralAuthorization": False,
            "requireSystemAccess": False,
        },
        "shadow": {
            "enabled": True,
            "authenticationMode": "Centralized",
            "authorizationMode": "Local",
            "shadowCentralAuthorization": True,
            "requireSystemAccess": True,
        },
        "centralized": {
            "enabled": True,
            "authenticationMode": "Centralized",
            "authorizationMode": "Centralized",
            "shadowCentralAuthorization": False,
            "requireSystemAccess": True,
        },
    }[profile]

    for key, expected_value in expected.items():
        actual = status.get(key)
        require(actual == expected_value, f"Profile 不匹配: {key}={actual!r}, 期望 {expected_value!r}")

    require(status.get("clientId") == "industrial-thingsgateway-web", "OIDC ClientId 不是 industrial-thingsgateway-web")
    require(str(status.get("redirectUri", "")).endswith("/thinggateway/auth/iam/callback"), "OIDC callback 路径不正确")


def probe_gateway_api_route(opener: urllib.request.OpenerDirector, gateway: str) -> None:
    url = gateway + "/api/thinggateway/openApi/runtimeInfo/redundancyStatus"
    result = request(opener, url)
    if result.status == 404:
        raise AcceptanceError("ThingsGateway API 路由返回 404；检查 YARP PathRemovePrefix/PathPrefix。")
    if result.status in (502, 503, 504):
        raise AcceptanceError(f"ThingsGateway API 路由已命中 Gateway，但后端不可用: HTTP {result.status}")
    if result.status == 200:
        raise AcceptanceError("未携带 Bearer 的 OpenAPI 请求返回 200；认证约束可能失效。")
    require(result.status in (401, 403), f"OpenAPI 无令牌探针返回异常状态: {pretty_error(result)}")
    print(f"[PASS] Gateway API 原生路径探针 -> HTTP {result.status}（已到达受保护端点，不是 404）")


def run_iamprepare(opener: urllib.request.OpenerDirector, gateway: str) -> None:
    probe_gateway_api_route(opener, gateway)
    print("[PASS] IamPrepare: Web SSO 关闭，Local/Local 配置正确")


def run_sso(
    opener: urllib.request.OpenerDirector,
    redirects: RecordingRedirectHandler,
    gateway: str,
    username: str,
    password: str,
    tenant: str | None,
    keep_session: bool,
) -> None:
    probe_gateway_api_route(opener, gateway)

    login = request(
        opener,
        gateway + "/account/login",
        method="POST",
        json_body={"userName": username, "password": password, "tenant": tenant or None},
    )
    require(login.status == 200, "IAM 账号登录失败: " + pretty_error(login))
    try:
        require(login.json().get("authenticated") is True, "IAM /account/login 未返回 authenticated=true")
    except (json.JSONDecodeError, AttributeError):
        raise AcceptanceError("IAM /account/login 返回的不是预期 JSON。")
    print("[PASS] IAM browser session 已建立")

    redirects.history.clear()
    start = gateway + "/thinggateway/auth/iam/login?returnUrl=" + urllib.parse.quote("/thinggateway/", safe="")
    final = request(opener, start)
    if final.status != 200:
        raise AcceptanceError("PKCE/Callback 未完成: " + pretty_error(final))

    redirect_targets = [str(item["to"]) for item in redirects.history]
    require(any("/connect/authorize" in target for target in redirect_targets), "PKCE 流程未经过 /connect/authorize")
    require(any("/thinggateway/auth/iam/callback" in target for target in redirect_targets), "PKCE 流程未回到 ThingsGateway callback")
    print(f"[PASS] PKCE redirect chain 完成，共 {len(redirect_targets)} 次跳转")

    session = request(opener, gateway + "/thinggateway/auth/iam/session")
    require(session.status == 200, "ThingsGateway SSO session 不可用: " + pretty_error(session))
    try:
        identity = session.json()
    except json.JSONDecodeError as exc:
        raise AcceptanceError("SSO session 端点返回的不是 JSON。") from exc

    require(identity.get("authenticated") is True, "ThingsGateway Cookie 未处于 authenticated 状态")
    require(str(identity.get("identitySource", "")).lower() == "platform", "identity_source 不是 Platform")
    require(bool(identity.get("globalUserId")), "SSO session 缺少 GlobalUserId")
    local_user_id = str(identity.get("localUserId") or "")
    require(local_user_id.isdigit() and int(local_user_id) > 0, "SSO session 没有映射到真实数字 LocalUserId")
    print(
        "[PASS] IAM -> LocalUser 映射成功: "
        f"GlobalUserId={identity.get('globalUserId')} LocalUserId={local_user_id} "
        f"PermissionVersion={identity.get('permissionVersion')}"
    )

    home = request(opener, gateway + "/thinggateway/")
    require(home.status == 200, "ThingsGateway 登录后首页访问失败: " + pretty_error(home))
    print("[PASS] ThingsGateway Blazor 首页使用原生 Cookie 可访问")

    if keep_session:
        print("[INFO] --keep-session 已启用，跳过注销验证。")
        return

    local_logout = request(opener, gateway + "/thinggateway/auth/iam/logout")
    require(local_logout.status == 200, "ThingsGateway 本地注销失败: " + pretty_error(local_logout))
    iam_logout = request(opener, gateway + "/account/logout", method="POST")
    require(iam_logout.status in (200, 204), "IAM browser session 注销失败: " + pretty_error(iam_logout))
    print("[PASS] ThingsGateway + IAM 双注销请求完成")


def main() -> int:
    parser = argparse.ArgumentParser(description="ThingsGateway Phase 8.2 live IAM SSO acceptance")
    parser.add_argument("--profile", choices=("iamprepare", "shadow", "centralized"), required=True)
    parser.add_argument(
        "--gateway",
        default=os.getenv("THINGSGATEWAY_GATEWAY_URL", "http://localhost:5202"),
        help="Public YARP origin; default: %(default)s",
    )
    parser.add_argument("--keep-session", action="store_true", help="Do not perform logout at the end")
    args = parser.parse_args()

    gateway = args.gateway.rstrip("/")
    opener, redirects, _ = build_client()

    print(f"[INFO] Gateway={gateway} Profile={args.profile}")
    status_result = request(opener, gateway + "/thinggateway/auth/iam/status")
    require(status_result.status == 200, "无法读取 ThingsGateway IAM status: " + pretty_error(status_result))
    try:
        status = status_result.json()
    except json.JSONDecodeError as exc:
        raise AcceptanceError("IAM status 端点返回的不是 JSON。") from exc
    check_profile(status, args.profile)
    print("[PASS] Security profile / OIDC contract 与阶段一致")

    if args.profile == "iamprepare":
        run_iamprepare(opener, gateway)
        return 0

    username = os.getenv("THINGSGATEWAY_IAM_USER", "").strip()
    password = os.getenv("THINGSGATEWAY_IAM_PASSWORD", "")
    tenant = os.getenv("THINGSGATEWAY_IAM_TENANT", "").strip() or None
    require(bool(username), "缺少环境变量 THINGSGATEWAY_IAM_USER")
    require(bool(password), "缺少环境变量 THINGSGATEWAY_IAM_PASSWORD")

    run_sso(opener, redirects, gateway, username, password, tenant, args.keep_session)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AcceptanceError as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        raise SystemExit(1)
