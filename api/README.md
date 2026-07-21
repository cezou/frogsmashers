# api/ — FrogSmashers accounts API

The accounts backend (issue #27): a Go + chi HTTP API that verifies an
identity-provider credential and issues the home-grown JWT used as
Bearer token by every later endpoint (`/queue`, `/match-result`,
`/leaderboard`, `/profile/{id}`). Design basis:
`docs/research/STEAM.md` §2.2, §2.7, §4.

## Identity abstraction

`internal/identity.Provider` — a credential goes in, a verified
provider-scoped player id comes out:

- **`ugs`** (now): the Unity client signs in anonymously to Unity
  Gaming Services and sends its UGS access token; the API verifies it
  against Unity's JWKS (RS256, issuer + `upid:<projectId>` audience)
  and uses the token `sub` (the UGS player id) as `provider_id`.
- **`steam`** (later): verify a Steam auth ticket via
  `AuthenticateUserTicket`; the user's `id` will BE their steamid64.
  No change to the users table or JWT layer.

`users.id` is a 64-bit key: generated ids for non-Steam users live in
`[1, 2^52)`, provably below the steamid64 range (≥ ~7.66e16) and
float64-safe. Ids travel as **strings** in JSON.

## Endpoints

| Route | Purpose |
|---|---|
| `POST /auth/register` | verify credential, create user → `201 {user_id, jwt, expires_at}`; `409` if already registered |
| `POST /auth/login` | verify credential, touch `last_login_at` → `200`; `404` if not registered (client falls back to register) |
| `GET /me` | Bearer-protected echo of the authenticated user id |
| `GET /healthz` | DB ping → `200` / `503` |

Request body for both auth routes:
`{"provider": "ugs", "token": "<UGS access token>"}`.
`401 invalid_credential` when the provider rejects the token, `400`
for malformed JSON / unknown provider.

Note: UGS access tokens expire after ~1 h — the client must send a
fresh one (anonymous sign-in is silent, so this is free). Our JWT
lasts `JWT_TTL` (default 24 h).

## Configuration (env)

`DATABASE_URL`, `JWT_SECRET`, `UGS_PROJECT_ID` required; optional
`LISTEN_ADDR` (default `127.0.0.1:8080`), `JWT_TTL` (default `24h`),
`UGS_ISSUER`, `UGS_JWKS_URL`. On the VM these come from
`/etc/frogsmashers/db.env` + `/etc/frogsmashers/api.env` via systemd.

## Test

```sh
go test ./...          # unit tests (JWKS mocked with httptest)
gofmt -l . && go vet ./...
```

Postgres integration test (optional, needs docker):

```sh
docker run --rm -d -p 5433:5432 -e POSTGRES_PASSWORD=test \
    --name frog-test-pg postgres:17
TEST_DATABASE_URL=postgres://postgres:test@localhost:5433/postgres \
    go test ./...
docker rm -f frog-test-pg
```

## Deploy

`./infra/provision.sh api` — cross-compiles a static linux/arm64
binary and installs it on the OCI VM as the `frogsmashers-api`
systemd service, bound to localhost (HTTPS exposure via Cloudflare is
a separate task). See `infra/README.md`.
