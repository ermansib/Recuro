#!/bin/sh
# Database-per-service (RCU-BKD-001 §1.2): one database and one login per service, and no login can
# connect to another service's database. Local development only; production credentials come from
# the environment or a vault, never from this file.
set -eu

for service in identity config audit notification requisition workflow candidate pipeline \
               interview bgv offer onboarding vendor reporting careers employee; do
  db="recuro_${service}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<SQL
CREATE ROLE ${db} LOGIN PASSWORD '${db}';
CREATE DATABASE ${db} OWNER ${db};
REVOKE CONNECT ON DATABASE ${db} FROM PUBLIC;
SQL
done
