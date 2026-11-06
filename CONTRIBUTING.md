# Contributing to SpanFlow

Thanks for helping. Bug reports, documentation fixes and pull requests are all welcome.

## Before you start

- **Bugs:** open an issue with the version (`VERSION` file or the console's About panel), what you did, what you
  expected and what happened. Logs help; remove secrets from them first.
- **Features:** open an issue to discuss the idea before writing a large change, so it fits the direction of
  the project.
- **Security problems:** do not open a public issue. Follow [SECURITY.md](SECURITY.md).

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep each pull request to one change, with tests for new behaviour.
3. Run the tests for every solution you touched (see the README) and, for the admin console,
   `npm test` and `npm run build` in `src/Platform/platform-admin`.
4. Describe what changed and why in the pull request.

## How changes land

SpanFlow is developed in a private repository and published here on each release. A merged pull request is
applied to the private repository by a maintainer and appears here with the next release, credited to you in
the release notes.

## Licence of contributions

By submitting a contribution you agree that it is licensed under the same terms as the project
([LICENSE.md](LICENSE.md)) and that the maintainer may also distribute it under the commercial licence used for
paid editions. If you cannot agree to that, please open an issue instead of a pull request.
