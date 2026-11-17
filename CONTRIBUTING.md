# Contributing

Thank you for helping. **Issues are welcome**: bugs in the libraries, mistakes or gaps in the specification, samples
that don't work, ideas for the contract. Please say which package version and which RetroEye version you have, and
what you saw.

Pull requests are welcome too, within these rules:

* **The contract is versioned** (docs/API.md, "Versioning and compatibility"). Within contract **1.x, changes are
  additive only**: never remove, rename or retype a member, event, message, request field, error code or part, and
  never change its meaning or units. Anything that would break a client waits for a contract 2, published beside 1.
* **The libraries follow semantic versioning**: 1.x speaks contract 1, their public surface only grows within 1.x,
  and their assembly version stays 1.0.0.0 for all of 1.x. The tests check the surface against
  `Api/Tests/RetroEye.Api.Tests/ApiBaseline`; after a deliberate addition, `RETROEYE_WRITE_API_BASELINE=1 dotnet test`
  writes it afresh, and the change is reviewed line by line.
* The golden lists in `docs/api` are never edited by hand, and `contract-1.0.golden.txt` never changes.
* Every public member has a `///` summary (a missing one fails the build), and `dotnet test` passes.
* A change to the contract itself needs RetroEye to implement it, so it starts as an issue.

By contributing you agree that your contribution is licensed under the MIT licence of this repository.
