# Phase 8.2 ThingsGateway Web IAM SSO — Live Acceptance

This runbook is the runtime acceptance gate after the code/configuration work in Phase 8.2.
It deliberately separates **code-ready** from **environment-verified** status.

## 1. Runtime topology

Start the services in this order:

```text
Industrial.IAM.Api       http://localhost:5100
        ↓
Aneiang.Yarp             http://localhost:5202
        ↓
ThingsGateway.Server     http://localhost:5000
```

All browser-style acceptance traffic must enter through YARP at `http://localhost:5202`.
Do not test the browser flow by mixing `localhost:5100`, `localhost:5000`, and `localhost:5202`
in the browser because IAM Session and PKCE cookies are intentionally exercised on the public
Gateway origin.

## 2. Prerequisites for Shadow/Centralized

Prepare one real IAM test account and one real enabled ThingsGateway local user.

Required state:

1. IAM user is enabled and not locked.
2. IAM user has `SystemAccess(THINGSGATEWAY)=true`.
3. IAM user is explicitly bound to an existing numeric ThingsGateway `SysUser.Id` through
   `/api/security/iam-bindings` while a local ThingsGateway SuperAdmin session is active.
4. The bound local ThingsGateway user keeps its existing Role / Org / Module / DataScope setup.
5. `industrial-thingsgateway-web` exists and is enabled in IAM.
6. Redirect URI is exactly:

   `http://localhost:5202/thinggateway/auth/iam/callback`

For production, configure the same HTTPS callback URI in both IAM and ThingsGateway.

## 3. IamPrepare acceptance

Start ThingsGateway with:

```text
INDUSTRIAL_SECURITY_PROFILE=IamPrepare
```

Run:

```bash
python scripts/validate_iam_sso_live.py --profile iamprepare
```

Expected:

- `IndustrialIamWeb.Enabled=false`
- Authentication=`Local`
- Authorization=`Local`
- Shadow central evaluation disabled
- SystemAccess not required for local login
- `/api/thinggateway/openApi/runtimeInfo/redundancyStatus` reaches the protected ThingsGateway endpoint
  through YARP and returns 401/403 rather than 404

Manual acceptance still required here:

- Existing local username/password login works unchanged.
- Existing local menus/buttons/organizations/device data scope are unchanged.
- IAM resource sync and explicit IAM binding administration are available.

## 4. Shadow acceptance

Start ThingsGateway with:

```text
INDUSTRIAL_SECURITY_PROFILE=Shadow
```

Set test credentials in the shell. Do not place the password on the command line:

### PowerShell

```powershell
$env:THINGSGATEWAY_IAM_USER = "your-test-user"
$env:THINGSGATEWAY_IAM_PASSWORD = "your-test-password"
# optional
$env:THINGSGATEWAY_IAM_TENANT = "your-tenant"

python scripts/validate_iam_sso_live.py --profile shadow
```

### bash

```bash
export THINGSGATEWAY_IAM_USER='your-test-user'
export THINGSGATEWAY_IAM_PASSWORD='your-test-password'
# optional
export THINGSGATEWAY_IAM_TENANT='your-tenant'

python scripts/validate_iam_sso_live.py --profile shadow
```

The live script validates the actual public-origin sequence:

```text
POST /account/login
    ↓
GET /thinggateway/auth/iam/login
    ↓
/connect/authorize (PKCE)
    ↓
/thinggateway/auth/iam/callback
    ↓
SystemAccess(THINGSGATEWAY)
    ↓
explicit IAM -> numeric LocalUserId binding
    ↓
original ThingsGateway AuthService cookie/session
    ↓
GET /thinggateway/auth/iam/session
    ↓
GET /thinggateway/
```

Expected session metadata:

- `authenticated=true`
- `identitySource=Platform`
- `globalUserId` is the IAM user id
- `localUserId` is an existing positive numeric ThingsGateway user id
- `permissionVersion` is present

Shadow business acceptance:

- Original local authorization result remains authoritative.
- Central decision is comparison-only.
- Local role/org/device data scope remains unchanged.
- Exercise at least one local allow / central deny mismatch.
- Exercise at least one local deny / central allow mismatch.
- Confirm comparison telemetry records the mismatch without changing the local Allow/Deny result.

## 5. Centralized acceptance

Start ThingsGateway with:

```text
INDUSTRIAL_SECURITY_PROFILE=Centralized
```

Use the same credential environment variables and run:

```bash
python scripts/validate_iam_sso_live.py --profile centralized
```

Expected:

- Authentication=`Centralized`
- Authorization=`Centralized`
- `RequireSystemAccess=true`
- `ShadowUserProvisioning=RequirePreProvision`
- Browser login goes through IAM automatically when no native cookie exists
- Existing local password flow cannot mint a new ThingsGateway cookie/API token
- IAM permission changes govern platform permission decisions
- Existing local Role / Org / Module / Device DataScope still constrain business data after identity mapping

Required negative cases:

1. Remove `SystemAccess(THINGSGATEWAY)` from the test IAM user → login/callback must fail closed with 403.
2. Restore SystemAccess but remove the explicit IAM→LocalUser binding → callback must fail with 403 and the binding message.
3. Restore the binding but remove a selected IAM permission → matching protected operation must be denied.
4. Disable or lock the IAM user → new IAM session/token usage must fail.
5. Stop IAM temporarily → new authentication and central checks fail closed; ThingsGateway must not silently fall back to local password in Centralized.

## 6. Gateway route regression check

ThingsGateway has mixed native route prefixes:

```text
/openApi/...
/api/...
```

Therefore the YARP route:

```text
/api/thinggateway/{**catch-all}
```

must only remove `/api/thinggateway`; it must **not** add another `/api` prefix.

Correct transform:

```json
"Transforms": [
  { "PathRemovePrefix": "/api/thinggateway" }
]
```

Otherwise:

```text
/api/thinggateway/openApi/...
→ /api/openApi/...
→ 404
```

The live acceptance script explicitly rejects this regression.

## 7. Logout acceptance

After successful SSO:

- ThingsGateway native `Verificat` session is removed.
- Native ThingsGateway Cookie is removed.
- IAM browser session is removed.

The live script performs both server logout requests unless `--keep-session` is specified.

## 8. Rollback

Rollback does not require deleting user bindings.

```text
Centralized
    ↓
Shadow
    ↓
IamPrepare
```

Fastest local-auth rollback:

```text
INDUSTRIAL_SECURITY_PROFILE=IamPrepare
```

Expected after restart:

- local login restored
- central Web SSO disabled
- existing IAM/local binding rows preserved for later retry
- existing ThingsGateway Role / Org / DataScope data unchanged

## 9. Completion gate

Phase 8.2 runtime acceptance is complete only when all of the following have evidence:

- [ ] IamPrepare local-login regression passed
- [ ] Gateway native ThingsGateway API route probe passed
- [ ] Shadow PKCE login passed
- [ ] Shadow IAM→LocalUser mapping passed
- [ ] Shadow local-authoritative mismatch cases passed
- [ ] Centralized PKCE login passed
- [ ] Centralized local-password bypass blocked
- [ ] SystemAccess removal fails closed
- [ ] Binding removal fails closed
- [ ] Selected central permission revoke returns deny
- [ ] Local Org / Device DataScope remains correct
- [ ] IAM outage behavior matches FailClosed design
- [ ] Logout clears both local and IAM sessions
- [ ] Centralized → IamPrepare rollback passed
- [ ] GitHub Actions build status is observed green

Do not mark runtime acceptance complete from static code inspection alone.
