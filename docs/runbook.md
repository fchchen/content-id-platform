# Runbook

## Start Locally

```bash
docker compose up --build
```

The stack starts PostgreSQL, MongoDB, LocalStack, OpenTelemetry Collector, the .NET API, and the Go worker.

## Validate the Flow

```bash
./scripts/healthcheck.py
```

The script waits for the API, submits a known fingerprint, polls for completion, and prints the final submission plus match response.

## Inspect LocalStack Resources

```bash
docker compose exec localstack awslocal sqs list-queues
docker compose exec localstack awslocal sns list-topics
docker compose exec localstack awslocal s3 ls
```

## Inspect the DLQ

```bash
docker compose exec localstack awslocal sqs receive-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/content-id-job-dlq \
  --attribute-names All \
  --message-attribute-names All
```

## Replay a Failed Job

1. Receive the DLQ message and copy its `Body`.
2. Send that body back to `content-id-job-queue`.
3. Delete the original DLQ message after verifying it was replayed.

```bash
docker compose exec localstack awslocal sqs send-message \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/content-id-job-queue \
  --message-body '<copied-body>'
```

## Common Issues

- API returns unhealthy: confirm PostgreSQL and MongoDB containers are healthy.
- Jobs stay queued: confirm LocalStack created `content-id-job-queue` and the worker container is running.
- No matches returned: submit one of the seeded hashes from `data/reference-assets/reference-assets.json`.
- Worker retries a message: inspect worker logs and DLQ state.
