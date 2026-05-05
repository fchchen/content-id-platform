# Architecture

Content ID Platform models a simplified automatic content recognition workflow.

## Request Flow

1. A client submits media metadata and a simulated fingerprint hash to `ContentId.Api`.
2. The API validates the request, stores normalized submission state in PostgreSQL, and writes a flexible fingerprint document to MongoDB.
3. The API publishes an identification job to `content-id-job-queue`.
4. `content-match-worker-go` consumes the job, marks it `processing`, and compares the submitted fingerprint with seeded reference assets.
5. The worker writes raw match evidence to MongoDB and normalized match summaries to PostgreSQL.
6. Clients poll the API for submission status and match results.

## Storage Choices

PostgreSQL stores operational records that benefit from relational constraints: submissions, job status, and match summaries.

MongoDB stores document-shaped data: submitted fingerprint payloads, raw match evidence, and reference metadata snapshots.

## Queue Semantics

LocalStack creates:

- `content-id-job-queue`
- `content-id-job-dlq`

The queue uses `maxReceiveCount = 3`. If the worker fails a message three times, SQS moves it to the DLQ for inspection and replay.

The API writes submission state before publishing the queue message. A production platform would usually use a transactional outbox or an `enqueue_failed` recovery workflow so a transient queue outage cannot leave a job orphaned. This demo keeps that tradeoff visible rather than adding a full outbox implementation.

## Observability

The API and worker emit OpenTelemetry traces to the collector. The collector writes traces to its debug exporter and exposes a Prometheus metrics endpoint on port `8889`.

## Production Gaps Intentionally Omitted

- client authentication and API keys
- tenant isolation
- real audio/video fingerprint extraction
- real media file storage
- horizontal autoscaling
- rate limiting
- full CI/CD deployment
- production secrets management
- transactional outbox for exactly-once enqueue recovery
