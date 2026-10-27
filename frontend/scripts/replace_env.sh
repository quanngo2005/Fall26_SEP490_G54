#!/bin/sh
set -eu

: "${API_URL:?Set the API origin for host-based routing}"
APP_VERSION="${APP_VERSION:-unknown}"
USE_MOCK_API="${USE_MOCK_API:-false}"

find /usr/share/nginx/html -type f -name '*.js' -exec sed -i \
  -e "s|__API_URL__|${API_URL}|g" \
  -e "s|__APP_VERSION__|${APP_VERSION}|g" \
  -e "s|__USE_MOCK_API__|${USE_MOCK_API}|g" {} +
