# TODO

- Security: TrustedCommandLoader hashes bytes but runs Assembly.LoadFrom(path). Load the exact hashed
  bytes before enabling any model-modifying command.
- Dev workflow: every Core change rebuilds Commands and changes its hash; design a dev-only allowlist
  flow that never applies to production models.
- Pin System.Reflection.Metadata to 7.0.0 to match Tekla 2025 bin (7.0.22.51805); Immutable follows.
  Requires rebuild + hash re-approval.
- Disposable-model guard path is hard-coded; move it to config (keep the guard).
