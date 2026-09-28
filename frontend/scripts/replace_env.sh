#!/bin/sh
set -eu

API_URL="${API_URL:-http://localhost:5000/api}"
APP_VERSION="${APP_VERSION:-unknown}"

find /usr/share/nginx/html -type f -name '*.js' -exec sed -i \
  -e "s|__API_URL__|${API_URL}|g" \
  -e "s|__APP_VERSION__|${APP_VERSION}|g" {} +
