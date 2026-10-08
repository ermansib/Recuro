# Notification service (port 5104)

Stories: RCU-NTF-001..004 (backend pack RCU-BKD-001), FRD §5.6 event matrix, plus the live bell
stream of RCU-GTW-003. Database: `recuro_notification`.

## What it does

- **Event matrix (NTF-001).** `Domain/Matrix/NotificationMatrix.cs` is FRD §5.6 as data: event type →
  template → recipients (a role, the actor, a user named in the payload, or the subject's initiator) →
  channels. One event can make bell items and emails. Fan-out is idempotent (inbox + a unique index on
  event, template and recipient).
- **Bell and email centre (NTF-003).** Responses are the frontend's `AppNotification` and
  `EmailMessage`, field for field. Role-wide items are allowed only for HR staff roles; candidates and
  employees only ever get items addressed to them. Read state is per person.
- **Email (NTF-002).** Emails queue in the database and a background job sends them over SMTP with
  MailKit (MIT): Mailpit locally, any SMTP relay in production. Three attempts with exponential
  backoff, then `Failed` (the dead letter) and `notification.email.failed.v1`. Bounced addresses are
  suppressed. Templates are plain text, so event data can never become HTML in an inbox.
- **Candidate emails (CAR-006, CAR-002, OFR-006).** Regret emails (scheduled for `regretSendAt`),
  application acknowledgements and offer chases go to the candidate record named by `candidateId`.
  The address comes from Candidate's `GET /api/v1/candidates/{id}`, called as this service
  (RCU-AUT-005; the service role sees name and email unmasked). An unknown candidate or a masked
  address is logged as suppressed; Candidate being down retries the event.
- **Staff recipients.** A role's people come from Identity's `GET /api/v1/identity/users?role=`, also
  called as this service, and are remembered locally. When Identity can't answer, the local directory
  (Identity's events and people's own sign-ins) stands in.
- **Delivery log (NTF-004).** Every email row keeps template, template version, matrix version,
  recipient, timestamps, provider message id and status. Rows are never deleted.
- **Live stream (GTW-003).** `GET /stream/notifications` is Server-Sent Events, proxied by the gateway.
  Writers call `pg_notify` inside their transaction; every replica `LISTEN`s and wakes its streams,
  which read what's new from the database. Reconnecting with `Last-Event-ID` resumes without loss;
  a heartbeat goes out every 15 s.

## API (through the gateway on :5100)

| Method | Path | Who | Frontend |
|---|---|---|---|
| GET | `/api/v1/notifications?filter=all\|unread&limit=50&before=<id>` | any signed-in persona | `listNotifications` |
| GET | `/api/v1/notifications/unread-count` | any | badge (`{ notifications, emails }`) |
| POST | `/api/v1/notifications/read` `{ ids }` or `{ all: true }` | any | `markNotificationRead`, `markAllRead` |
| GET | `/api/v1/notifications/emails?filter=…` | any | `listEmails` |
| POST | `/api/v1/notifications/emails/read` `{ ids }` or `{ all: true }` | any | `markEmailRead`, `markAllRead` |
| GET | `/api/v1/notifications/delivery-log?status=&templateKey=&sourceEventId=` | HR staff | — |
| POST | `/api/v1/notifications/email-bounces` `{ address, reason }` | service accounts | — |
| GET | `/stream/notifications` (SSE, `Last-Event-ID`) | any | live bell |

The browser's `EventSource` can't send a bearer token, so the portal should use a fetch-based SSE
client (for example `@microsoft/fetch-event-source`, MIT) with the `Authorization` header. Tokens
never go in the URL.

## Events

Consumes the matrix events (see `NotificationMatrix.SubscribedEventTypes`), plus
`identity.user.provisioned.v1` and `identity.role.changed.v1` to keep a local directory of who holds
which role. Those events carry no PII, so a person's name and email are remembered from their token
the first time they open the bell (just in time, like Identity does). Until then, mail to them is
logged as suppressed. Publishes
`notification.created.v1`, `notification.email.dispatched.v1` and `notification.email.failed.v1`
(schemas in `contracts/events`).

## Run it

```bash
cd services
docker compose up -d postgres redis rabbitmq mailpit
dotnet run --project Notification/src/Recuro.Notification.Api --launch-profile http   # http://localhost:5104
```

Sent mail shows up in Mailpit at http://localhost:8025.
