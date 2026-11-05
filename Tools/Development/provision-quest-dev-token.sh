#!/usr/bin/env bash
set -euo pipefail

package="com.meta.hackathon.thinkingspace"
remote_directory="/sdcard/Android/data/${package}/files/ThinkingSpace"
remote_file="${remote_directory}/dev-token.txt"
temporary_file=""

cleanup() {
  if [[ -n "${temporary_file}" && -f "${temporary_file}" ]]; then
    rm -f -- "${temporary_file}"
  fi
  unset token || true
}
trap cleanup EXIT INT TERM

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

command -v adb >/dev/null 2>&1 || fail "adb was not found on PATH. Install Android platform tools and open a new terminal."

device_output="$(adb devices | tail -n +2 | sed '/^[[:space:]]*$/d')"
device_count="$(printf '%s\n' "${device_output}" | sed '/^[[:space:]]*$/d' | wc -l | tr -d ' ')"
authorized_count="$(printf '%s\n' "${device_output}" | awk '$2 == "device" { count++ } END { print count+0 }')"
[[ "${device_count}" == "1" && "${authorized_count}" == "1" ]] || fail "Connect exactly one authorized Quest and verify it with 'adb devices'."

adb shell pm path "${package}" >/dev/null 2>&1 || fail "Thinking Space (${package}) is not installed. Run the app once before provisioning."

IFS= read -r -s -p "Enter the privately supplied Thinking Space development token: " token
printf '\n'
[[ -n "${token//[[:space:]]/}" ]] || fail "No token was supplied."

temporary_file="$(mktemp "${TMPDIR:-/tmp}/thinking-space-token.XXXXXX")"
chmod 600 "${temporary_file}"
printf '%s' "${token}" > "${temporary_file}"
unset token

adb shell mkdir -p "${remote_directory}" >/dev/null
adb push "${temporary_file}" "${remote_file}" >/dev/null
adb shell test -s "${remote_file}" || fail "The token file was copied but could not be verified as nonempty."

printf 'Development authentication was provisioned successfully. Restart Thinking Space.\n'
printf 'Quest location: %s\n' "${remote_file}"
