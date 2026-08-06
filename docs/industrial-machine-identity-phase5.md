# ThingsGateway machine identity (Phase 5)

## Goal

Phase 5 gives ThingsGateway a first-party machine identity for low-frequency platform and
IoT control-plane calls without coupling industrial acquisition to IAM availability.

```text
ThingsGateway background worker
  -> IAM /connect/token
  -> client_credentials
  -> cached service access token
  -> platform/control-plane calls

PLC / Modbus / OPC UA / device driver / telemetry loop
  -> local runtime path
  -> no IAM call per sample
```

## 1. IAM bootstrap

IAM registers confidential client:

```text
client_id = thingsgateway-service
```

Configure a strong secret through deployment configuration:

```text
Iam__BootstrapThingsGatewayClientSecret=<secret>
```

Never commit the real secret.

The current Phase 5 client may request these first-party scopes:

```text
industrial-platform
iam.resources.sync
iot.gateway.connect
iot.telemetry.write
iot.device.report
```

Use the minimum scope required by each outbound integration. The default background identity
uses only:

```text
iot.gateway.connect
```

## 2. ThingsGateway configuration

Default `appsettings.json` keeps machine identity disabled:

```text
IndustrialMachineIdentity:Enabled = false
```

Enable it through environment/deployment configuration:

```text
IndustrialMachineIdentity__Enabled=true
IndustrialMachineIdentity__ClientId=thingsgateway-service
IndustrialMachineIdentity__ClientSecret=<secret>
IndustrialMachineIdentity__Scope=iot.gateway.connect
```

If `IndustrialMachineIdentity__ClientSecret` is omitted, the provider may reuse:

```text
Security__ResourceSync__ClientSecret
```

when both operations intentionally share the same ThingsGateway confidential client.

## 3. Runtime behavior

`IThingGetWayMachineTokenProvider` caches the client-credentials token and renews it before
expiry. `ThingGetWayMachineIdentityWorker` performs warm-up/renewal in the background.

Failure behavior is deliberate:

```text
IAM reachable      -> token cached / status Authenticated=true
IAM unavailable    -> Warning + status error + retry
PLC acquisition    -> continues
protocol drivers   -> continue
local telemetry    -> continues
```

Do not call `GetAccessTokenAsync` from every telemetry write or PLC polling iteration.
Use it only when a platform/upstream call actually requires a machine access token, then
reuse the cached result.

## 4. Status endpoint

An authenticated operator can inspect machine identity state without exposing the token:

```text
GET /api/platform/machine-identity/status
```

The response contains:

```text
Enabled
Authenticated
ClientId
Scope
TokenExpiresAt
LastSuccessAt
LastError
```

The access token and client secret are never returned.

## 5. Human authorization remains separate

Phase 5 does not migrate ThingsGateway operator accounts to IAM. Default human security stays:

```text
Authentication = Local
Authorization  = Local
```

The previous synthetic Shadow auto-create behavior has been disabled:

```text
ShadowUserProvisioning = RequirePreProvision
```

This prevents accidental creation of fake local identities if a future Shadow profile is
enabled. Human IAM migration should be handled only when a real mapping to existing
ThingsGateway users/roles is defined.

## 6. Health and Gateway

ThingsGateway already exposes the platform traffic-health endpoint, so YARP may actively
probe:

```text
/health/traffic
```

Machine identity failure by itself is not a reason to mark the acquisition gateway dead.
The health decision must continue to represent the service/runtime's ability to handle
traffic, not the temporary availability of IAM.

## 7. Production checklist

Before enabling machine identity at a site:

- use a unique strong `BootstrapThingsGatewayClientSecret`;
- inject the same secret into ThingsGateway deployment configuration;
- start with `iot.gateway.connect` only;
- verify `/api/platform/machine-identity/status` becomes authenticated;
- stop IAM temporarily and verify local acquisition remains active;
- restart IAM and verify the worker recovers automatically;
- only add `iot.telemetry.write` or `iot.device.report` when a concrete outbound API uses them.
