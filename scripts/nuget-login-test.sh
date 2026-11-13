#!/usr/bin/env bash
#
# Regression tests for scripts/nuget-login.sh, covering its exit-code contract:
# 1 means nuget.org refused the exchange, 2 means the script could not find
# out. The script only ever runs for real on a release, so this is the only
# place its branches are exercised before that.
#
# Entirely offline: `curl` is a stub on PATH that answers from the scenario in
# STUB_SCENARIO and records the requests it was given.
#
# Usage:
#   scripts/nuget-login-test.sh
#
set -euo pipefail

root=$(git rev-parse --show-toplevel)
login="$root/scripts/nuget-login.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

mkdir "$tmp/bin"
cat >"$tmp/bin/curl" <<'STUB'
#!/usr/bin/env bash
# Two calls reach this stub: the GET for GitHub's OIDC token (no -o) and the
# POST to the token service (-o <file> -w '%{http_code}').
out=""
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  [[ "${args[i]}" == "-o" ]] && out=${args[i + 1]}
done
printf '%s\n' "$*" >>"$STUB_LOG"
if [[ -z "$out" ]]; then
  case "$STUB_SCENARIO" in
    oidc-down) exit 22 ;;
    oidc-empty) echo '{}' ;;
    *) echo '{"value":"oidc-token"}' ;;
  esac
  exit 0
fi
case "$STUB_SCENARIO" in
  refused) echo '{"error":"no matching policy"}' >"$out"; printf 401 ;;
  no-key) echo '{}' >"$out"; printf 200 ;;
  unreachable) exit 7 ;;
  *) echo '{"apiKey":"the-key"}' >"$out"; printf 200 ;;
esac
STUB
chmod +x "$tmp/bin/curl"

pass=0
fail=0

# run <name> <expected exit> <scenario> [VAR=value ...]
run() {
  local name=$1 want=$2 scenario=$3
  shift 3
  : >"$tmp/log"
  : >"$tmp/output"
  local got=0
  env PATH="$tmp/bin:$PATH" STUB_SCENARIO="$scenario" STUB_LOG="$tmp/log" \
    GITHUB_OUTPUT="$tmp/output" NUGET_USER=someone \
    ACTIONS_ID_TOKEN_REQUEST_TOKEN=request-token \
    ACTIONS_ID_TOKEN_REQUEST_URL='https://oidc.invalid/token?api-version=2' \
    "$@" "$login" >"$tmp/stdout" 2>"$tmp/stderr" || got=$?
  if [[ "$got" == "$want" ]]; then
    pass=$((pass + 1))
    echo "ok   $name"
  else
    fail=$((fail + 1))
    echo "FAIL $name: exit $got, want $want"
    sed 's/^/     /' "$tmp/stderr"
  fi
}

check() { # <name> <command...>
  local name=$1
  shift
  if "$@"; then
    pass=$((pass + 1))
    echo "ok   $name"
  else
    fail=$((fail + 1))
    echo "FAIL $name"
  fi
}

run "a granted exchange exits 0" 0 ok
check "  and writes the key to the step output" grep -qx 'api_key=the-key' "$tmp/output"
check "  and masks the key" grep -qx '::add-mask::the-key' "$tmp/stdout"
check "  and asks GitHub for the nuget.org audience" \
  grep -q 'audience=https%3A%2F%2Fwww.nuget.org' "$tmp/log"
check "  and names the policy creator in the exchange" \
  grep -q '{"username":"someone","tokenType":"ApiKey"}' "$tmp/log"

run "a refusal from nuget.org exits 1" 1 refused
check "  and carries nuget.org's reason" grep -q 'no matching policy' "$tmp/stderr"
check "  and writes no key" test ! -s "$tmp/output"

run "a 200 without a key exits 2" 2 no-key
run "an unreachable token service exits 2" 2 unreachable
run "no OIDC token from GitHub exits 2" 2 oidc-empty
run "GitHub's token endpoint failing exits 2" 2 oidc-down
run "a job without id-token: write exits 2" 2 ok ACTIONS_ID_TOKEN_REQUEST_URL=
run "an empty NUGET_USER exits 2" 2 ok NUGET_USER=

echo
echo "$pass passed, $fail failed"
[[ "$fail" == 0 ]]
