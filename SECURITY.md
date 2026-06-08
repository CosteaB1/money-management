# Security Policy

## Reporting a vulnerability

Please report security issues **privately** — do not open a public issue for a vulnerability.

Use GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) (the **Security → Report a vulnerability** tab on this repository). I'll acknowledge and address valid reports as time allows — this is a personal, best-effort project with no SLA.

## Scope & intended use

This app is **self-hosted and single-user**, and ships **without an authentication layer by design** — it's meant to run on a trusted personal machine or network. Exposing it directly to the public internet is unsupported and not recommended. Reports that amount to "it has no auth when exposed publicly" are out of scope, since that's a documented non-goal.

In-scope examples: SQL injection, data-corruption bugs, dependency vulnerabilities, or anything that lets a local request do something it shouldn't.
