# This branch is the Beta build

You are on the `beta` branch. This branch **is** the Beta environment's deployable artifact —
it is not a folder or a hidden page inside production.

- Published by: `.github/workflows/deploy-beta.yml` (refuses to run from any branch other than `beta`)
- Target: the Beta hostname configured server-side (`settings.beta_host` + `settings.beta_path`,
  see the admin panel's **Beta** tab) — initially
  `betatester.get-aether.de/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis`
- Difference from `main`: `public/aether-config.js` sets `betaDeployment: true` so the UI can
  label itself as Beta. Access is still decided by the Worker (session + verified email +
  Tester role), never by this branch, the URL, or the flag.

Promoting a change to production: open a PR `beta -> main` (or `git cherry-pick` onto `main`).
Production only ever deploys from `main`.
