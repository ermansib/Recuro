# Event schemas

One JSON Schema per event type, named `<type>.schema.json` (for example
`recruitment.mrf.submitted.v1.schema.json`). The producer adds the schema in the same PR that first
publishes the event; the CloudEvents `dataschema` attribute points here (`events/<type>.schema.json`).

Rules (RCU-PLT-003):

- Names come only from the catalog in `BuildingBlocks/src/Recuro.BuildingBlocks.Application/IntegrationEvents/EventTypes.cs`.
- Adding an optional field is compatible. Removing or renaming a field, or changing its meaning, is a
  new major type (`…v2`), published alongside `v1` until every consumer has moved.
- Payloads are camelCase JSON and never carry PII that the consumer doesn't need (ids, not names or emails).
