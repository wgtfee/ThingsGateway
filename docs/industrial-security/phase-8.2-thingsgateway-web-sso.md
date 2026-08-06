# Phase 8.2 — ThingsGateway Web IAM SSO

## Goal

Unify the human login identity with Industrial IAM without moving ThingsGateway's business authorization model into IAM.

The final responsibility split is:

- IAM: global identity, `SystemAccess(THINGSGATEWAY)`, platform permission assignment and token/session lifecycle.
- ThingsGateway: existing numeric `SysUser` identity, roles, organization hierarchy, modules, `PermissionCodeList`, device/data scope and domain rules.
- Gateway: public browser entry and OIDC paths.
- Telemetry/data acquisition hot path: never performs an IAM call per data point.

## Runtime flow

```text
Browser
  -> /thinggateway/
  -> native ThingsGateway cookie challenge
  -> /thinggateway/auth/iam/signin
  -> POST /account/login (Gateway -> IAM; password never reaches ThingsGateway)
  -> /thinggateway/auth/iam/login
  -> /connect/authorize (Authorization Code + PKCE)
  -> /thinggateway/auth/iam/callback
  -> server-side /connect/token
  -> /api/iam/users/me
  -> /api/iam/access/THINGSGATEWAY
  -> explicit IAM user -> existing numeric LocalUserId binding
  -> IAuthService.LoginTrustedLocalUserAsync(LocalUserId)
  -> original ThingsGateway native cookie/session
  -> original Role / Org / Module / Device DataScope
```

The IAM access token is not written to browser JavaScript/localStorage. It is kept only as an encrypted/HttpOnly native-cookie claim so server-side Industrial.Security calls can re-check IAM permission and SystemAccess. The native ThingsGateway session lifetime is capped below the IAM access-token lifetime.

## Stable OIDC contract

Client:

```text
industrial-thingsgateway-web
```

Local development callback:

```text
http://localhost:5202/thinggateway/auth/iam/callback
```

Production must override both sides with exactly the same HTTPS callback:

```text
IAM:
Iam__ThingsGatewayWebClientRedirectUri=https://platform.example.com/thinggateway/auth/iam/callback

ThingsGateway:
IndustrialIamWeb__PublicAuthority=https://platform.example.com
IndustrialIamWeb__RedirectUri=https://platform.example.com/thinggateway/auth/iam/callback
```

## Phase A — IamPrepare

Use:

```text
INDUSTRIAL_SECURITY_PROFILE=IamPrepare
```

Expected behavior:

- Existing ThingsGateway local login remains authoritative.
- IAM Web SSO is disabled.
- ResourceSync is enabled.
- `thingsgateway-service` machine identity is enabled when its secret is injected.
- A local ThingsGateway SuperAdmin pre-binds IAM users to existing local users through:

```text
GET    /api/security/iam-bindings
POST   /api/security/iam-bindings
DELETE /api/security/iam-bindings/{iamUserId}
```

Never auto-match by username and never copy IAM passwords.

Example binding request:

```json
{
  "iamUserId": "<IAM global user id>",
  "localUserId": "<existing numeric ThingsGateway user id>",
  "userName": "optional",
  "displayName": "optional"
}
```

## Phase B — Shadow

Use:

```text
INDUSTRIAL_SECURITY_PROFILE=Shadow
```

Expected behavior:

- Existing local login remains available for comparison/rollback.
- IAM SSO is available at:

```text
http://localhost:5202/thinggateway/auth/iam/signin
```

- IAM authentication is centralized.
- Local authorization remains authoritative.
- Central permission result is evaluated for migration comparison where Industrial.Security permission metadata is used.
- `SystemAccess(THINGSGATEWAY)` is required.
- Missing explicit local-user binding returns 403.
- Existing local user being disabled, having no module, or belonging to a disabled organization still rejects the IAM login because the native `AuthService` lifecycle is reused.

Compare the same user via local login and IAM login:

- local numeric UserId
- role list
- organization / tenant
- module/menu visibility
- `PermissionCodeList`
- device/data scope
- online-session behavior
- single-login behavior

Do not cut over until the business-visible result is the same.

## Phase C — Canonical permission catalog

Central IAM catalog and current OpenAPI controller metadata use only canonical permissions:

```text
thingsgateway.gateway.view
thingsgateway.gateway.control
thingsgateway.gateway.manage
thingsgateway.gateway.export
```

The central manifest no longer publishes the temporary `thinggateway.*` aliases. Legacy `THINGGATEWAY.*` mapping may remain inside the local mapper only for old local-role compatibility during migration.

## Phase D — Centralized

Use only after Shadow comparison is accepted:

```text
INDUSTRIAL_SECURITY_PROFILE=Centralized
```

Expected behavior:

- Native local-password login is rejected.
- Unauthenticated native-cookie challenges go to `/thinggateway/auth/iam/signin`.
- IAM login requires `SystemAccess(THINGSGATEWAY)`.
- IAM user must be explicitly bound to a real local numeric user.
- Native cookie keeps the original ThingsGateway UserId/OrgId/TenantId/SuperAdmin/session model.
- Central IAM permission checks use the protected platform access token.
- Local organization/device data-scope restrictions continue to apply.
- Existing logout clears the native session and the IAM browser-session cookie when the session originated from IAM.

## Required negative tests

| Scenario | Expected |
| --- | --- |
| IAM account/password invalid | IAM login rejected |
| IAM user disabled/locked | IAM login rejected |
| `SystemAccess(THINGSGATEWAY)=false` | 403 |
| no IAM-to-local binding | 403 |
| bound local user disabled | login rejected |
| bound local user's organization disabled | login rejected |
| bound local user has no module | login rejected |
| IAM permission missing on protected API | 403 |
| local device DataScope excludes device | device remains inaccessible |
| IAM permission present + local DataScope present | allowed |

## Logout test

For an IAM-created native session:

1. Log in through IAM.
2. Use the existing ThingsGateway logout UI or `/thinggateway/auth/iam/logout`.
3. Confirm the native session is gone.
4. Confirm `industrial-iam-session` is also gone.
5. Re-entering ThingsGateway must require IAM authentication again rather than silently restoring the old platform session.

## Rollback

Rollback never deletes user bindings or local business users.

```text
Centralized -> Shadow
```

or:

```text
Centralized/Shadow -> IamPrepare
```

or remove `INDUSTRIAL_SECURITY_PROFILE` to return to the existing local behavior.

Machine identity/resource data already synchronized with IAM can remain; the device acquisition hot path remains independent.

## Local topology

```text
Gateway       http://localhost:5202
IAM           http://localhost:5100
ThingsGateway http://localhost:5000
```

Public browser entry:

```text
http://localhost:5202/thinggateway/
```

Do not use the direct `:5000` address to judge the Gateway-hosted IAM browser flow, because `/account/*` and `/connect/*` are platform routes owned by Gateway/IAM.
