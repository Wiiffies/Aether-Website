# Beta environment — separate branch, separate deployment

The Beta is **not** a page inside production. It is a second, independently deployable
environment with its own branch, its own deployment workflow, its own environment secrets and
its own server-side configuration.

```
main  ──► production / Alpha  ──► get-aether.de            (Pages + aether-api worker)
                                          │
beta  ──► Beta deployment      ──► betatester.get-aether.de/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis
```

## What makes it separate

| Concern | Production | Beta |
| --- | --- | --- |
| Branch | `main` | `beta` |
| Workflow | `.github/workflows/deploy-production.yml` | `.github/workflows/deploy-beta.yml` (refuses any ref other than `beta`) |
| GitHub environment | `github-pages` | `beta` (own secrets/vars — nothing shared by default) |
| Host | `get-aether.de` | `betatester.get-aether.de` + the long path |
| Secrets | Worker secrets on `aether-api` | its own Worker/KV/D1 secrets; production secrets are never exposed to Beta |
| Data | `aether-db` | its own D1/KV (see "Data isolation" below) |

Publishing the Beta never publishes production, and a commit that only exists on `beta` can
never reach production through the production workflow.

## Access control (the part that actually protects it)

The long URL only reduces accidental discovery — it is **not** a security mechanism. Every Beta
request is checked server-side, in this order:

1. a valid session (HttpOnly, hashed-at-rest token);
2. a **verified email address** (required whenever email delivery is configured);
3. the **Tester** role (or Admin) from the server-side `users.role` column;
4. ownership checks on any resource the request touches.

A normal, fully verified customer who somehow learns the URL still gets `403` from
`/api/beta/status` and sees no URL at all from `/api/beta/access`. The frontend never decides
access: it only renders what the API reports.

Optional cross-host hand-off (for a Beta deployment on a different hostname that cannot read
the production cookie):

1. `POST /api/beta/ticket` → single-use ticket, 120 s, stored hashed server-side;
2. the Beta host calls `POST /api/beta/redeem` with the ticket → normal session there.

The ticket is single-use, expiring, and still requires a Tester account with a verified email.

## Configuring the Beta address

The address is server-side configuration (D1 `settings.beta_host` / `settings.beta_path`, with
`BETA_HOST` / `BETA_PATH` env fallbacks). Admins change it from the admin panel's **Beta** tab:

1. admin enters the new host + path, **and** re-enters their password;
2. the Worker validates the values (host must be a subdomain of `get-aether.de`, the path must
   be plain URL characters) and creates a 64-hex, 30-minute, single-use confirmation token,
   stored hashed;
3. a confirmation email goes to the admin account (when email delivery is configured);
4. the admin opens the link (`/admin.html?betaToken=…`), and the panel POSTs the token back;
5. the Worker re-checks that the account is *still* an admin, consumes the token, writes the
   setting, and records an audit entry (`beta.domain.request` → `beta.domain.confirm`).

Nothing about the Beta address can be changed by a browser request alone.

## Data isolation

- Beta-only writes go to Beta-only tables (`beta_feedback`), never into production tables.
- The Beta deployment should run against its own D1 database and KV namespace, and set
  `AETHER_ENV=beta` so `/api/beta/status` reports the environment honestly.
- Beta deployments must never be given production secrets (payment keys, webhook secrets,
  email keys). Copy the *schema*, not the credentials.
- Experimenting in the Beta must not be able to corrupt production rows: no Beta code path
  writes to `orders`, `conversations` or `users` beyond the authenticated account it owns.

## Manual steps still required

- [ ] `MANUAL` — DNS: create the `betatester` record for the Beta host.
- [ ] `MANUAL` — route `betatester.get-aether.de/api/*` to the Beta Worker, and serve the Beta
      branch build at the long path.
- [ ] `MANUAL` — create the Beta Worker + its own KV namespace and D1 database, then apply the
      same schema/settings.
- [ ] `MANUAL` — set the `beta` GitHub environment's `BETA_TARGET` / `BETA_DEPLOY_TOKEN` (or
      point `deploy-beta.yml` at your chosen target).
- [ ] `MANUAL` — push the `main` and `beta` branches to GitHub and add the repository remote
      (this working copy had no git repository at all, so it was initialised locally).

## Shipping a change from Beta to production

The Beta branch is a testing ground, not a deployment path. When a Beta change is proven:

```
git checkout main
git cherry-pick <beta commit>     # or open a PR beta -> main
# production workflow runs the same checks, then publishes
```
