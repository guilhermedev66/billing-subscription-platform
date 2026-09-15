#!/usr/bin/env bash
set -Eeuo pipefail

readonly api_base_url="${API_BASE_URL:-http://localhost:8080}"
readonly web_base_url="${WEB_BASE_URL:-http://localhost:5173}"
readonly timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-120}"
readonly deadline=$((SECONDS + timeout_seconds))

healthy=false
while ((SECONDS < deadline)); do
  if health_response="$(curl --silent --show-error --fail --max-time 5 \
    "$api_base_url/health/ready" 2>/dev/null)" &&
    jq --exit-status '.status == "Healthy"' <<<"$health_response" >/dev/null; then
    healthy=true
    break
  fi

  sleep 2
done

if [[ "$healthy" != true ]]; then
  echo "::error::API readiness check did not become Healthy within ${timeout_seconds}s."
  exit 1
fi

smoke_password="$(openssl rand -base64 24 | tr -d '\n')Aa1!"
echo "::add-mask::$smoke_password"
readonly smoke_password
readonly smoke_email="smoke-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}@example.com"
readonly organization_name="CI Smoke Organization"

register_payload="$(jq --null-input --compact-output \
  --arg organizationName "$organization_name" \
  --arg email "$smoke_email" \
  --arg password "$smoke_password" \
  '{organizationName: $organizationName, email: $email, password: $password}')"
register_response="$(curl --silent --show-error --fail-with-body \
  --header 'Content-Type: application/json' \
  --data "$register_payload" \
  "$api_base_url/api/auth/register")"
registered_organization_id="$(jq --exit-status --raw-output \
  '.user.organizationId | select(type == "string" and length > 0)' \
  <<<"$register_response")"

login_payload="$(jq --null-input --compact-output \
  --arg email "$smoke_email" \
  --arg password "$smoke_password" \
  '{email: $email, password: $password}')"
login_response="$(curl --silent --show-error --fail-with-body \
  --header 'Content-Type: application/json' \
  --data "$login_payload" \
  "$api_base_url/api/auth/login")"
access_token="$(jq --exit-status --raw-output \
  '.token | select(type == "string" and length > 0)' \
  <<<"$login_response")"
echo "::add-mask::$access_token"

organization_response="$(curl --silent --show-error --fail-with-body \
  --header "Authorization: Bearer $access_token" \
  "$api_base_url/api/organizations/current")"
jq --exit-status \
  --arg organizationId "$registered_organization_id" \
  --arg organizationName "$organization_name" \
  '.id == $organizationId and .name == $organizationName' \
  <<<"$organization_response" >/dev/null

curl --silent --show-error --fail --max-time 10 "$web_base_url/" >/dev/null

echo "Production stack smoke test passed."
