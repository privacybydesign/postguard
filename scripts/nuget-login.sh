#!/usr/bin/env bash
#
# NuGet trusted publishing, without the action.
#
# Trades the job's GitHub OIDC token for a short-lived nuget.org API key and
# writes it to the step's `api_key` output. This is what `NuGet/login` does;
# the privacybydesign organization only runs GitHub-owned, verified and
# allowlisted actions, and a workflow that names any other action does not
# start at all -- which in delivery.yml would take the release and both image
# publishes down with it. Same reasoning as .github/actions/release-plz.
#
# The job needs `id-token: write`. nuget.org matches the token against a
# trusted-publishing policy naming this repository, the workflow file and the
# job's `environment:`; NUGET_USER is the nuget.org account that created that
# policy (the creator, not the owning organization).
#
# Exit codes: 0 key written; 1 nuget.org refused the exchange (no matching
# policy, wrong user); 2 could not find out (no OIDC in this job, GitHub's
# token endpoint failed, malformed response).
#
# Usage:
#   NUGET_USER=<account> scripts/nuget-login.sh
#
set -euo pipefail

audience=${NUGET_AUDIENCE:-https://www.nuget.org}
token_service=${NUGET_TOKEN_SERVICE_URL:-https://www.nuget.org/api/v2/token}

if [[ -z "${NUGET_USER:-}" ]]; then
  echo "::error::NUGET_USER is empty: set the secret on the job's environment" >&2
  exit 2
fi
if [[ -z "${ACTIONS_ID_TOKEN_REQUEST_TOKEN:-}" || -z "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" ]]; then
  echo "::error::GitHub OIDC is not available: the job needs \`id-token: write\`" >&2
  exit 2
fi

oidc_token=$(curl -sSf --retry 3 \
  -H "Authorization: Bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
  "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=$(jq -rn --arg a "$audience" '$a|@uri')" |
  jq -r '.value // empty') || true
if [[ -z "$oidc_token" ]]; then
  echo "::error::GitHub did not return an OIDC token for audience $audience" >&2
  exit 2
fi
echo "::add-mask::$oidc_token"

body=$(mktemp)
trap 'rm -f "$body"' EXIT
status=$(curl -sS -o "$body" -w '%{http_code}' -X POST "$token_service" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $oidc_token" \
  -H 'User-Agent: postguard-delivery' \
  --data "$(jq -cn --arg u "$NUGET_USER" '{username: $u, tokenType: "ApiKey"}')") || {
  echo "::error::could not reach $token_service" >&2
  exit 2
}

if [[ "$status" != "200" ]]; then
  reason=$(jq -r '.error // empty' "$body" 2>/dev/null || true)
  echo "::error::nuget.org refused the token exchange (HTTP $status): ${reason:-$(head -c 300 "$body")}. Check the trusted-publishing policy (repository, workflow file, environment) and that NUGET_USER is the policy's creator." >&2
  exit 1
fi

api_key=$(jq -r '.apiKey // empty' "$body" 2>/dev/null || true)
if [[ -z "$api_key" ]]; then
  echo "::error::nuget.org answered 200 without an apiKey" >&2
  exit 2
fi
echo "::add-mask::$api_key"
echo "api_key=$api_key" >>"$GITHUB_OUTPUT"
echo "Exchanged the OIDC token for a NuGet API key."
