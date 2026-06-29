# Contributing to SystemCare

Thanks for your interest in improving SystemCare! Contributions are welcome — bug
reports, feature ideas, and pull requests.

> **Heads up:** every change to `main` goes through a pull request and **requires the
> maintainer's review and approval before it can be merged.** Anyone can open a PR;
> only the maintainer can merge it.

## Ways to contribute

- **Report a bug** or **request a feature** — open an issue using the templates.
- **Submit code** — fork the repo, make your change on a branch, and open a pull
  request against `main`.

## Pull request flow

1. **Fork** the repository and create a branch from `main`
   (e.g. `feature/sensors-fan-curve` or `fix/registry-scan-crash`).
2. Make your change. Keep it focused — one logical change per PR.
3. Build and test locally:
   ```powershell
   dotnet build SystemCare.sln -c Release   # must be 0 warnings / 0 errors
   dotnet test SystemCare.sln               # all tests must pass
   ```
4. Match the **surrounding style** (the codebase favours small, well-named methods,
   MVVM with CommunityToolkit, and the existing cyberpunk design-system resources).
   Add unit tests for any new pure logic (see `tests/SystemCare.Tests`).
5. Open the PR, fill in the template, and add screenshots for any UI change.
6. The maintainer ([@WifeInLantern](https://github.com/WifeInLantern)) is auto-requested
   as a reviewer (via `CODEOWNERS`) and must approve before merge.

## Conventions

- **.NET 8 / WPF**, MVVM (`[ObservableProperty]` / `[RelayCommand]`), DI via the
  container in `App.xaml.cs`.
- Keep the build at **0 warnings**. Prefer extracting testable pure logic into
  `Helpers/` and unit-testing it.
- Everything stays **local** — no network telemetry.

## Licensing of contributions

SystemCare is distributed under the terms in [EULA.txt](EULA.txt) (free, source-
available; not an OSI open-source license). By submitting a contribution you agree
that it is licensed to the maintainer for inclusion in SystemCare.
