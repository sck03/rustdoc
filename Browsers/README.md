# Browsers

Place Chromium or Chrome for Testing here when report PDF generation is enabled.

Use `scripts/provision-chrome-for-testing.ps1` for supported desktop and browser-server packages. Its default mode pins Chrome for Testing to the Chromium version declared for the repository's Microsoft.Playwright package; `-Channel` and `-Version` are explicit compatibility-diagnostic overrides.

This directory is copied to the program root as `Browsers/`. It is not business data and should not be moved into `App_Data`.
